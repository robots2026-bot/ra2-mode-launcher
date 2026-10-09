using Ra2ModeLauncher;

static void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); }
foreach (int rate in new[] { 0, 1, 10, 37, 60, 75, 90, 120, 999, 1000 })
{
    var target = new GameSpeedTarget(rate);
    Check(target.CncMaxGameTicks == (rate == 0 ? -1 : rate), "cnc conversion " + rate);
    Check(CncSpeedClient.TryDecode(CncSpeedClient.ReplyTag | (uint)(rate + 1), out var decoded) && decoded == target, "acknowledged target " + rate);
}
foreach (uint malformed in new uint[] { 0, 1, 120, 0x53410000, 0x534103EA, 0x5342003D, uint.MaxValue })
    Check(!CncSpeedClient.TryDecode(malformed, out _), "reject incompatible acknowledgement " + malformed);
int[] rates = [60, 30, 20, 15, 12, 10];
for (int i = 0; i < rates.Length; i++) Check(GameSpeedTarget.FromLegacy(i + 1, 0).TicksPerSecond == rates[i], "migrate original preset " + (i + 1));
Check(new GameSpeedTarget(1000).Step(1).Unlimited && new GameSpeedTarget(0).Step(-1).TicksPerSecond == 1000 && new GameSpeedTarget(1).Step(-1).TicksPerSecond == 1, "hotkey bounds and unlimited transition");
var unsupported = new CncSpeedClient((IntPtr window, uint request, out uint reply) => { reply = 0; return true; });
Check(!unsupported.TryRead(new(1), out _) && !unsupported.TrySet(new(1), new(120), out _), "stock DLL cannot be mistaken for live support");
var stale = new CncSpeedClient((IntPtr window, uint request, out uint reply) => { reply = CncSpeedClient.ReplyTag | 61; return true; });
Check(!stale.TrySet(new(1), new(120), out _) && stale.TrySet(new(1), new(60), out _), "different readback is failure");
var timeout = new CncSpeedClient((IntPtr window, uint request, out uint reply) => { reply = CncSpeedClient.ReplyTag | 121; return false; });
Check(!timeout.TrySet(new(1), new(120), out _), "timeout is never a successful change");
Console.WriteLine("Managed control contract verified with simulated transport only; no native DLL or game tested.");
