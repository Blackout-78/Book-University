using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace BookUniversity;

[Activity(
    Label = "Book University",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity<MobileApp>
{
}
