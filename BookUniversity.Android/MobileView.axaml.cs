using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PDFtoImage;
using SkiaSharp;

namespace BookUniversity;

internal sealed record LibraryRow(string Id, string Title, string Subtitle, double Percent);

public partial class MobileView : UserControl
{
    private const string ReleasesUrl = "https://github.com/Blackout-78/book-university/releases";
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#7FA6FF"));
    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#8D9AB8"));

    private readonly LibraryStore _store = new();
    private readonly BookmarkStore _bookmarks = new();
    private LibraryEntry? _entry;
    private byte[]? _pdf;
    private string _key = "";
    private int _page;
    private int _pageCount;
    private int _zoom = 100;
    private List<int> _resultPages = new();
    private List<(string Id, int Page)> _allBookmarks = new();
    private int _added, _duplicates, _failed;

    private readonly Control _libraryRoot, _bookmarksRoot, _settingsRoot, _readerRoot, _navBar;
    private readonly Control _zoomPanel, _searchPanel;
    private readonly Button _tabLibrary, _tabBookmarks, _tabSettings, _bookmarkButton;
    private readonly TextBox _zoomBox, _searchBox, _libSearch;
    private readonly TextBlock _pageText, _percentText, _statusText, _titleText, _libStatus, _bookmarksEmpty;
    private readonly Image _image;
    private readonly ScrollViewer _scroll;
    private readonly ToggleButton _lock, _night;
    private readonly ProgressBar _progress;
    private readonly ListBox _resultsList, _libList, _allBookmarksList;

    public MobileView()
    {
        AvaloniaXamlLoader.Load(this);
        _libraryRoot = this.FindControl<Control>("LibraryRoot")!;
        _bookmarksRoot = this.FindControl<Control>("BookmarksRoot")!;
        _settingsRoot = this.FindControl<Control>("SettingsRoot")!;
        _readerRoot = this.FindControl<Control>("ReaderRoot")!;
        _navBar = this.FindControl<Control>("NavBar")!;
        _zoomPanel = this.FindControl<Control>("ZoomPanel")!;
        _searchPanel = this.FindControl<Control>("SearchPanel")!;
        _tabLibrary = this.FindControl<Button>("TabLibrary")!;
        _tabBookmarks = this.FindControl<Button>("TabBookmarks")!;
        _tabSettings = this.FindControl<Button>("TabSettings")!;
        _bookmarkButton = this.FindControl<Button>("BookmarkButton")!;
        _zoomBox = this.FindControl<TextBox>("ZoomBox")!;
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _libSearch = this.FindControl<TextBox>("LibSearchBox")!;
        _pageText = this.FindControl<TextBlock>("PageText")!;
        _percentText = this.FindControl<TextBlock>("PercentText")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _libStatus = this.FindControl<TextBlock>("LibStatus")!;
        _bookmarksEmpty = this.FindControl<TextBlock>("BookmarksEmpty")!;
        _image = this.FindControl<Image>("PageImage")!;
        _scroll = this.FindControl<ScrollViewer>("PageScroll")!;
        _lock = this.FindControl<ToggleButton>("ZoomLockButton")!;
        _night = this.FindControl<ToggleButton>("NightButton")!;
        _progress = this.FindControl<ProgressBar>("ReadProgress")!;
        _resultsList = this.FindControl<ListBox>("ResultsList")!;
        _libList = this.FindControl<ListBox>("LibList")!;
        _allBookmarksList = this.FindControl<ListBox>("AllBookmarksList")!;

        this.FindControl<TextBlock>("StampText")!.Text = AppInfo.Stamp;
        this.FindControl<TextBlock>("VersionText")!.Text = AppInfo.Stamp;

        // Tabs
        _tabLibrary.Click += (_, _) => ShowTab(_libraryRoot);
        _tabBookmarks.Click += (_, _) => ShowTab(_bookmarksRoot);
        _tabSettings.Click += (_, _) => ShowTab(_settingsRoot);
        this.FindControl<Button>("ReleasesButton")!.Click += async (_, _) => await OpenReleasesAsync();

        // Library
        this.FindControl<Button>("AddFilesButton")!.Click += async (_, _) => await AddFilesAsync();
        this.FindControl<Button>("AddFolderButton")!.Click += async (_, _) => await AddFolderAsync();
        _libSearch.TextChanged += (_, _) => RefreshLibrary();
        _libList.AddHandler(Button.ClickEvent, OnRowButton);
        _libList.SelectionChanged += async (_, _) =>
        {
            if (_libList.SelectedItem is not LibraryRow row) return;
            _libList.SelectedIndex = -1;
            await OpenAsync(row.Id);
        };
        _allBookmarksList.SelectionChanged += async (_, _) =>
        {
            var i = _allBookmarksList.SelectedIndex;
            if (i < 0 || i >= _allBookmarks.Count) return;
            var (id, page) = _allBookmarks[i];
            _allBookmarksList.SelectedIndex = -1;
            await OpenAsync(id, page);
        };

        // Reader
        this.FindControl<Button>("BackButton")!.Click += (_, _) => ShowTab(_libraryRoot);
        this.FindControl<Button>("PrevButton")!.Click += async (_, _) => await GoToAsync(_page - 1);
        this.FindControl<Button>("NextButton")!.Click += async (_, _) => await GoToAsync(_page + 1);
        this.FindControl<Button>("ZoomInButton")!.Click += async (_, _) => await SetZoomAsync(_zoom + 25);
        this.FindControl<Button>("ZoomOutButton")!.Click += async (_, _) => await SetZoomAsync(_zoom - 25);
        this.FindControl<Button>("ZoomToggle")!.Click += (_, _) => _zoomPanel.IsVisible = !_zoomPanel.IsVisible;
        this.FindControl<Button>("SearchToggle")!.Click += (_, _) => _searchPanel.IsVisible = !_searchPanel.IsVisible;
        this.FindControl<Button>("SearchGo")!.Click += async (_, _) => await SearchAsync();
        _searchBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await SearchAsync(); };
        _zoomBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await ApplyZoomBoxAsync(); };
        _zoomBox.LostFocus += async (_, _) => await ApplyZoomBoxAsync();
        _bookmarkButton.Click += (_, _) => ToggleBookmark();
        _night.IsCheckedChanged += async (_, _) => { if (_pdf != null) await RenderAsync(keepScroll: true); };
        _scroll.SizeChanged += (_, _) => ApplyImageWidth();
        _resultsList.SelectionChanged += async (_, _) =>
        {
            var i = _resultsList.SelectedIndex;
            if (i < 0 || i >= _resultPages.Count) return;
            _searchPanel.IsVisible = false;
            await GoToAsync(_resultPages[i]);
        };

        ShowTab(_libraryRoot);
        UpdateLabels();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        top.BackRequested += (_, args) =>
        {
            if (_readerRoot.IsVisible) { ShowTab(_libraryRoot); args.Handled = true; }
            else if (!_libraryRoot.IsVisible) { ShowTab(_libraryRoot); args.Handled = true; }
        };
        if (top.InsetsManager is { } insets)
        {
            Padding = insets.SafeAreaPadding;
            insets.SafeAreaChanged += (_, _) => Padding = insets.SafeAreaPadding;
        }
    }

    // ---------- Navigation ----------

    private void ShowTab(Control page)
    {
        foreach (var p in new[] { _libraryRoot, _bookmarksRoot, _settingsRoot, _readerRoot }) p.IsVisible = ReferenceEquals(p, page);
        _navBar.IsVisible = !ReferenceEquals(page, _readerRoot);
        _tabLibrary.Foreground = ReferenceEquals(page, _libraryRoot) ? Active : Idle;
        _tabBookmarks.Foreground = ReferenceEquals(page, _bookmarksRoot) ? Active : Idle;
        _tabSettings.Foreground = ReferenceEquals(page, _settingsRoot) ? Active : Idle;
        if (ReferenceEquals(page, _libraryRoot)) RefreshLibrary();
        if (ReferenceEquals(page, _bookmarksRoot)) RefreshAllBookmarks();
    }

    private async Task OpenReleasesAsync()
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher != null) await launcher.LaunchUriAsync(new Uri(ReleasesUrl));
        }
        catch (Exception) { }
    }

    // ---------- Library ----------

    private static double Percent(LibraryEntry e) =>
        e.LastOpened == DateTime.MinValue || e.Pages <= 0 ? 0 : Math.Round((e.LastPage + 1) * 100.0 / e.Pages);

    private void RefreshLibrary()
    {
        IEnumerable<LibraryEntry> q = _store.Entries;
        var text = _libSearch.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
            q = q.Where(e => e.Title.Contains(text, StringComparison.OrdinalIgnoreCase));
        q = q.OrderByDescending(e => e.LastOpened).ThenByDescending(e => e.Added);
        var rows = q.Select(e =>
        {
            var pct = Percent(e);
            var sub = e.LastOpened == DateTime.MinValue
                ? $"Not started · 0% · {e.Pages} pages"
                : $"Page {e.LastPage + 1} of {e.Pages} · {pct}%";
            return new LibraryRow(e.Id, e.Title, sub, pct);
        }).ToList();
        _libList.ItemsSource = rows;
        if (_added + _duplicates + _failed == 0)
            _libStatus.Text = _store.Entries.Count == 0
                ? "No PDFs yet. Tap Add files or Add folder."
                : $"{rows.Count} of {_store.Entries.Count} PDFs. Tap one to open it.";
    }

    private void OnRowButton(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button b || b.Tag is not string id) return;
        e.Handled = true;
        var entry = _store.Entries.FirstOrDefault(x => x.Id == id);
        if (entry == null) return;
        _store.Remove(entry);
        _libStatus.Text = $"Removed {entry.Title} from the library. Your original file is untouched.";
        _added = _duplicates = _failed = 1;
        RefreshLibrary();
        _added = _duplicates = _failed = 0;
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
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp == null) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add PDFs",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" }, MimeTypes = new[] { "application/pdf" } }
            }
        });
        if (files.Count == 0) return;
        _libStatus.Text = "Adding...";
        foreach (var f in files) await AddFileAsync(f);
        Report();
    }

    private async Task AddFolderAsync()
    {
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp == null) return;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add a whole folder" });
        if (folders.Count == 0) return;
        _libStatus.Text = "Adding...";
        await AddFolderItemsAsync(folders[0]);
        Report();
    }

    private async Task AddFolderItemsAsync(IStorageFolder folder)
    {
        try
        {
            await foreach (var item in folder.GetItemsAsync())
            {
                if (item is IStorageFile f && f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    await AddFileAsync(f);
                else if (item is IStorageFolder sub)
                    await AddFolderItemsAsync(sub);
            }
        }
        catch (Exception) { _failed++; }
    }

    private async Task AddFileAsync(IStorageFile f)
    {
        try
        {
            await using var s = await f.OpenReadAsync();
            Tally(await _store.AddAsync(f.Name, s));
        }
        catch (Exception) { _failed++; }
    }

    // ---------- Bookmarks tab ----------

    private void RefreshAllBookmarks()
    {
        _allBookmarks = new List<(string, int)>();
        var lines = new List<string>();
        foreach (var e in _store.Entries.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
            foreach (var p in _bookmarks.Get(e.Id))
            {
                _allBookmarks.Add((e.Id, p));
                lines.Add($"{e.Title} · Page {p + 1}");
            }
        _allBookmarksList.ItemsSource = lines;
        _bookmarksEmpty.IsVisible = lines.Count == 0;
    }

    // ---------- Reader ----------

    private async Task OpenAsync(string id, int? startPage = null)
    {
        var entry = _store.Entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(_store.PathOf(entry));
            var count = await Task.Run(() => { using var m = new MemoryStream(bytes); return Conversion.GetPageCount(m); });
            _entry = entry;
            _pdf = bytes;
            _pageCount = count;
            _page = Math.Clamp(startPage ?? entry.LastPage, 0, count - 1);
            _zoom = 100;
            _key = entry.Id;
            _titleText.Text = entry.Title;
            _resultPages = new List<int>();
            _resultsList.ItemsSource = new List<string>();
            _searchPanel.IsVisible = false;
            _zoomPanel.IsVisible = false;
            _store.MarkOpened(entry, _page);
            ShowTab(_readerRoot);
            await RenderAsync();
        }
        catch (Exception ex)
        {
            ShowTab(_libraryRoot);
            _libStatus.Text = "Could not open this PDF: " + ex.Message;
        }
    }

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
            _statusText.Text = "Searching...";
            var hits = await Task.Run(() => PdfSearch.Find(bytes, query));
            _resultPages = hits.Select(h => h.Page).ToList();
            _resultsList.ItemsSource = hits.Select(h => $"Page {h.Page + 1}: {h.Snippet}").ToList();
            _statusText.Text = hits.Count == 0 ? "No matches for \"" + query + "\"." : "";
        }
        catch (Exception ex)
        {
            _statusText.Text = "Search failed: " + ex.Message;
        }
    }

    private void ToggleBookmark()
    {
        if (_pdf == null) return;
        if (!_bookmarks.Toggle(_key, _page)) _statusText.Text = "Could not save the bookmark.";
        UpdateLabels();
    }

    private void ApplyImageWidth()
    {
        var w = _scroll.Bounds.Width > 0 ? _scroll.Bounds.Width : 360;
        _image.Width = w * _zoom / 100.0;
    }

    private async Task RenderAsync(bool keepScroll = false)
    {
        if (_pdf == null) return;
        var bytes = _pdf;
        var page = _page;
        var night = _night.IsChecked == true;
        // Sharp enough for the phone screen, capped so big zooms do not run out of memory.
        var dpi = Math.Min(300, 150 * _zoom / 100);
        try
        {
            var bmp = await Task.Run(() =>
            {
                using var m = new MemoryStream(bytes);
                using var sk = Conversion.ToImage(m, page: page, options: new RenderOptions { Dpi = dpi });
                using var surface = SKSurface.Create(new SKImageInfo(sk.Width, sk.Height));
                surface.Canvas.DrawBitmap(sk, 0, 0);
                if (night) surface.Canvas.DrawColor(SKColors.White, SKBlendMode.Difference);
                using var img = surface.Snapshot();
                using var data = img.Encode(SKEncodedImageFormat.Jpeg, 92);
                using var jpg = new MemoryStream(data.ToArray());
                return new Bitmap(jpg);
            });
            _image.Source = bmp;
            _statusText.Text = "";
            ApplyImageWidth();
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
        if (_pdf == null)
        {
            _pageText.Text = "No PDF open";
            _percentText.Text = "";
            _progress.Value = 0;
            return;
        }
        var pct = Math.Round((_page + 1) * 100.0 / _pageCount);
        _pageText.Text = $"Page {_page + 1} of {_pageCount}";
        _percentText.Text = $"{pct}%";
        _progress.Value = pct;
        _bookmarkButton.Content = _bookmarks.Has(_key, _page) ? "Bookmarked" : "Bookmark";
        _bookmarkButton.Foreground = _bookmarks.Has(_key, _page) ? Active : Idle;
    }
}
