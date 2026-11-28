using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using PDFtoImage;
using SkiaSharp;

namespace BookUniversity;

public partial class MainWindow : Window
{
    private readonly LibraryStore _store = new();
    private readonly BookmarkStore _bookmarks = new();
    private LibraryEntry? _entry;
    private byte[]? _pdf;
    private string _key = "";
    private int _page;
    private int _pageCount;
    private int _zoom = 100;
    private List<int> _resultPages = new();
    private List<int> _bookmarkPages = new();
    private List<LibraryEntry> _shown = new();
    private int _added, _duplicates, _failed;

    private readonly Control _readerRoot, _libraryRoot, _settingsRoot;
    private readonly TextBox _zoomBox;
    private readonly TextBlock _pageText, _statusText, _titleText, _libStatus;
    private readonly Image _image;
    private readonly ScrollViewer _scroll;
    private readonly ToggleButton _lock, _sortRecent, _sortName, _sortBookmarked;
    private readonly TextBox _searchBox, _libSearch;
    private readonly Button _bookmarkButton;
    private readonly ListBox _resultsList, _bookmarksList, _libList;
    private readonly TabControl _tabs;
    private readonly TabItem _resultsTab, _bookmarksTab;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _readerRoot = this.FindControl<Control>("ReaderRoot")!;
        _libraryRoot = this.FindControl<Control>("LibraryRoot")!;
        _settingsRoot = this.FindControl<Control>("SettingsRoot")!;
        _zoomBox = this.FindControl<TextBox>("ZoomBox")!;
        _pageText = this.FindControl<TextBlock>("PageText")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _libStatus = this.FindControl<TextBlock>("LibStatus")!;
        _image = this.FindControl<Image>("PageImage")!;
        _scroll = this.FindControl<ScrollViewer>("PageScroll")!;
        _lock = this.FindControl<ToggleButton>("ZoomLockButton")!;
        _sortRecent = this.FindControl<ToggleButton>("SortRecent")!;
        _sortName = this.FindControl<ToggleButton>("SortName")!;
        _sortBookmarked = this.FindControl<ToggleButton>("SortBookmarked")!;
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _libSearch = this.FindControl<TextBox>("LibSearchBox")!;
        _bookmarkButton = this.FindControl<Button>("BookmarkButton")!;
        _resultsList = this.FindControl<ListBox>("ResultsList")!;
        _bookmarksList = this.FindControl<ListBox>("BookmarksList")!;
        _libList = this.FindControl<ListBox>("LibList")!;
        _tabs = this.FindControl<TabControl>("SideTabs")!;
        _resultsTab = this.FindControl<TabItem>("ResultsTab")!;
        _bookmarksTab = this.FindControl<TabItem>("BookmarksTab")!;
        this.FindControl<TextBlock>("StampText")!.Text = AppInfo.Stamp;
        this.FindControl<TextBlock>("LibStampText")!.Text = AppInfo.Stamp;

        // Reader
        this.FindControl<Button>("BackButton")!.Click += (_, _) => ShowLibrary();
        this.FindControl<Button>("PrevButton")!.Click += async (_, _) => await GoToAsync(_page - 1);
        this.FindControl<Button>("NextButton")!.Click += async (_, _) => await GoToAsync(_page + 1);
        this.FindControl<Button>("ZoomInButton")!.Click += async (_, _) => await SetZoomAsync(_zoom + 25);
        this.FindControl<Button>("ZoomOutButton")!.Click += async (_, _) => await SetZoomAsync(_zoom - 25);
        _zoomBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await ApplyZoomBoxAsync(); };
        _zoomBox.LostFocus += async (_, _) => await ApplyZoomBoxAsync();
        this.FindControl<Button>("SearchButton")!.Click += async (_, _) => await SearchAsync();
        _searchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchAsync(); };
        this.FindControl<Button>("PrevResultButton")!.Click += (_, _) => MoveResult(-1);
        this.FindControl<Button>("NextResultButton")!.Click += (_, _) => MoveResult(1);
        _bookmarkButton.Click += (_, _) => ToggleBookmark();
        _resultsList.SelectionChanged += async (_, _) =>
        {
            var i = _resultsList.SelectedIndex;
            if (i >= 0 && i < _resultPages.Count) await GoToAsync(_resultPages[i]);
        };
        _bookmarksList.SelectionChanged += async (_, _) =>
        {
            var i = _bookmarksList.SelectedIndex;
            if (i >= 0 && i < _bookmarkPages.Count) await GoToAsync(_bookmarkPages[i]);
        };
        var night = this.FindControl<ToggleButton>("NightButton")!;
        night.IsCheckedChanged += (_, _) =>
            Application.Current!.RequestedThemeVariant =
                night.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;

        // Settings
        this.FindControl<TextBlock>("SettingsVersionText")!.Text = AppInfo.Stamp;
        this.FindControl<ListBox>("HistoryList")!.ItemsSource = VersionHistory.Lines;
        this.FindControl<Button>("SettingsButton")!.Click += (_, _) =>
        {
            _libraryRoot.IsVisible = false;
            _settingsRoot.IsVisible = true;
        };
        this.FindControl<Button>("SettingsBackButton")!.Click += (_, _) => ShowLibrary();
        this.FindControl<Button>("ReleasesButton")!.Click += async (_, _) =>
        {
            try { await Launcher.LaunchUriAsync(new Uri(VersionHistory.ReleasesUrl)); } catch (Exception) { }
        };

        // Library
        this.FindControl<Button>("AddFilesButton")!.Click += async (_, _) => await AddFilesAsync();
        this.FindControl<Button>("AddFolderButton")!.Click += async (_, _) => await AddFolderAsync();
        this.FindControl<Button>("LibOpenButton")!.Click += async (_, _) => await OpenSelectedAsync();
        this.FindControl<Button>("LibRemoveButton")!.Click += (_, _) => RemoveSelected();
        _libList.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        _libSearch.TextChanged += (_, _) => RefreshLibrary();
        foreach (var b in new[] { _sortRecent, _sortName, _sortBookmarked })
            b.Click += (s, _) =>
            {
                foreach (var o in new[] { _sortRecent, _sortName, _sortBookmarked }) o.IsChecked = ReferenceEquals(o, s);
                RefreshLibrary();
            };
        DragDrop.SetAllowDrop(_libraryRoot, true);
        _libraryRoot.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        _libraryRoot.AddHandler(DragDrop.DropEvent, OnDrop);

        ShowLibrary();
        UpdateLabels();
    }

    // ---------- Library ----------

    private void ShowLibrary()
    {
        _readerRoot.IsVisible = false;
        _settingsRoot.IsVisible = false;
        _libraryRoot.IsVisible = true;
        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        IEnumerable<LibraryEntry> q = _store.Entries;
        var text = _libSearch.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
            q = q.Where(e => e.Title.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (_sortBookmarked.IsChecked == true) q = q.Where(e => _bookmarks.Get(e.Id).Count > 0);
        q = _sortName.IsChecked == true
            ? q.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            : q.OrderByDescending(e => e.LastOpened).ThenByDescending(e => e.Added);
        _shown = q.ToList();
        _libList.ItemsSource = _shown.Select(e =>
            $"{e.Title}   ·   {e.Pages} pages   ·   " +
            (e.LastOpened == DateTime.MinValue ? "Not opened" : $"Page {e.LastPage + 1}")).ToList();
        if (_added + _duplicates + _failed == 0)
            _libStatus.Text = _store.Entries.Count == 0
                ? "No PDFs yet. Tap + Add PDFs, tap Add folder, or drag PDFs onto this window."
                : $"{_shown.Count} of {_store.Entries.Count} PDFs. Double-click one to open it.";
    }

    private void Tally(AddResult r)
    {
        if (r == AddResult.Added) _added++;
        else if (r == AddResult.Duplicate) _duplicates++;
        else _failed++;
    }

    private void Report()
    {
        _libStatus.Text = $"Added {_added}, already in library {_duplicates}, could not read {_failed}.";
        RefreshLibrary();
        _added = _duplicates = _failed = 0;
    }

    private async Task AddFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add PDFs",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
        });
        if (files.Count == 0) return;
        _libStatus.Text = "Adding...";
        foreach (var f in files)
        {
            try
            {
                await using var s = await f.OpenReadAsync();
                Tally(await _store.AddAsync(f.Name, s));
            }
            catch (Exception) { _failed++; }
        }
        Report();
    }

    private async Task AddFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add a whole folder" });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (path == null) { _libStatus.Text = "Could not read that folder."; return; }
        _libStatus.Text = "Adding...";
        await AddPathsAsync(Directory.EnumerateFiles(path, "*.pdf", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        }));
        Report();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = (e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>())
            .Select(i => i.TryGetLocalPath())
            .Where(p => p != null && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(p => p!)
            .ToList();
        if (paths.Count == 0) { _libStatus.Text = "Drop PDF files here."; return; }
        _libStatus.Text = "Adding...";
        await AddPathsAsync(paths);
        Report();
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            try
            {
                await using var s = File.OpenRead(p);
                Tally(await _store.AddAsync(Path.GetFileName(p), s));
            }
            catch (Exception) { _failed++; }
        }
    }

    private void RemoveSelected()
    {
        var i = _libList.SelectedIndex;
        if (i < 0 || i >= _shown.Count) return;
        var e = _shown[i];
        _store.Remove(e);
        _libStatus.Text = $"Removed {e.Title} from the library. Your original file is untouched.";
        _added = _duplicates = _failed = 1; // keep this message until the next add
        RefreshLibrary();
        _added = _duplicates = _failed = 0;
    }

    private async Task OpenSelectedAsync()
    {
        var i = _libList.SelectedIndex;
        if (i < 0 || i >= _shown.Count) return;
        var entry = _shown[i];
        try
        {
            var bytes = await File.ReadAllBytesAsync(_store.PathOf(entry));
            var count = await Task.Run(() => { using var m = new MemoryStream(bytes); return Conversion.GetPageCount(m); });
            _entry = entry;
            _pdf = bytes;
            _pageCount = count;
            _page = Math.Clamp(entry.LastPage, 0, count - 1);
            _zoom = 100;
            _key = entry.Id;
            _titleText.Text = entry.Title;
            _resultPages = new List<int>();
            _resultsList.ItemsSource = new List<string>();
            _resultsTab.Header = "Results";
            RefreshBookmarks();
            _store.MarkOpened(entry, _page);
            _libraryRoot.IsVisible = false;
            _readerRoot.IsVisible = true;
            await RenderAsync();
        }
        catch (Exception ex)
        {
            _libStatus.Text = "Could not open this PDF: " + ex.Message;
        }
    }

    // ---------- Reader ----------

    // Zoom lock on: keep the zoom. Off: each page resets to 100%. Every new page starts at the top.
    private async Task GoToAsync(int page)
    {
        if (_pdf == null || page < 0 || page >= _pageCount || page == _page) return;
        _page = page;
        if (_lock.IsChecked != true) _zoom = 100;
        if (_entry != null) _store.MarkOpened(_entry, _page);
        await RenderAsync();
    }

    private async Task SetZoomAsync(int value)
    {
        _zoom = Math.Clamp(value, 25, 400);
        if (_pdf != null) await RenderAsync(keepScroll: true);
        else UpdateLabels();
    }

    // Typed zoom: whole numbers, snapped to 25-400. Anything else restores the old zoom.
    private async Task ApplyZoomBoxAsync()
    {
        var t = _zoomBox.Text?.Trim().TrimEnd('%').Trim();
        if (int.TryParse(t, out var v) && Math.Clamp(v, 25, 400) != _zoom) await SetZoomAsync(v);
        else UpdateLabels();
    }

    private async Task SearchAsync()
    {
        var query = _searchBox.Text?.Trim();
        if (_pdf == null || string.IsNullOrEmpty(query)) return;
        var bytes = _pdf;
        try
        {
            _resultsTab.Header = "Searching...";
            var hits = await Task.Run(() => PdfSearch.Find(bytes, query));
            _resultPages = hits.Select(h => h.Page).ToList();
            _resultsList.ItemsSource = hits.Select(h => $"Page {h.Page + 1}: {h.Snippet}").ToList();
            _resultsTab.Header = $"Results ({hits.Count})";
            _tabs.SelectedItem = _resultsTab;
            if (hits.Count == 0) _statusText.Text = "No matches for \"" + query + "\".";
            else _resultsList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _resultsTab.Header = "Results";
            _statusText.Text = "Search failed: " + ex.Message;
        }
    }

    private void MoveResult(int step)
    {
        if (_resultPages.Count == 0) return;
        _resultsList.SelectedIndex = Math.Clamp(_resultsList.SelectedIndex + step, 0, _resultPages.Count - 1);
    }

    private void ToggleBookmark()
    {
        if (_pdf == null) return;
        if (!_bookmarks.Toggle(_key, _page)) _statusText.Text = "Could not save the bookmark.";
        RefreshBookmarks();
        UpdateLabels();
    }

    private void RefreshBookmarks()
    {
        _bookmarkPages = _bookmarks.Get(_key);
        _bookmarksList.ItemsSource = _bookmarkPages.Select(p => $"Page {p + 1}").ToList();
        _bookmarksTab.Header = $"Bookmarks ({_bookmarkPages.Count})";
    }

    private async Task RenderAsync(bool keepScroll = false)
    {
        if (_pdf == null) return;
        var bytes = _pdf;
        var page = _page;
        var dpi = 96 * _zoom / 100;
        try
        {
            var bmp = await Task.Run(() =>
            {
                using var m = new MemoryStream(bytes);
                using var sk = Conversion.ToImage(m, page: page, options: new RenderOptions { Dpi = dpi });
                using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
                using var png = new MemoryStream(data.ToArray());
                return new Bitmap(png);
            });
            _image.Source = bmp;
            _statusText.Text = "";
            if (!keepScroll) _scroll.Offset = new Vector(0, 0);
        }
        catch (Exception ex)
        {
            _statusText.Text = "Could not draw this page: " + ex.Message;
        }
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        _zoomBox.Text = _zoom.ToString();
        _pageText.Text = _pdf == null ? "No PDF open" : $"Page {_page + 1} of {_pageCount}";
        _bookmarkButton.Content = _pdf != null && _bookmarks.Has(_key, _page) ? "★ Bookmarked" : "☆ Bookmark";
    }
}
