using System.Text;

namespace Ra2ModeLauncher;

internal static class IniFileEditor
{
    public static int ConfigureSinglePlayerSpeed(string runtimePath, int engineSpeed, int maxGameTicks)
    {
        bool managed = CncSpeedComponent.IsInstalled(runtimePath);
        int cap = managed ? GameSpeedTarget.FromLegacy(engineSpeed, maxGameTicks).CncMaxGameTicks : maxGameTicks == 0 ? -1 : maxGameTicks;
        ConfigureCncDdraw(runtimePath, cap);
        return managed ? 0 : engineSpeed;
    }

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
        SetValue(lines, "gamemd-spawn", "maxgameticks", maxGameTicks.ToString());
        SetValue(lines, "gamemd-spawn", "limiter_type", "0");
        SetValue(lines, "gamemd-spawn", "nonexclusive", "true");
        SetValue(lines, "gamemd-spawn", "noactivateapp", "false");
        SetValue(lines, "gamemd-spawn", "game_handles_close", "true");
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        ConfigureQuickExit(runtimePath);
    }

    public static void VerifyLanSpeed(string runtimePath, LanGameSetup setup)
    {
        setup.ValidateSpeed();
        int? Read(string file, string section, string key)
        {
            bool active = false;
            foreach (string source in File.ReadLines(Path.Combine(runtimePath, file)))
            {
                string line = source.Trim();
                if (line.StartsWith('[')) { active = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase); continue; }
                string[] pair = line.Split('=', 2);
                if (active && pair.Length == 2 && pair[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    string value = pair[1].Split(';')[0].Trim();
                    if (bool.TryParse(value, out bool flag)) return flag ? 1 : 0;
                    return int.TryParse(value, out int number) ? number : null;
                }
            }
            return null;
        }
        if (Read("ddraw.ini", "ddraw", "maxgameticks") != setup.EffectiveMaxGameTicks ||
            Read("ddraw.ini", "gamemd-spawn", "maxgameticks") != setup.EffectiveMaxGameTicks ||
            Read("ddraw.ini", "gamemd-spawn", "limiter_type") != 0 ||
            Read("spawn.ini", "Settings", "GameSpeed") != setup.GameSpeed ||
            Read("spawn.ini", "Settings", "DisableGameSpeed") != 1 ||
            Read("spawn.ini", "Settings", "ForceMultiplayer") != 1)
            throw new InvalidDataException("本机速度配置与房主不一致，已取消启动。请重新准备。");
    }

    private static void ConfigureQuickExit(string runtimePath)
    {
        string path = Path.Combine(runtimePath, "RA2MD.INI");
        if (!File.Exists(path)) return;
        string backup = path + ".launcher-backup";
        if (!File.Exists(backup)) File.Copy(path, backup);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = Encoding.GetEncoding(936);
        List<string> lines = File.ReadAllLines(path, encoding).ToList();
        SetValue(lines, "Options", "QuickExit", "yes");
        File.WriteAllLines(path, lines, encoding);
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
