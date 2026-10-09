using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ra2ModeLauncher;

internal sealed class GameToolsBar : FlowLayoutPanel
{
    private readonly CheckBox enabled = new() { Text = "启用生产连点器", AutoSize = true };
    private readonly NumericUpDown interval = new() { Minimum = 10, Maximum = 5000, Value = 10, Increment = 10, Width = 75 };
    private readonly Label state = new() { AutoSize = true, Text = "关闭｜勾选后 Ctrl＋左键批量生产", Margin = new Padding(10, 7, 0, 0) };
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly MouseHook callback;
    private IntPtr hook;
    private uint capturedButton;
    private IntPtr capturedWindow;
    private NativePoint capturedPoint;
    private IntPtr batchWindow;
    private NativePoint batchPoint;
    private int remaining;
    private bool injectedMouseDown;

    public GameToolsBar()
    {
        Dock = DockStyle.Bottom;
        Height = 44;
        Padding = new Padding(8);
        WrapContents = false;
        BackColor = Color.FromArgb(230, 235, 241);
        Controls.AddRange(new Control[] { enabled, new Label { Text = "间隔(ms)", AutoSize = true, Margin = new Padding(8, 5, 0, 0) }, interval, state });
        callback = OnMouse;
        enabled.CheckedChanged += (_, _) => SetEnabled();
        interval.ValueChanged += (_, _) => timer.Interval = (int)interval.Value;
        timer.Interval = (int)interval.Value;
        timer.Tick += (_, _) => SendNextClick();
        enabled.Checked = true;
    }

    private void SetEnabled()
    {
        StopBatch();
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        capturedButton = 0;
        if (enabled.Checked)
        {
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                enabled.Checked = false;
                state.Text = "无法启用鼠标监听：" + Marshal.GetLastWin32Error();
                return;
            }
        }
        state.Text = enabled.Checked ? "已开启：Ctrl＋左键100次（保持鼠标在图标上）" : "生产连点器已关闭";
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(hook, code, message, data);
        uint action = unchecked((uint)message.ToInt64());
        MouseData mouse = Marshal.PtrToStructure<MouseData>(data);
        // Only physical clicks trigger a batch. Never feed injected clicks back into the macro.
        if ((mouse.Flags & 1) != 0) return CallNextHookEx(hook, code, message, data);
        if (capturedButton != 0 && action == capturedButton + 1)
        {
            capturedButton = 0;
            if (enabled.Checked && GetForegroundWindow() == capturedWindow && Near(mouse.Point, capturedPoint))
            {
                StopBatch();
                batchWindow = capturedWindow;
                batchPoint = capturedPoint;
                remaining = MapPatcher.ProductionQueueLimit;
                state.Text = "正在发送生产点击：0/100";
                StartupTrace.Mark("production click batch started: Ctrl+left");
                timer.Start();
            }
            return new IntPtr(1);
        }
        bool produce = action == 0x0201 && IsProductionTrigger(action, Down(0x11), Down(0x10), Down(0x12));
        if (enabled.Checked && capturedButton == 0 && produce && TryGetProductionWindow(mouse.Point, out IntPtr window))
        {
            StopBatch();
            capturedButton = action;
            capturedWindow = window;
            capturedPoint = mouse.Point;
            return new IntPtr(1);
        }
        // Any unrelated physical button action interrupts a pending batch.
        if (action is 0x0201 or 0x0204 or 0x0207 or 0x020B) StopBatch();
        return CallNextHookEx(hook, code, message, data);
    }

    private static bool IsProductionTrigger(uint action, bool control, bool shift, bool alt) => action == 0x0201 && control && !shift && !alt;

    private static bool TryGetProductionWindow(NativePoint point, out IntPtr window)
    {
        window = GetForegroundWindow();
        if (window == IntPtr.Zero || GetAncestor(WindowFromPoint(point), 2) != window) return false;
        GetWindowThreadProcessId(window, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var config = LauncherConfig.Load();
            if (!string.Equals(process.MainModule?.FileName, Path.Combine(config.RuntimePath, "gamemd-spawn.exe"), StringComparison.OrdinalIgnoreCase)) return false;
            if (!GetClientRect(window, out NativeRect rect) || !ScreenToClient(window, ref point) || rect.Right <= 0 || rect.Bottom <= 0) return false;
            // The active game may use a resolution different from the last saved launcher selection.
            Size size = ReadGameSize(config);
            double x = point.X * (double)size.Width / rect.Right;
            double y = point.Y * (double)size.Height / rect.Bottom;
            // Include the first cameo row; exclude the radar, outside borders and bottom controls.
            bool inProductionArea = x >= size.Width - 152 && x < size.Width - 8 && y >= 194 && y < size.Height - 48;
            if (!inProductionArea) StartupTrace.Mark($"production click ignored outside sidebar: x={x:F0}, y={y:F0}, game={size.Width}x{size.Height}");
            return inProductionArea;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static Size ReadGameSize(LauncherConfig config)
    {
        int width = config.ResolutionWidth, height = config.ResolutionHeight;
        try
        {
            bool video = false;
            foreach (string source in File.ReadLines(Path.Combine(config.RuntimePath, "RA2MD.INI")))
            {
                string line = source.Trim();
                if (line.StartsWith('[')) { video = line.Equals("[Video]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!video) continue;
                string[] pair = line.Split('=', 2);
                if (pair.Length != 2 || !int.TryParse(pair[1].Split(';')[0].Trim(), out int value) || value <= 0) continue;
                if (pair[0].Trim().Equals("ScreenWidth", StringComparison.OrdinalIgnoreCase)) width = value;
                if (pair[0].Trim().Equals("ScreenHeight", StringComparison.OrdinalIgnoreCase)) height = value;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new Size(width, height);
    }

    private void SendNextClick()
    {
        if (!enabled.Checked || remaining <= 0 || GetForegroundWindow() != batchWindow || !GetCursorPos(out NativePoint cursor) || !Near(cursor, batchPoint))
        {
            StopBatch();
            return;
        }
        // DirectInput games need system input, not queued WM_* messages. Keep down/up on separate ticks
        // so the game can observe both states instead of missing an instantaneous press/release pair.
        bool press = !injectedMouseDown;
        if (!SendMouseInput(press))
        {
            StopBatch();
            state.Text = "点击发送失败，请检查游戏与加载器权限是否一致";
            return;
        }
        injectedMouseDown = press;
        if (press) return;
        remaining--;
        state.Text = $"已发送生产点击：{MapPatcher.ProductionQueueLimit - remaining}/{MapPatcher.ProductionQueueLimit}";
        if (remaining == 0)
        {
            timer.Stop();
            state.Text = "100次点击已发送（请查看游戏队列）";
            StartupTrace.Mark("production click batch completed: 100 system input clicks");
        }
    }

    private void StopBatch()
    {
        timer.Stop();
        if (injectedMouseDown) { SendMouseInput(false); injectedMouseDown = false; }
        if (remaining > 0) { state.Text = $"批量点击已停止：{MapPatcher.ProductionQueueLimit - remaining}/{MapPatcher.ProductionQueueLimit}"; StartupTrace.Mark($"production click batch stopped: {MapPatcher.ProductionQueueLimit - remaining}/{MapPatcher.ProductionQueueLimit}"); }
        remaining = 0;
    }
    private static bool SendMouseInput(bool down)
    {
        var inputs = new[] { new Input { Type = 0, Mouse = new MouseInput { Flags = down ? 0x0002u : 0x0004u } } };
        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }
    private static bool Near(NativePoint a, NativePoint b) => Math.Abs((long)a.X - b.X) <= 3 && Math.Abs((long)a.Y - b.Y) <= 3;
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopBatch();
            timer.Dispose();
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public NativePoint Point; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public MouseInput Mouse; }
    private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, MouseHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
