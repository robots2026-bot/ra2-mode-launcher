namespace Ra2ModeLauncher;

// One value for both launch-time and live cnc-ddraw pacing. Zero means unlimited,
// never engine GameSpeed=0 or cnc-ddraw's different maxgameticks=0 (automatic).
internal readonly record struct GameSpeedTarget
{
    public const int Maximum = 1000;
    public int TicksPerSecond { get; }
    public bool Unlimited => TicksPerSecond == 0;
    public int CncMaxGameTicks => Unlimited ? -1 : TicksPerSecond;
    public string DisplayName => Unlimited ? "不限速" : $"{TicksPerSecond}/秒";

    public GameSpeedTarget(int ticksPerSecond)
    {
        if (ticksPerSecond is < 0 or > Maximum) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "目标速度必须为 1–1000，或 0（不限速）。");
        TicksPerSecond = ticksPerSecond;
    }

    public static GameSpeedTarget FromLegacy(int engineSpeed, int maxGameTicks)
    {
        if (maxGameTicks == -1 && engineSpeed == 0) return new(0);
        if (maxGameTicks is >= 1 and <= Maximum && engineSpeed == 0) return new(maxGameTicks);
        if (maxGameTicks == 0 && engineSpeed is >= 1 and <= 6)
            return new(new[] { 0, 60, 30, 20, 15, 12, 10 }[engineSpeed]);
        throw new InvalidDataException("旧速度配置无效，不能自动迁移。");
    }

    public GameSpeedTarget Step(int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (Unlimited) return direction > 0 ? this : new(Maximum);
        int next = TicksPerSecond + direction * 5;
        return new(next > Maximum ? 0 : Math.Max(1, next));
    }
}
