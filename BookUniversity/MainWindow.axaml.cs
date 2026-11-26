using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace BookUniversity;

public partial class MainWindow : Window
{
    private int _zoom = 100;
    private readonly TextBlock _zoomText;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _zoomText = this.FindControl<TextBlock>("ZoomText")!;
        this.FindControl<TextBlock>("StampText")!.Text = AppInfo.Stamp;
        this.FindControl<Button>("ZoomInButton")!.Click += (_, _) => SetZoom(_zoom + 25);
        this.FindControl<Button>("ZoomOutButton")!.Click += (_, _) => SetZoom(_zoom - 25);
        var night = this.FindControl<ToggleButton>("NightButton")!;
        night.IsCheckedChanged += (_, _) =>
            Application.Current!.RequestedThemeVariant =
                night.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;
        SetZoom(_zoom);
    }

    private void SetZoom(int value)
    {
        _zoom = Math.Clamp(value, 25, 400);
        _zoomText.Text = $"{_zoom}%";
    }
}
