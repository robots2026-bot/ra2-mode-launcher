using System.Reflection;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var assembly = Assembly.Load("Ra2ModeLauncher");
        Type type = assembly.GetType("Ra2ModeLauncher.GameToolsBar", true)!;
        using var bar = (Control)Activator.CreateInstance(type, true)!;
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var checkbox = (CheckBox)type.GetField("enabled", flags)!.GetValue(bar)!;
        var interval = (NumericUpDown)type.GetField("interval", flags)!.GetValue(bar)!;
        var hook = type.GetField("hook", flags)!;
        if (interval.Value != 10 || !checkbox.Checked || (IntPtr)hook.GetValue(bar)! == IntPtr.Zero) throw new Exception("Incorrect defaults or default mouse hook missing");
        if (bar.Controls.OfType<Button>().Any() || bar.Controls.OfType<ComboBox>().Any()) throw new Exception("Unexpected exit/hotkey controls");
        var trigger = type.GetMethod("IsProductionTrigger", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Matches(uint action, bool ctrl, bool shift, bool alt) => (bool)trigger.Invoke(null, new object[] { action, ctrl, shift, alt })!;
        if (!Matches(0x0201, true, false, false) || Matches(0x0204, false, true, false) || Matches(0x0204, true, false, false) || Matches(0x0201, false, false, false) || Matches(0x0201, true, true, false)) throw new Exception("Incorrect Ctrl-left trigger or intercepted native right-click");
        Type input = type.GetNestedType("Input", BindingFlags.NonPublic)!;
        if (System.Runtime.InteropServices.Marshal.SizeOf(input) != (IntPtr.Size == 8 ? 40 : 28)) throw new Exception("Incorrect native INPUT layout");
        string fixture = Path.Combine(AppContext.BaseDirectory, "resolution-fixture");
        Directory.CreateDirectory(fixture);
        Type patcher = assembly.GetType("Ra2ModeLauncher.MapPatcher", true)!;
        var patchQueue = patcher.GetMethod("SetProductionQueueLimit", BindingFlags.Public | BindingFlags.Static)!;
        foreach (string source in new[] { "[Basic]\nName=test\n", "[General]\r\nMaximumQueuedObjects=30\r\nOther=keep\r\n[UNIT]\r\nBuildLimit=1\r\n" })
        {
            string map = Path.Combine(fixture, "queue.map");
            File.WriteAllText(map, source);
            patchQueue.Invoke(null, new object[] { map });
            string once = File.ReadAllText(map);
            patchQueue.Invoke(null, new object[] { map });
            if (!once.Contains("MaximumQueuedObjects=100") || once != File.ReadAllText(map) || source.Contains("BuildLimit=1") && (!once.Contains("BuildLimit=1") || !once.Contains("Other=keep"))) throw new Exception("Queue patch is not idempotent or changed other rules");
        }
        Console.WriteLine("PASS: queue limit 100 inserts/replaces idempotently and preserves unit build limits.");
        Type overlayType = assembly.GetType("Ra2ModeLauncher.GamePerformanceOverlay", true)!;
        var canChange = overlayType.GetMethod("CanChangeSpeed", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (int mode in new[] { -1, 0, 3, 4, 5, 6 })
        foreach (int speed in new[] { -1, 0, 1, 6, 7 })
        {
            bool allowed = (bool)canChange.Invoke(null, new object[] { mode, speed })!;
            if (allowed != ((mode == 0 || mode == 5) && speed >= 0 && speed <= 6)) throw new Exception("Live speed guard allows multiplayer or invalid speed");
        }
        var step = overlayType.GetMethod("StepSpeed", BindingFlags.NonPublic | BindingFlags.Static)!;
        if ((int)step.Invoke(null, new object[] { 0, -1 })! != 0 || (int)step.Invoke(null, new object[] { 6, 1 })! != 6) throw new Exception("Speed step exceeds engine range");
        using (var overlay = (Form)Activator.CreateInstance(overlayType, true)!)
        {
            if (overlay.Visible || overlay.ShowInTaskbar || !overlay.TopMost) throw new Exception("Overlay steals focus or adds taskbar entry");
            // Deterministically reproduce a stale foreground PID after game shutdown.
            // GetProcessById throws ArgumentException for this nonexistent PID.
            overlayType.GetMethod("TryAttach", flags)!.Invoke(overlay, new object[] { int.MaxValue });
            if (overlayType.GetField("game", flags)!.GetValue(overlay) is not null) throw new Exception("Exited PID was attached");
            Console.WriteLine("PASS: disappeared game process is ignored without crashing the overlay.");
            Type dm = overlayType.GetNestedType("DisplayMode", BindingFlags.NonPublic)!;
            if (System.Runtime.InteropServices.Marshal.SizeOf(dm) != 220) throw new Exception("Incorrect display refresh-rate native structure");
        }
        Console.WriteLine("PASS: live speed permits single-player only, clamps engine range, overlay starts hidden, refresh-rate structure is correct. No game memory written.");
        File.WriteAllText(Path.Combine(fixture, "RA2MD.INI"), "[Video]\nScreenWidth=1280\nScreenHeight=720\n[Other]\nScreenWidth=999\n");
        Type configType = assembly.GetType("Ra2ModeLauncher.LauncherConfig", true)!;
        object config = Activator.CreateInstance(configType, true)!;
        configType.GetProperty("RuntimePath")!.SetValue(config, fixture);
        var actual = (System.Drawing.Size)type.GetMethod("ReadGameSize", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { config })!;
        if (actual.Width != 1280 || actual.Height != 720) throw new Exception("Actual game resolution was not used");
        Console.WriteLine("PASS: Ctrl-left only; native Shift-right passes through; INPUT native alignment; actual game resolution overrides launcher selection.");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            checkbox.Checked = true;
            if (!checkbox.Checked || (IntPtr)hook.GetValue(bar)! == IntPtr.Zero) throw new Exception("Native mouse hook did not install");
            checkbox.Checked = false;
            if ((IntPtr)hook.GetValue(bar)! != IntPtr.Zero) throw new Exception("Native mouse hook did not release");
        }
        checkbox.Checked = true;
        bar.Dispose();
        if ((IntPtr)hook.GetValue(bar)! != IntPtr.Zero) throw new Exception("Disposal leaked native mouse hook");
        Console.WriteLine("PASS: 10ms default, no exit/hotkey controls, repeated native hook enable/disable, disposal cleanup. No game input sent.");
    }
}
