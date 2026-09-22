using System.Text;

namespace Ra2ModeLauncher;

internal static class IniFileEditor
{
    public static void SetVideoResolution(string runtimePath, int width, int height)
    {
        string path = Path.Combine(runtimePath, "RA2MD.INI");
        if (!File.Exists(path)) throw new FileNotFoundException("运行目录中缺少 RA2MD.INI。", path);
        string backup = path + ".launcher-backup";
        if (!File.Exists(backup)) File.Copy(path, backup);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = Encoding.GetEncoding(936);
        List<string> lines = File.ReadAllLines(path, encoding).ToList();
        SetValue(lines, "Video", "AllowHiResModes", "yes");
        SetValue(lines, "Video", "ScreenWidth", width.ToString());
        SetValue(lines, "Video", "ScreenHeight", height.ToString());
        File.WriteAllLines(path, lines, encoding);
    }

    public static void ConfigureCncDdraw(string runtimePath, int maxGameTicks)
    {
        string path = Path.Combine(runtimePath, "ddraw.ini");
        if (!File.Exists(path)) throw new FileNotFoundException("运行目录中缺少 ddraw.ini。", path);
        string backup = path + ".launcher-backup";
        if (!File.Exists(backup)) File.Copy(path, backup);

        List<string> lines = File.ReadAllLines(path).ToList();
        SetValue(lines, "ddraw", "windowed", "true");
        SetValue(lines, "ddraw", "fullscreen", "true");
        SetValue(lines, "ddraw", "border", "true");
        SetValue(lines, "ddraw", "resizable", "true");
        SetValue(lines, "ddraw", "toggle_borderless", "true");
        SetValue(lines, "ddraw", "savesettings", "1");
        SetValue(lines, "ddraw", "keytogglefullscreen", "0x0D");
        SetValue(lines, "ddraw", "maxgameticks", maxGameTicks.ToString());
        SetValue(lines, "gamemd-spawn", "nonexclusive", "true");
        SetValue(lines, "gamemd-spawn", "noactivateapp", "false");
        SetValue(lines, "gamemd-spawn", "game_handles_close", "false");
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void SetValue(List<string> lines, string section, string key, string value)
    {
        int sectionIndex = lines.FindIndex(line => line.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
            return;
        }

        int nextSection = lines.FindIndex(sectionIndex + 1, line => line.TrimStart().StartsWith('['));
        if (nextSection < 0) nextSection = lines.Count;
        for (int i = sectionIndex + 1; i < nextSection; i++)
        {
            int equals = lines[i].IndexOf('=');
            if (equals >= 0 && lines[i][..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                return;
            }
        }
        lines.Insert(nextSection, $"{key}={value}");
    }
}
