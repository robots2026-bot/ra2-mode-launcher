namespace Ra2ModeLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        StartupTrace.Reset();
        ApplicationConfiguration.Initialize();
        StartupTrace.Mark("application initialized");
        try
        {
            Application.Run(new HomeForm());
        }
        catch (Exception ex)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "launcher-error.log");
            File.WriteAllText(logPath, ex.ToString());
            MessageBox.Show($"启动器发生错误，详情已写入：\n{logPath}\n\n{ex.Message}", "RA2 Mode Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
