namespace BookUniversity;

internal static class AppInfo
{
    public const string Version = "0.1.0";
    public const int Build = 1;
    public static string Stamp => $"Book University v{Version} (build {Build})";
}
