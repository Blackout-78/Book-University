using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using PDFtoImage;
using SkiaSharp;

namespace BookUniversity;

public partial class MainWindow : Window
{
    private byte[]? _pdf;
    private string _key = "";
    private int _page;
    private int _pageCount;
    private int _zoom = 100;
    private List<int> _resultPages = new();
    private List<int> _bookmarkPages = new();
    private readonly BookmarkStore _bookmarks = new();
    private readonly TextBlock _zoomText, _pageText, _statusText, _titleText;
    private readonly Image _image;
    private readonly ScrollViewer _scroll;
    private readonly ToggleButton _lock;
    private readonly TextBox _searchBox;
    private readonly Button _bookmarkButton;
    private readonly ListBox _resultsList, _bookmarksList;
    private readonly TabControl _tabs;
    private readonly TabItem _resultsTab, _bookmarksTab;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _zoomText = this.FindControl<TextBlock>("ZoomText")!;
        _pageText = this.FindControl<TextBlock>("PageText")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _image = this.FindControl<Image>("PageImage")!;
        _scroll = this.FindControl<ScrollViewer>("PageScroll")!;
        _lock = this.FindControl<ToggleButton>("ZoomLockButton")!;
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _bookmarkButton = this.FindControl<Button>("BookmarkButton")!;
        _resultsList = this.FindControl<ListBox>("ResultsList")!;
        _bookmarksList = this.FindControl<ListBox>("BookmarksList")!;
        _tabs = this.FindControl<TabControl>("SideTabs")!;
        _resultsTab = this.FindControl<TabItem>("ResultsTab")!;
        _bookmarksTab = this.FindControl<TabItem>("BookmarksTab")!;
        this.FindControl<TextBlock>("StampText")!.Text = AppInfo.Stamp;
        this.FindControl<Button>("OpenButton")!.Click += async (_, _) => await OpenAsync();
        this.FindControl<Button>("PrevButton")!.Click += async (_, _) => await GoToAsync(_page - 1);
        this.FindControl<Button>("NextButton")!.Click += async (_, _) => await GoToAsync(_page + 1);
        this.FindControl<Button>("ZoomInButton")!.Click += async (_, _) => await SetZoomAsync(_zoom + 25);
        this.FindControl<Button>("ZoomOutButton")!.Click += async (_, _) => await SetZoomAsync(_zoom - 25);
        this.FindControl<Button>("SearchButton")!.Click += async (_, _) => await SearchAsync();
        _searchBox.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await SearchAsync(); };
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
        UpdateLabels();
    }

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PDF",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var count = await Task.Run(() => { using var m = new MemoryStream(bytes); return Conversion.GetPageCount(m); });
            _pdf = bytes;
            _pageCount = count;
            _page = 0;
            _zoom = 100;
            _key = files[0].TryGetLocalPath() ?? files[0].Name;
            _titleText.Text = files[0].Name;
            _resultPages = new List<int>();
            _resultsList.ItemsSource = new List<string>();
            _resultsTab.Header = "Results";
            RefreshBookmarks();
            await RenderAsync();
        }
        catch (Exception ex)
        {
            _statusText.Text = "Could not open this PDF: " + ex.Message;
        }
    }

    // Zoom lock on: keep the zoom. Off: each page resets to 100%. Every new page starts at the top.
    private async Task GoToAsync(int page)
    {
        if (_pdf == null || page < 0 || page >= _pageCount || page == _page) return;
        _page = page;
        if (_lock.IsChecked != true) _zoom = 100;
        await RenderAsync();
    }

    private async Task SetZoomAsync(int value)
    {
        _zoom = Math.Clamp(value, 25, 400);
        if (_pdf != null) await RenderAsync(keepScroll: true);
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
        _zoomText.Text = $"{_zoom}%";
        _pageText.Text = _pdf == null ? "No PDF open" : $"Page {_page + 1} of {_pageCount}";
        _bookmarkButton.Content = _pdf != null && _bookmarks.Has(_key, _page) ? "★ Bookmarked" : "☆ Bookmark";
    }
}
