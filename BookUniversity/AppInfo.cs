namespace BookUniversity;

internal static class AppInfo
{
    public const string Version = "0.3.0";
    public const int Build = 3;
    public static string Stamp => $"Book University v{Version} (build {Build})";
}
