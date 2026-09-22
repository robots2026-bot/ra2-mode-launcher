namespace Ra2ModeLauncher;

internal static class StartupTrace
{
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "launcher-startup.log");
    public static void Reset()
    {
        try { File.WriteAllText(PathName, $"{DateTime.Now:O} start{Environment.NewLine}"); } catch { }
    }

    public static void Mark(string text)
    {
        try { File.AppendAllText(PathName, $"{DateTime.Now:O} {text}{Environment.NewLine}"); } catch { }
    }
}
