using System.Runtime.InteropServices;

namespace Ra2ModeLauncher;

// Requires our source-built cnc-ddraw bridge. Stock DLLs deliberately fail the
// handshake: never report an INI edit or an unacknowledged message as live success.
internal sealed class CncSpeedClient
{
    internal const string MessageName = "Ra2ModeLauncher.CncSpeed.v1";
    internal const uint Cookie = 0x52413253, ReplyTag = 0x53410000, Query = uint.MaxValue;
    internal delegate bool Transport(IntPtr window, uint request, out uint reply);
    private readonly Transport transport;

    public CncSpeedClient() : this(Send) { }
    internal CncSpeedClient(Transport transport) => this.transport = transport;

    public bool TryRead(IntPtr window, out GameSpeedTarget target)
    {
        target = default;
        return window != IntPtr.Zero && transport(window, Query, out uint reply) && TryDecode(reply, out target);
    }

    public bool TrySet(IntPtr window, GameSpeedTarget requested, out GameSpeedTarget actual)
    {
        actual = default;
        if (window == IntPtr.Zero || !transport(window, (uint)requested.TicksPerSecond, out uint reply) || !TryDecode(reply, out actual)) return false;
        return actual == requested;
    }

    internal static bool TryDecode(uint reply, out GameSpeedTarget target)
    {
        target = default;
        uint encoded = reply & 0xFFFF;
        if ((reply & 0xFFFF0000) != ReplyTag || encoded is < 1 or > GameSpeedTarget.Maximum + 1) return false;
        target = new((int)encoded - 1);
        return true;
    }

    private static bool Send(IntPtr window, uint request, out uint reply)
    {
        reply = 0;
        uint message = RegisterWindowMessage(MessageName);
        if (message == 0 || SendMessageTimeout(window, message, new UIntPtr(request), new IntPtr(Cookie), 0x0003, 100, out UIntPtr result) == IntPtr.Zero) return false;
        ulong raw = result.ToUInt64();
        if (raw > uint.MaxValue) return false;
        reply = (uint)raw;
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
}
