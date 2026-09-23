using System.Text.Json;

namespace Ra2ModeLauncher;

internal sealed class LauncherConfig
{
    public string RuntimePath { get; set; } = @"D:\Software\RA2Mode";
    public string PlayerName { get; set; } = Environment.MachineName;
    public string RoomName { get; set; } = $"{Environment.MachineName} 的房间";
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public int GameSpeed { get; set; } = 1;
    public int MaxGameTicks { get; set; }
    public bool ShortGame { get; set; } = true;

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "launcher-config.json");

    public static LauncherConfig Load()
    {
        try
        {
            return File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath)) ?? new LauncherConfig()
                : new LauncherConfig();
        }
        catch
        {
            return new LauncherConfig();
        }
    }

    public void Save() => File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
