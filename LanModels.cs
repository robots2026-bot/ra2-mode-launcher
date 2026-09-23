using System.Security.Cryptography;

namespace Ra2ModeLauncher;

internal sealed record LanSlot(int Country, int Color, int Team, int Difficulty, int Start, bool Computer, bool Closed = false);

internal sealed record LanGameSetup(bool Ra2Mode, string MapName, string MapFileName, byte[] MapData, string MapHash, string ComponentHash, int Credits, int GameSpeed, int MaxGameTicks, bool Crates, bool SuperWeapons, bool ShortGame, bool RevealAllMap, List<LanSlot> Slots)
{
    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}

internal static class LanCompatibility
{
    private static readonly string[] CriticalFiles = ["gamemd-spawn.exe", "Ares.dll", "Phobos.dll", "CnCNet-Spawner.dll", "ra2mode.mix"];

    public static string ComputeComponentHash(string runtimePath)
    {
        using var combined = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string name in CriticalFiles)
        {
            string path = Path.Combine(runtimePath, name);
            if (!File.Exists(path)) throw new FileNotFoundException($"联机所需组件缺失：{name}", path);
            byte[] label = System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant() + "\n");
            combined.AppendData(label);
            using FileStream stream = File.OpenRead(path);
            byte[] buffer = new byte[128 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) combined.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(combined.GetHashAndReset());
    }
}

internal sealed record LanPlayer(Guid Id, string Name, string Address, bool Ready, bool IsHost);

internal sealed record LanRoomState(string RoomName, string MapName, int Capacity, int MaxHumanPlayers, List<LanPlayer> Players, LanGameSetup Setup);

internal sealed record LanRoomAnnouncement(string RoomName, string MapName, int Players, int Capacity);

internal sealed record LanLaunchPackage(int GameId, LanGameSetup Setup, List<LanPlayer> Players);

internal sealed class LanMessage
{
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public Guid PlayerId { get; set; }
    public bool Ready { get; set; }
    public LanSlot? Slot { get; set; }
    public LanRoomState? State { get; set; }
    public LanLaunchPackage? Launch { get; set; }
    public string? Error { get; set; }
}

internal sealed record DiscoveredLanRoom(string Address, LanRoomAnnouncement Announcement)
{
    public override string ToString() => $"{Announcement.RoomName}  |  {Announcement.MapName}  |  {Announcement.Players}/{Announcement.Capacity}  |  {Address}:{LanLobbyHost.LobbyPort}";
}
