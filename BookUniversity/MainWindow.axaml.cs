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
    private int _page;
    private int _pageCount;
    private int _zoom = 100;
    private readonly TextBlock _zoomText, _pageText, _statusText, _titleText;
    private readonly Image _image;
    private readonly ScrollViewer _scroll;
    private readonly ToggleButton _lock;

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
        this.FindControl<TextBlock>("StampText")!.Text = AppInfo.Stamp;
        this.FindControl<Button>("OpenButton")!.Click += async (_, _) => await OpenAsync();
        this.FindControl<Button>("PrevButton")!.Click += async (_, _) => await GoToAsync(_page - 1);
        this.FindControl<Button>("NextButton")!.Click += async (_, _) => await GoToAsync(_page + 1);
        this.FindControl<Button>("ZoomInButton")!.Click += async (_, _) => await SetZoomAsync(_zoom + 25);
        this.FindControl<Button>("ZoomOutButton")!.Click += async (_, _) => await SetZoomAsync(_zoom - 25);
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
            _titleText.Text = files[0].Name;
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
        if (_pdf == null || page < 0 || page >= _pageCount) return;
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
    }
}
