using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Ra2ModeLauncher;

internal static class LanNetworkAddress
{
    public static string GetPreferredIPv4()
    {
        IEnumerable<NetworkInterface> candidates = NetworkInterface.GetAllNetworkInterfaces().Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        foreach (NetworkInterface adapter in candidates.OrderByDescending(adapter => adapter.GetIPProperties().GatewayAddresses.Count > 0))
        {
            UnicastIPAddressInformation? address = adapter.GetIPProperties().UnicastAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(item.Address) && !item.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
            if (address is not null) return address.Address.ToString();
        }
        return "";
    }
}

internal sealed class LanLobbyHost : IDisposable
{
    public const int DiscoveryPort = 1232;
    public const int LobbyPort = 1233;
    public const int GamePort = 1234;

    private sealed class Peer(Guid id, TcpClient client, StreamWriter writer)
    {
        public Guid Id { get; } = id;
        public TcpClient Client { get; } = client;
        public StreamWriter Writer { get; } = writer;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TcpListener listener = new(IPAddress.Any, LobbyPort);
    private readonly Dictionary<Guid, Peer> peers = [];
    private readonly List<LanPlayer> players = [];
    private LanGameSetup setup;
    private readonly string roomName;
    private readonly Guid hostId = Guid.NewGuid();

    public event Action<LanRoomState>? StateChanged;
    public event Action<LanLaunchPackage>? Launching;
    public event Action<string>? Error;

    public LanLobbyHost(string roomName, string hostName, LanGameSetup setup)
    {
        this.roomName = roomName;
        this.setup = setup;
        players.Add(new LanPlayer(hostId, SanitizeName(hostName), LanNetworkAddress.GetPreferredIPv4(), false, true));
    }

    public Guid HostId => hostId;
    public LanRoomState CurrentState { get { lock (gate) return Snapshot(); } }

    public void Start()
    {
        listener.Start();
        _ = AcceptLoopAsync(stop.Token);
        _ = AnnounceLoopAsync(stop.Token);
        RaiseStateChanged();
    }

    public void SetHostReady(bool ready)
    {
        lock (gate)
        {
            int index = players.FindIndex(p => p.Id == hostId);
            players[index] = players[index] with { Ready = ready };
        }
        _ = BroadcastStateAsync();
    }

    public void UpdateSetup(LanGameSetup updated)
    {
        lock (gate)
        {
            int maxHumans = GetMaxHumanPlayers(updated);
            if (players.Count > maxHumans) throw new InvalidOperationException($"当前有 {players.Count} 名真人，至少需要保留 {players.Count - 1} 个开放位置。");
            setup = updated;
            for (int i = 0; i < players.Count; i++) players[i] = players[i] with { Ready = false };
        }
        _ = BroadcastStateAsync();
    }

    public void UpdateHostPlayer(LanSlot slot) => UpdatePlayer(hostId, slot);

    public bool CanLaunch(out string reason)
    {
        lock (gate)
        {
            if (players.Count > setup.Slots.Count) { reason = "玩家数量超过地图容量。"; return false; }
            if (players.Count > 1 && players.Any(p => !p.Ready)) { reason = "还有玩家未准备。"; return false; }
        }
        reason = "";
        return true;
    }

    public async Task LaunchAsync()
    {
        if (!CanLaunch(out string reason)) throw new InvalidOperationException(reason);
        LanLaunchPackage package;
        lock (gate) package = new LanLaunchPackage(Random.Shared.Next(1, int.MaxValue), setup, [.. players]);
        await BroadcastAsync(new LanMessage { Type = "launch", Launch = package });
        Launching?.Invoke(package);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token);
                _ = HandlePeerAsync(client, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }

    private async Task HandlePeerAsync(TcpClient client, CancellationToken token)
    {
        Guid id = Guid.NewGuid();
        Peer? peer = null;
        try
        {
            client.NoDelay = true;
            NetworkStream stream = client.GetStream();
            var reader = new StreamReader(stream, new UTF8Encoding(false), false, 8192, leaveOpen: true);
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = true };
            LanMessage? join = Deserialize(await reader.ReadLineAsync(token));
            if (join?.Type != "join" || string.IsNullOrWhiteSpace(join.Name)) throw new InvalidDataException("无效的加入请求。");

            lock (gate)
            {
                if (players.Count >= GetMaxHumanPlayers(setup)) throw new InvalidOperationException("房间没有开放的真人位置。");
                string address = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                players.Add(new LanPlayer(id, SanitizeName(join.Name), address, false, false));
                peer = new Peer(id, client, writer);
                peers.Add(id, peer);
            }
            await writer.WriteLineAsync(JsonSerializer.Serialize(new LanMessage { Type = "welcome", PlayerId = id }));
            await BroadcastStateAsync();

            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line is null) break;
                LanMessage? message = Deserialize(line);
                if (message?.Type == "ready")
                {
                    lock (gate)
                    {
                        int index = players.FindIndex(p => p.Id == id);
                        if (index >= 0) players[index] = players[index] with { Ready = message.Ready };
                    }
                    await BroadcastStateAsync();
                }
                else if (message?.Type == "player-options" && message.Slot is not null)
                {
                    try { UpdatePlayer(id, message.Slot); }
                    catch (Exception ex) { await writer.WriteLineAsync(JsonSerializer.Serialize(new LanMessage { Type = "notice", Error = ex.Message })); }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (peer is null)
            {
                try
                {
                    using var reject = new StreamWriter(client.GetStream(), new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                    await reject.WriteLineAsync(JsonSerializer.Serialize(new LanMessage { Type = "error", Error = ex.Message }));
                }
                catch { }
            }
        }
        finally
        {
            lock (gate)
            {
                peers.Remove(id);
                players.RemoveAll(p => p.Id == id);
            }
            client.Dispose();
            if (!stop.IsCancellationRequested) await BroadcastStateAsync();
        }
    }

    private async Task AnnounceLoopAsync(CancellationToken token)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var target = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        try
        {
            while (!token.IsCancellationRequested)
            {
                LanRoomState state = CurrentState;
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(new LanRoomAnnouncement(roomName, setup.MapName, state.Players.Count, state.MaxHumanPlayers));
                await udp.SendAsync(data, target, token);
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke($"房间广播失败：{ex.Message}"); }
    }

    private Task BroadcastStateAsync()
    {
        LanRoomState state;
        lock (gate) state = Snapshot();
        RaiseStateChanged(state);
        return BroadcastAsync(new LanMessage { Type = "state", State = state });
    }

    private async Task BroadcastAsync(LanMessage message)
    {
        string json = JsonSerializer.Serialize(message);
        Peer[] targets;
        lock (gate) targets = [.. peers.Values];
        foreach (Peer peer in targets)
        {
            try
            {
                await peer.WriteLock.WaitAsync(stop.Token);
                try { await peer.Writer.WriteLineAsync(json); }
                finally { peer.WriteLock.Release(); }
            }
            catch { }
        }
    }

    private LanRoomState Snapshot() => new(roomName, setup.MapName, setup.Slots.Count, GetMaxHumanPlayers(setup), [.. players], setup);
    private void RaiseStateChanged() => RaiseStateChanged(CurrentState);
    private void RaiseStateChanged(LanRoomState state) => StateChanged?.Invoke(state);
    private static LanMessage? Deserialize(string? line) => string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<LanMessage>(line);
    private static string SanitizeName(string name) => name.Replace("\r", " ").Replace("\n", " ").Trim()[..Math.Min(name.Trim().Length, 20)];
    private static int GetMaxHumanPlayers(LanGameSetup value) => Math.Min(value.Slots.Count, 1 + value.Slots.Skip(1).Count(slot => !slot.Computer && !slot.Closed));

    private void UpdatePlayer(Guid id, LanSlot requested)
    {
        lock (gate)
        {
            int playerIndex = players.FindIndex(player => player.Id == id);
            if (playerIndex < 0 || playerIndex >= setup.Slots.Count) throw new InvalidOperationException("玩家位置不存在。");
            if (requested.Color is < 0 or > 7 || requested.Start < 1 || requested.Start > setup.Slots.Count) throw new InvalidOperationException("颜色或出生点无效。");
            List<LanSlot> slots = [.. setup.Slots];
            requested = requested with { Computer = false, Closed = false };
            if (slots.Where((slot, index) => index != playerIndex && !slot.Closed).Any(slot => slot.Color == requested.Color)) throw new InvalidOperationException("该颜色已被占用。");
            if (slots.Where((slot, index) => index != playerIndex && !slot.Closed).Any(slot => slot.Start == requested.Start)) throw new InvalidOperationException("该出生点已被占用。");
            slots[playerIndex] = requested;
            setup = setup with { Slots = slots };
            players[playerIndex] = players[playerIndex] with { Ready = false };
        }
        _ = BroadcastStateAsync();
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        lock (gate) foreach (Peer peer in peers.Values) peer.Client.Dispose();
        stop.Dispose();
    }
}

internal sealed class LanLobbyClient : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly TcpClient client = new();
    private StreamWriter? writer;
    public Guid PlayerId { get; private set; }
    public string HostAddress { get; private set; } = "";
    public event Action<LanRoomState>? StateChanged;
    public event Action<LanLaunchPackage>? LaunchReceived;
    public event Action<string>? Error;

    public async Task ConnectAsync(string host, string playerName)
    {
        await client.ConnectAsync(host, LanLobbyHost.LobbyPort, stop.Token);
        client.NoDelay = true;
        HostAddress = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
        NetworkStream stream = client.GetStream();
        writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new LanMessage { Type = "join", Name = playerName }));
        _ = ReceiveLoopAsync(new StreamReader(stream, new UTF8Encoding(false), false, 8192, leaveOpen: true), stop.Token);
    }

    public Task SetReadyAsync(bool ready) => SendAsync(new LanMessage { Type = "ready", Ready = ready });
    public Task UpdatePlayerAsync(LanSlot slot) => SendAsync(new LanMessage { Type = "player-options", Slot = slot });

    private async Task SendAsync(LanMessage message)
    {
        if (writer is null) throw new InvalidOperationException("尚未连接房间。");
        await writer.WriteLineAsync(JsonSerializer.Serialize(message));
    }

    private async Task ReceiveLoopAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line is null) throw new IOException("与房主的连接已断开。");
                LanMessage? message = JsonSerializer.Deserialize<LanMessage>(line);
                if (message?.Type == "welcome") PlayerId = message.PlayerId;
                else if (message?.Type == "state" && message.State is not null)
                {
                    StateChanged?.Invoke(message.State);
                }
                else if (message?.Type == "launch" && message.Launch is not null) LaunchReceived?.Invoke(message.Launch);
                else if (message?.Type == "error") throw new InvalidOperationException(message.Error ?? "房主拒绝了连接。");
                else if (message?.Type == "notice") Error?.Invoke(message.Error ?? "房主拒绝了这项修改。");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!stop.IsCancellationRequested) Error?.Invoke(ex.Message); }
    }

    public void Dispose()
    {
        stop.Cancel();
        client.Dispose();
        stop.Dispose();
    }
}

internal sealed class LanDiscoveryService : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly UdpClient udp;
    public event Action<DiscoveredLanRoom>? RoomFound;

    public LanDiscoveryService()
    {
        udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanLobbyHost.DiscoveryPort));
        _ = ReceiveLoopAsync(stop.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(token);
                LanRoomAnnouncement? room = JsonSerializer.Deserialize<LanRoomAnnouncement>(result.Buffer);
                if (room is not null) RoomFound?.Invoke(new DiscoveredLanRoom(result.RemoteEndPoint.Address.ToString(), room));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        stop.Cancel();
        udp.Dispose();
        stop.Dispose();
    }
}
