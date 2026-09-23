namespace Ra2ModeLauncher;

internal enum RoomEntryMode
{
    Create,
    Join
}

internal sealed record RoomEntry(RoomEntryMode Mode, string PlayerName, string RoomName = "", string HostAddress = "")
{
    public static RoomEntry Create(string playerName, string roomName) => new(RoomEntryMode.Create, playerName, roomName);
    public static RoomEntry Join(string playerName, string hostAddress) => new(RoomEntryMode.Join, playerName, HostAddress: hostAddress);
}
