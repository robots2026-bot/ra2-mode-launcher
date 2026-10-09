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
    public const int ProtocolVersion = 5;

    private sealed class Peer(Guid id, TcpClient client, StreamWriter writer)
    {
        public Guid Id { get; } = id;
        public TcpClient Client { get; } = client;
        public StreamWriter Writer { get; } = writer;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string KnownMapHash { get; set; } = "";
    }

    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TcpListener listener;
    private readonly bool announce;
    private string phase = "等待中";
    private readonly Dictionary<Guid, TaskCompletionSource<string?>> launchChecks = [];
    private readonly HashSet<Guid> playing = [];
    private int pendingGameId;
    private int disposed;
    public Func<LanLaunchPackage, Task<string?>>? PrepareLaunch { get; set; }
    private readonly Dictionary<Guid, Peer> peers = [];
    private readonly List<LanPlayer> players = [];
    private LanGameSetup setup;
    private readonly string roomName;
    private readonly Guid hostId = Guid.NewGuid();

    public event Action<LanRoomState>? StateChanged;
    public event Action<LanLaunchPackage>? Launching;
    public event Action<string>? Error;

    public LanLobbyHost(string roomName, string hostName, LanGameSetup setup, int port = LobbyPort, bool announce = true)
    {
        setup.ValidateSpeed();
        listener = new TcpListener(IPAddress.Any, port);
        this.announce = announce;
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
        if (announce) _ = AnnounceLoopAsync(stop.Token);
        RaiseStateChanged();
    }

    public void SetHostReady(bool ready)
    {
        lock (gate)
        {
            EnsureWaiting();
            int index = players.FindIndex(p => p.Id == hostId);
            players[index] = players[index] with { Ready = ready };
        }
        _ = BroadcastStateAsync();
    }

    public void UpdateSetup(LanGameSetup updated)
    {
        updated.ValidateSpeed();
        lock (gate)
        {
            EnsureWaiting();
            int maxHumans = GetMaxHumanPlayers(updated);
            if (players.Count > maxHumans) throw new InvalidOperationException($"当前有 {players.Count} 名真人，至少需要保留 {players.Count - 1} 个开放位置。");
            if (players.Any(player => player.SlotIndex < 0 || player.SlotIndex >= updated.Slots.Count || updated.Slots[player.SlotIndex].Closed || updated.Slots[player.SlotIndex].Computer))
                throw new InvalidOperationException("不能删除、关闭或改为电脑：该位置已有玩家。");
            setup = updated;
            for (int i = 0; i < players.Count; i++) players[i] = players[i] with { Ready = false };
        }
        _ = BroadcastStateAsync();
    }

    public void UpdateHostPlayer(LanSlot slot) => UpdatePlayer(hostId, slot);
    public void UpdateHostName(string name) => UpdateName(hostId, name);
    private void UpdateName(Guid id, string name)
    {
        lock (gate)
        {
            EnsureWaiting();
            string clean = SanitizeName(name);
            if (clean.Length == 0) throw new InvalidOperationException("玩家名称不能为空。");
            int index = players.FindIndex(player => player.Id == id);
            if (index < 0) throw new InvalidOperationException("玩家不存在。");
            players[index] = players[index] with { Name = clean };
            ResetReady();
        }
        _ = BroadcastStateAsync();
    }

    public bool CanLaunch(out string reason)
    {
        lock (gate)
        {
            if (phase != "等待中") { reason = phase; return false; }
            if (players.Count > setup.Slots.Count) { reason = "玩家数量超过地图容量。"; return false; }
            if (players.Count > 1 && players.Any(p => !p.Ready)) { reason = "还有玩家未准备。"; return false; }
        }
        reason = "";
        return true;
    }

    public async Task LaunchAsync()
    {
        LanLaunchPackage package;
        lock (gate)
        {
            if (!CanLaunch(out string reason)) throw new InvalidOperationException(reason);
            package = new LanLaunchPackage(Random.Shared.Next(1, int.MaxValue), Snapshot().Setup, [.. players]);
            phase = "同步中";
            pendingGameId = package.GameId;
            launchChecks.Clear();
            foreach (Guid id in peers.Keys) launchChecks[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            await BroadcastStateAsync();
            string? hostError = PrepareLaunch is null ? "房主未配置启动检查。" : await PrepareLaunch(package).WaitAsync(TimeSpan.FromSeconds(15), stop.Token);
            if (hostError is not null) throw new InvalidOperationException(hostError);
            Task<string?>[] checks;
            lock (gate) checks = launchChecks.Values.Select(check => check.Task).ToArray();
            await BroadcastAsync(new LanMessage { Type = "prepare-launch", Launch = package });
            string?[] results = await Task.WhenAll(checks).WaitAsync(TimeSpan.FromSeconds(15), stop.Token);
            string? failed = results.FirstOrDefault(error => error is not null);
            if (failed is not null) throw new InvalidOperationException(failed);
            lock (gate)
            {
                if (players.Count != package.Players.Count || package.Players.Any(player => players.All(current => current.Id != player.Id))) throw new InvalidOperationException("同步期间有玩家离开。");
                phase = "游戏中";
                playing.UnionWith(players.Select(player => player.Id));
            }
            await BroadcastStateAsync();
            await BroadcastAsync(new LanMessage { Type = "launch", Launch = package });
            Launching?.Invoke(package);
        }
        catch
        {
            lock (gate) { phase = "等待中"; playing.Clear(); ResetReady(); }
            await BroadcastStateAsync();
            throw;
        }
        finally { lock (gate) launchChecks.Clear(); }
    }

    private void EnsureWaiting() { if (phase != "等待中") throw new InvalidOperationException($"房间{phase}，暂时不能修改或加入。"); }
    private void ResetReady() { for (int i = 0; i < players.Count; i++) players[i] = players[i] with { Ready = false }; }
    public void SetSoloPlaying() { lock (gate) { EnsureWaiting(); phase = "游戏中"; playing.Add(hostId); } _ = BroadcastStateAsync(); }
    public void ReportGameExited() => GameExited(hostId);
    private void GameExited(Guid id)
    {
        lock (gate) { playing.Remove(id); if (phase == "游戏中" && playing.Count == 0) { phase = "等待中"; ResetReady(); } }
        _ = BroadcastStateAsync();
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
            LanMessage? join = Deserialize(await reader.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(15), token));
            if (join?.Type != "join" || string.IsNullOrWhiteSpace(join.Name)) throw new InvalidDataException("无效的加入请求。");
            if (join.ProtocolVersion != ProtocolVersion) throw new InvalidDataException("启动器协议不一致，请双方更新启动器。");

            lock (gate)
            {
                EnsureWaiting();
                if (players.Count >= GetMaxHumanPlayers(setup)) throw new InvalidOperationException("房间没有开放的真人位置。");
                var occupiedIndices = players.Select(player => player.SlotIndex).ToHashSet();
                int next = Enumerable.Range(0, setup.Slots.Count).FirstOrDefault(index => !setup.Slots[index].Closed && !setup.Slots[index].Computer && !occupiedIndices.Contains(index), -1);
                if (next < 0) throw new InvalidOperationException("房间没有开放的真人位置。");
                List<LanSlot> slots = [.. setup.Slots];
                var occupied = slots.Where((slot, index) => !slot.Closed && (occupiedIndices.Contains(index) || slot.Computer)).ToList();
                slots[next] = slots[next] with
                {
                    Color = Enumerable.Range(0, 8).First(color => occupied.All(slot => slot.Color != color)),
                    Start = Enumerable.Range(1, slots.Count).First(start => occupied.All(slot => slot.Start != start))
                };
                setup = setup with { Slots = slots };
                string address = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                players.Add(new LanPlayer(id, SanitizeName(join.Name), address, false, false, next));
                peer = new Peer(id, client, writer);
                peers.Add(id, peer);
            }
            await WritePeerAsync(peer, new LanMessage { Type = "welcome", PlayerId = id, ProtocolVersion = ProtocolVersion });
            await BroadcastStateAsync();

            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(15), token);
                if (line is null) break;
                LanMessage? message = Deserialize(line);
                if (message?.Type == "ping") { await WritePeerAsync(peer, new LanMessage { Type = "pong" }); continue; }
                if (message?.Type == "launch-check")
                {
                    lock (gate) if (message.GameId == pendingGameId && launchChecks.TryGetValue(id, out var check)) check.TrySetResult(message.Error);
                    continue;
                }
                if (message?.Type == "game-exited") { GameExited(id); continue; }
                if (message?.Type == "player-name")
                {
                    try { UpdateName(id, message.Name ?? ""); }
                    catch (Exception ex) { await WritePeerAsync(peer, new LanMessage { Type = "notice", Error = ex.Message }); await BroadcastStateAsync(); }
                    continue;
                }
                if (message?.Type == "ready")
                {
                    lock (gate)
                    {
                        if (phase != "等待中") continue;
                        int index = players.FindIndex(p => p.Id == id);
                        if (index >= 0) players[index] = players[index] with { Ready = message.Ready };
                    }
                    await BroadcastStateAsync();
                }
                else if (message?.Type == "player-options" && message.Slot is not null)
                {
                    try { UpdatePlayer(id, message.Slot); }
                    catch (Exception ex) { await WritePeerAsync(peer, new LanMessage { Type = "notice", Error = ex.Message }); await BroadcastStateAsync(); }
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
                if (launchChecks.TryGetValue(id, out var check)) check.TrySetResult("玩家在同步期间断开连接。");
                playing.Remove(id);
                if (phase == "游戏中" && playing.Count == 0) phase = "等待中";
                int departed = players.FindIndex(p => p.Id == id);
                if (departed >= 0)
                {
                    // A departure opens only this player's fixed slot; no other row moves.
                    players.RemoveAt(departed);
                    for (int i = 0; i < players.Count; i++) players[i] = players[i] with { Ready = false };
                }
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
                int openSlots = state.Phase == "等待中" ? Math.Max(0, state.MaxHumanPlayers - state.Players.Count) : 0;
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(new LanRoomAnnouncement(
                    roomName,
                    state.Players.FirstOrDefault(player => player.IsHost)?.Name ?? "",
                    setup.MapName,
                    setup.MapFileName,
                    setup.MapHash,
                    setup.ComponentHash,
                    setup.Ra2Mode,
                    state.Players.Count,
                    state.MaxHumanPlayers,
                    openSlots,
                    setup.Credits,
                    setup.GameSpeed,
                    setup.MaxGameTicks,
                    setup.Crates,
                    setup.SuperWeapons,
                    setup.ShortGame,
                    setup.RevealAllMap, state.Phase));
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
        Peer[] targets;
        lock (gate) targets = [.. peers.Values];
        foreach (Peer peer in targets)
        {
            try
            {
                await peer.WriteLock.WaitAsync(stop.Token);
                try
                {
                    LanMessage outbound = message;
                    if (message.Type == "state" && message.State is not null && peer.KnownMapHash == message.State.Setup.MapHash)
                        outbound = new LanMessage { Type = "state", State = message.State with { Setup = message.State.Setup with { MapData = [] } } };
                    await peer.Writer.WriteLineAsync(JsonSerializer.Serialize(outbound)).WaitAsync(TimeSpan.FromSeconds(10), stop.Token);
                    if (message.State is not null) peer.KnownMapHash = message.State.Setup.MapHash;
                }
                finally { peer.WriteLock.Release(); }
            }
            catch { }
        }
    }

    private async Task WritePeerAsync(Peer peer, LanMessage message)
    {
        await peer.WriteLock.WaitAsync(stop.Token);
        try { await peer.Writer.WriteLineAsync(JsonSerializer.Serialize(message)).WaitAsync(TimeSpan.FromSeconds(10), stop.Token); }
        finally { peer.WriteLock.Release(); }
    }

    private LanRoomState Snapshot() => new(roomName, setup.MapName, setup.Slots.Count, GetMaxHumanPlayers(setup), [.. players], setup with { Slots = [.. setup.Slots] }, phase);
    private void RaiseStateChanged() => RaiseStateChanged(CurrentState);
    private void RaiseStateChanged(LanRoomState state) => StateChanged?.Invoke(state);
    private static LanMessage? Deserialize(string? line) => string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<LanMessage>(line);
    private static string SanitizeName(string name) => name.Replace("\r", " ").Replace("\n", " ").Trim()[..Math.Min(name.Trim().Length, 20)];
    private static int GetMaxHumanPlayers(LanGameSetup value) => value.Slots.Count(slot => !slot.Computer && !slot.Closed);

    private void UpdatePlayer(Guid id, LanSlot requested)
    {
        lock (gate)
        {
            EnsureWaiting();
            int playerIndex = players.FirstOrDefault(player => player.Id == id)?.SlotIndex ?? -1;
            if (playerIndex < 0 || playerIndex >= setup.Slots.Count) throw new InvalidOperationException("玩家位置不存在。");
            if (requested.Color is < 0 or > 7 || requested.Start < 1 || requested.Start > setup.Slots.Count || requested.Team is < 0 or > 4 || requested.Country < 0 || requested.Country > (setup.Ra2Mode ? 8 : 9)) throw new InvalidOperationException("国家、队伍、颜色或出生点无效。");
            List<LanSlot> slots = [.. setup.Slots];
            var occupiedIndices = players.Select(player => player.SlotIndex).ToHashSet();
            requested = requested with { Computer = false, Closed = false };
            if (slots.Where((slot, index) => index != playerIndex && !slot.Closed && (occupiedIndices.Contains(index) || slot.Computer)).Any(slot => slot.Color == requested.Color)) throw new InvalidOperationException("该颜色已被占用。");
            if (slots.Where((slot, index) => index != playerIndex && !slot.Closed && (occupiedIndices.Contains(index) || slot.Computer)).Any(slot => slot.Start == requested.Start)) throw new InvalidOperationException("该出生点已被占用。");
            slots[playerIndex] = requested;
            setup = setup with { Slots = slots };
            ResetReady();
        }
        _ = BroadcastStateAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        listener.Stop();
        lock (gate) foreach (Peer peer in peers.Values) peer.Client.Dispose();
        // Background readers may still be unwinding; leave the token source valid until collected.
    }
}

internal sealed class LanLobbyClient : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly TcpClient client = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly TaskCompletionSource<bool> connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string mapHash = "";
    private byte[] mapData = [];
    private int disposed;
    public Func<LanLaunchPackage, Task<string?>>? PrepareLaunch { get; set; }
    private StreamWriter? writer;
    public Guid PlayerId { get; private set; }
    public string HostAddress { get; private set; } = "";
    public event Action<LanRoomState>? StateChanged;
    public event Action<LanLaunchPackage>? LaunchReceived;
    public event Action<string>? Error;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(string host, string playerName, int port = LanLobbyHost.LobbyPort)
    {
        await client.ConnectAsync(host, port, stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), stop.Token);
        client.NoDelay = true;
        HostAddress = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
        NetworkStream stream = client.GetStream();
        writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = true };
        await SendAsync(new LanMessage { Type = "join", Name = playerName, ProtocolVersion = LanLobbyHost.ProtocolVersion });
        _ = ReceiveLoopAsync(new StreamReader(stream, new UTF8Encoding(false), false, 8192, leaveOpen: true), stop.Token);
        _ = HeartbeatAsync(stop.Token);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10), stop.Token);
    }

    public Task SetReadyAsync(bool ready) => SendAsync(new LanMessage { Type = "ready", Ready = ready });
    public Task UpdatePlayerAsync(LanSlot slot) => SendAsync(new LanMessage { Type = "player-options", Slot = slot });
    public Task ReportGameExitedAsync() => SendAsync(new LanMessage { Type = "game-exited" });
    public Task UpdateNameAsync(string name) => SendAsync(new LanMessage { Type = "player-name", Name = name });

    private async Task SendAsync(LanMessage message)
    {
        if (writer is null) throw new InvalidOperationException("尚未连接房间。");
        await writeLock.WaitAsync(stop.Token);
        try { await writer.WriteLineAsync(JsonSerializer.Serialize(message)).WaitAsync(TimeSpan.FromSeconds(10), stop.Token); }
        finally { writeLock.Release(); }
    }

    private async Task HeartbeatAsync(CancellationToken token)
    {
        try { while (!token.IsCancellationRequested) { await Task.Delay(3000, token); await SendAsync(new LanMessage { Type = "ping" }); } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) { connected.TrySetException(ex); Disconnected?.Invoke(ex.Message); client.Dispose(); } }
    }

    private async Task ReceiveLoopAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(20), token);
                if (line is null) throw new IOException("与房主的连接已断开。");
                LanMessage? message = JsonSerializer.Deserialize<LanMessage>(line);
                if (message?.Type == "welcome")
                {
                    if (message.ProtocolVersion != LanLobbyHost.ProtocolVersion) throw new InvalidDataException("启动器协议不一致，请双方更新启动器。");
                    PlayerId = message.PlayerId;
                }
                else if (message?.Type == "state" && message.State is not null)
                {
                    message.State.Setup.ValidateSpeed();
                    if (message.State.Setup.MapData.Length > 0)
                    {
                        if (LanGameSetup.Hash(message.State.Setup.MapData) != message.State.Setup.MapHash) throw new InvalidDataException("收到的地图校验失败。");
                        mapHash = message.State.Setup.MapHash; mapData = message.State.Setup.MapData;
                    }
                    else
                    {
                        if (mapHash != message.State.Setup.MapHash) throw new InvalidDataException("地图尚未同步。");
                        message.State = message.State with { Setup = message.State.Setup with { MapData = mapData } };
                    }
                    StateChanged?.Invoke(message.State);
                    connected.TrySetResult(true);
                }
                else if (message?.Type == "prepare-launch" && message.Launch is not null)
                {
                    string? error;
                    try { message.Launch.Setup.ValidateSpeed(); error = PrepareLaunch is null ? "客户端未配置启动检查。" : await PrepareLaunch(message.Launch); }
                    catch (Exception ex) { error = ex.Message; }
                    await SendAsync(new LanMessage { Type = "launch-check", GameId = message.Launch.GameId, Error = error });
                }
                else if (message?.Type == "launch" && message.Launch is not null) LaunchReceived?.Invoke(message.Launch);
                else if (message?.Type == "error") throw new InvalidOperationException(message.Error ?? "房主拒绝了连接。");
                else if (message?.Type == "notice") Error?.Invoke(message.Error ?? "房主拒绝了这项修改。");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!stop.IsCancellationRequested) { connected.TrySetException(ex); Disconnected?.Invoke(ex.Message); stop.Cancel(); client.Dispose(); } }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        client.Dispose();
    }
}

internal sealed class LanDiscoveryService : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly UdpClient udp;
    private int disposed;
    public event Action<DiscoveredLanRoom>? RoomFound;

    public LanDiscoveryService(int port = LanLobbyHost.DiscoveryPort)
    {
        udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _ = ReceiveLoopAsync(stop.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(token);
                try
                {
                    LanRoomAnnouncement? room = JsonSerializer.Deserialize<LanRoomAnnouncement>(result.Buffer);
                    if (room is not null && room.Players >= 1 && room.Capacity is >= 1 and <= 8) RoomFound?.Invoke(new DiscoveredLanRoom(result.RemoteEndPoint.Address.ToString(), room));
                }
                catch (JsonException) { /* Ignore unrelated or malformed LAN datagrams and keep searching. */ }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        udp.Dispose();
    }
}
