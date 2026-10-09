using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Ra2ModeLauncher;

// Data addresses are from YRpp GameOptionsClass, SessionClass and FPSCounter.
// Only the two bundled 1.001 executable builds are supported; never guess on another build.
internal sealed class GamePerformanceOverlay : Form
{
    private const int SpeedAddress = 0xA8EB60, ModeAddress = 0xA8B238, FpsAddress = 0xABCD44;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Label content = new() { Dock = DockStyle.Fill, Padding = new Padding(10), ForeColor = Color.LightGreen, AutoSize = false };
    private Process? game;
    private IntPtr memory;
    private bool writable, supported, keysRegistered, hidden;
    private readonly HashSet<int> registeredKeys = [];
    private readonly CncSpeedClient cncSpeed = new();
    private bool managedComponent, bridgeInitialized;
    private DateTime nextMetrics, noticeUntil;
    private string metrics = "正在读取游戏数据…", notice = "";
    private int configuredCap;

    public GamePerformanceOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(12, 19, 24);
        Opacity = 0.65;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(280, 78);
        Controls.Add(content);
        timer.Tick += (_, _) => Poll();
        timer.Start();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080; return p; }
    }

    internal static bool CanChangeSpeed(int mode, int speed) => mode is 0 or 5 && speed is >= 0 and <= 6;
    internal static int StepSpeed(int current, int delta) => Math.Clamp(current + delta, 0, 6);
    internal static string SpeedName(int speed) => speed switch
    {
        0 => "不限速", 1 => "很快 · 60", 2 => "较快 · 30", 3 => "正常 · 20",
        4 => "较慢 · 15", 5 => "很慢 · 12", 6 => "最慢 · 10", _ => "未知"
    };

    private void Poll()
    {
        try
        {
            IntPtr window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out uint pid);
            if (game is not null && game.HasExited) Detach();
            if (game is null && pid != 0) TryAttach((int)pid);
            if (game is null || game.Id != pid || IsIconic(window)) { Hide(); RegisterKeys(false); return; }
            RegisterKeys(true);
            if (DateTime.UtcNow >= nextMetrics)
            {
                UpdateMetrics(window);
                nextMetrics = DateTime.UtcNow.AddSeconds(1);
            }
            if (notice.Length > 0 && DateTime.UtcNow >= noticeUntil) notice = "";
            content.Text = metrics + (notice.Length == 0 ? "" : "\r\n" + notice);
            Size measured = TextRenderer.MeasureText(content.Text, Font, new Size(ClientSize.Width - content.Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            ClientSize = new Size(ClientSize.Width, measured.Height + content.Padding.Vertical);
            if (hidden) { Hide(); return; }
            if (!GetClientRect(window, out Rect rect)) { Hide(); return; }
            var origin = new NativePoint();
            if (!ClientToScreen(window, ref origin)) { Hide(); return; }
            int logicalWidth = Read(SpeedAddress + 36) ?? rect.Right;
            int sidebarWidth = logicalWidth > 0 ? (int)(156.0 * rect.Right / logicalWidth) : 156;
            Location = new Point(origin.X + Math.Max(0, rect.Right - sidebarWidth - Width - 12), origin.Y + 12);
            if (!Visible) Show();
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // topmost, no activation or resizing
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            Hide(); RegisterKeys(false); Detach();
        }
    }

    private void TryAttach(int pid)
    {
        Process candidate;
        // The foreground window's process can disappear between the PID lookup
        // and this call during normal Alt+F4 shutdown.
        try { candidate = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        bool attached = false;
        try
        {
            if (!string.Equals(candidate.ProcessName, "gamemd-spawn", StringComparison.OrdinalIgnoreCase)) return;
            string expected = Path.Combine(LauncherConfig.Load().RuntimePath, "gamemd-spawn.exe");
            if (!string.Equals(candidate.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) return;
            game = candidate;
            attached = true;
            using var image = File.OpenRead(expected);
            string hash = Convert.ToHexString(SHA256.HashData(image));
            supported = candidate.MainModule?.BaseAddress == new IntPtr(0x400000) && hash is
                "C87EB812D7ADD97F8F9962A749EC16205431B6D60C2C4ABF3EF7A74720C7F05B" or
                "B29C47A94D6D414FAD3B53AB0F9EBCD0CE22F0A98CA2BE6F0C6920C4DD7E2FE2";
            if (supported)
            {
                memory = OpenProcess(0x1038, false, pid);
                writable = memory != IntPtr.Zero;
                if (!writable) memory = OpenProcess(0x1010, false, pid);
            }
            configuredCap = ReadIniInt(Path.Combine(Path.GetDirectoryName(expected)!, "ddraw.ini"), "ddraw", "maxgameticks", 0);
            configuredCap = ReadIniInt(Path.Combine(Path.GetDirectoryName(expected)!, "ddraw.ini"), "gamemd-spawn", "maxgameticks", configuredCap);
            managedComponent = CncSpeedComponent.IsInstalled(Path.GetDirectoryName(expected)!);
            bridgeInitialized = false;


            nextMetrics = DateTime.MinValue;
            notice = "";
            StartupTrace.Mark($"performance overlay attached: pid={pid}, supported={supported}, writable={writable}");
        }
        finally { if (!attached) candidate.Dispose(); }
    }

    internal static int ReadIniInt(string path, string section, string key, int fallback)
    {
        if (!File.Exists(path)) return fallback;
        bool active = false;
        foreach (string source in File.ReadLines(path))
        {
            string line = source.Trim();
            if (line.StartsWith('[')) { active = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase); continue; }
            string[] pair = line.Split('=', 2);
            if (active && pair.Length == 2 && pair[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase) && int.TryParse(pair[1].Split(';')[0].Trim(), out int value)) return value;
        }
        return fallback;
    }

    private int? Read(int address)
    {
        byte[] data = new byte[4];
        return memory != IntPtr.Zero && ReadProcessMemory(memory, new IntPtr(address), data, 4, out nuint read) && read == 4 ? BitConverter.ToInt32(data) : null;
    }

    private void UpdateMetrics(IntPtr window)
    {
        int? speed = Read(SpeedAddress), mode = Read(ModeAddress), fps = Read(FpsAddress);
        string speedText = speed is >= 0 and <= 6 ? SpeedName(speed.Value) : "未适配";
        if (managedComponent && mode is 0 or 5 && cncSpeed.TryRead(window, out GameSpeedTarget target))
        {
            if (!bridgeInitialized && writable && speed is >= 0 and <= 6)
            {
                bridgeInitialized = ApplyManagedTarget(window, target);
                if (!bridgeInitialized) SetNotice("实时调速初始化失败");
            }
            speedText = target.DisplayName;
            if (!bridgeInitialized) speedText += "（未应用）";
        }
        else if (configuredCap > 0) speedText += $"（上限 {configuredCap}）";
        if (mode is 3 or 4) speedText += " · 联机锁定";
        string fpsText = fps is >= 0 and <= 10000 ? fps.Value.ToString() : "—";
        Screen screen = Screen.FromHandle(window);
        var display = new DisplayMode { Size = (short)Marshal.SizeOf<DisplayMode>() };
        string hz = EnumDisplaySettings(screen.DeviceName, -1, ref display) && display.Frequency > 1 ? $"{display.Frequency} Hz" : "—";
        metrics = $"速度：{speedText}\r\n游戏 FPS：{fpsText}\r\n刷新率：{hz}";
    }

    private void SetNotice(string message)
    {
        notice = message;
        noticeUntil = DateTime.UtcNow.AddSeconds(3);
    }
    private void ChangeSpeed(int? requested, int delta = 0)
    {
        int? current = Read(SpeedAddress), mode = Read(ModeAddress);
        if (!writable || current is null || mode is null || !CanChangeSpeed(mode.Value, current.Value))
        {
            SetNotice("仅单人对局支持调速");
            return;
        }
        IntPtr window = GetForegroundWindow();
        if (!managedComponent || !cncSpeed.TryRead(window, out GameSpeedTarget before))
        {
            SetNotice("需安装实时调速组件");
            return;
        }
        GameSpeedTarget target = requested is int rate ? new(rate) : before.Step(-delta);
        bool success = ApplyManagedTarget(window, target);
        SetNotice(success ? "速度：" + target.DisplayName : "调速失败");
        nextMetrics = DateTime.MinValue;
        StartupTrace.Mark($"live cnc speed: target={target.TicksPerSecond}, engine={Read(SpeedAddress)}, success={success}");
    }

    private bool ApplyManagedTarget(IntPtr window, GameSpeedTarget target)
    {
        int? current = Read(SpeedAddress), mode = Read(ModeAddress);
        if (!managedComponent || !writable || current is null || mode is null || !CanChangeSpeed(mode.Value, current.Value) || !cncSpeed.TryRead(window, out GameSpeedTarget before)) return false;
        if (!cncSpeed.TrySet(window, target, out _)) return false;
        byte[] data = BitConverter.GetBytes(0);
        if (WriteProcessMemory(memory, new IntPtr(SpeedAddress), data, 4, out nuint written) && written == 4 && Read(SpeedAddress) == 0)
        {
            bridgeInitialized = true;
            configuredCap = target.CncMaxGameTicks;
            return true;
        }
        cncSpeed.TrySet(window, before, out _);
        return false;
    }

    private void ChooseSpeed()
    {
        IntPtr window = GetForegroundWindow();
        int? current = Read(SpeedAddress), mode = Read(ModeAddress);
        if (!managedComponent || current is null || mode is null || !CanChangeSpeed(mode.Value, current.Value) || !cncSpeed.TryRead(window, out GameSpeedTarget target)) { SetNotice("仅支持已安装组件的单人对局"); return; }
        using var dialog = new Form { Text = "游戏速度", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(300, 140), ShowInTaskbar = false, TopMost = true, MaximizeBox = false, MinimizeBox = false, Font = Font };
        var rate = new NumericUpDown { Minimum = 1, Maximum = GameSpeedTarget.Maximum, Value = target.Unlimited ? 60 : target.TicksPerSecond, Width = 110 };
        var unlimited = new CheckBox { Text = "不限速", Checked = target.Unlimited, AutoSize = true };
        rate.Enabled = !unlimited.Checked;
        unlimited.CheckedChanged += (_, _) => rate.Enabled = !unlimited.Checked;
        var apply = new Button { Text = "应用", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), WrapContents = true };
        layout.Controls.AddRange([new Label { Text = "目标速度/秒", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, rate, unlimited, apply, cancel]);
        dialog.Controls.Add(layout); dialog.AcceptButton = apply; dialog.CancelButton = cancel;
        if (dialog.ShowDialog() == DialogResult.OK && game is not null)
        {
            var chosen = new GameSpeedTarget(unlimited.Checked ? 0 : (int)rate.Value);
            bool success = ApplyManagedTarget(window, chosen);
            SetNotice(success ? "速度：" + chosen.DisplayName : "调速失败");
            nextMetrics = DateTime.MinValue;
        }
    }

    private void RegisterKeys(bool enable)
    {
        if (enable == keysRegistered) return;
        keysRegistered = enable;
        if (enable)
        {
            int[] keys = [(int)Keys.Up, (int)Keys.Down, (int)Keys.D0, (int)Keys.D1, (int)Keys.F10, (int)Keys.S];
            for (int i = 0; i < keys.Length; i++)
            {
                if (RegisterHotKey(Handle, 7100 + i, 0x4003, keys[i])) registeredKeys.Add(7100 + i);
                else SetNotice("部分调速快捷键被占用");
            }
        }
        else { foreach (int key in registeredKeys) UnregisterHotKey(Handle, key); registeredKeys.Clear(); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // mouse clicks pass through
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
        if (m.Msg == 0x0312)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            if (game is null || game.Id != pid) return;
            switch (m.WParam.ToInt32())
            {
                case 7100: ChangeSpeed(null, -1); break;
                case 7101: ChangeSpeed(null, 1); break;
                case 7102: ChangeSpeed(0); break;
                case 7103: ChangeSpeed(60); break;
                case 7104: hidden = !hidden; break;
                case 7105: ChooseSpeed(); break;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private void Detach()
    {
        if (memory != IntPtr.Zero) { CloseHandle(memory); memory = IntPtr.Zero; }
        game?.Dispose(); game = null; writable = false; supported = false;
        managedComponent = false; bridgeInitialized = false;

        metrics = "正在读取游戏数据…"; notice = "";
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Stop(); timer.Dispose(); RegisterKeys(false); Detach(); }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public uint BitsPerPel, Width, Height, Flags, Frequency, ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, ref DisplayMode display);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, int key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] data, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
