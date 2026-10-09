namespace Ra2ModeLauncher;

internal sealed record Choice(string Name, int Value)
{
    public override string ToString() => Name;
}

internal sealed record GameSpeedChoice(string Name, int GameSpeed, int MaxGameTicks, bool Custom = false)
{
    public override string ToString() => Name;
}

internal sealed record ResolutionChoice(string Name, int Width, int Height)
{
    public override string ToString() => Name;
}

internal sealed record MapInfo(string Path, string Name, int StartingPoints, IReadOnlyList<PointF> StartPositions)
{
    public override string ToString() => $"{Name}  ({StartingPoints} 人)";
}

internal sealed record SaveInfo(string Path, string DisplayName, DateTime LastWriteTime, bool UsesAresExtensions)
{
    public override string ToString() => $"{DisplayName}  |  {System.IO.Path.GetFileName(Path)}  |  {LastWriteTime:yyyy-MM-dd HH:mm:ss}";
}

internal sealed class ParticipantRow
{
    public int SlotType { get; set; } = 2;
    public string Name { get; set; } = "电脑";
    public string ReadyStatus { get; set; } = "等待加入";
    public int Country { get; set; } = GameData.Countries[8].Value;
    public int Color { get; set; } = GameData.Colors[1].Value;
    public int Team { get; set; }
    public int Difficulty { get; set; } = GameData.Difficulties[1].Value;
    public int Start { get; set; } = 2;
}

internal static class GameData
{
    public static readonly Choice[] SlotTypes = [new("玩家", 0), new("电脑", 1), new("开放", 2), new("关闭", 3)];
    public static readonly Choice[] Teams = [new("无队伍", 0), new("队伍 1", 1), new("队伍 2", 2), new("队伍 3", 3), new("队伍 4", 4)];
    public static readonly Choice[] Starts = Enumerable.Range(1, 8).Select(value => new Choice($"位置 {value}", value)).ToArray();
    public static readonly GameSpeedChoice[] GameSpeeds =
    [
        new("不限速（联机实验）", 0, -1), new("超快（120·联机实验）", 0, 120), new("极快（90·联机实验）", 0, 90), new("更快（75·联机实验）", 0, 75),
        new("很快（推荐）", 1, 0), new("较快", 2, 0), new("正常", 3, 0), new("较慢", 4, 0), new("很慢", 5, 0), new("最慢", 6, 0), new("自定义（联机实验）", 0, 60, true)
    ];
    public static readonly ResolutionChoice[] Resolutions =
    [
        new("1024 × 768", 1024, 768), new("1280 × 720", 1280, 720), new("1280 × 800", 1280, 800),
        new("1366 × 768", 1366, 768), new("1600 × 900", 1600, 900), new("1920 × 1080（推荐）", 1920, 1080), new("2560 × 1440", 2560, 1440)
    ];

    public static readonly Choice[] Countries =
    [
        new("美国", 0), new("韩国", 1), new("法国", 2), new("德国", 3), new("英国", 4),
        new("利比亚", 5), new("伊拉克", 6), new("古巴", 7), new("苏俄", 8)
    ];

    public static readonly Choice[] YuriCountries = [.. Countries, new("尤里", 9)];

    public static readonly Choice[] Colors =
    [
        new("黄色", 0), new("红色", 1), new("蓝色", 2), new("绿色", 3),
        new("橙色", 4), new("浅蓝", 5), new("紫色", 6), new("粉色", 7)
    ];

    public static readonly Choice[] Difficulties = [new("简单", 2), new("中等", 1), new("困难", 0)];
}
