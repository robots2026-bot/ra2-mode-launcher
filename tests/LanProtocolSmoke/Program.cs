using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Ra2ModeLauncher;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS {name}");
}
static async Task Until(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(25, timeout.Token);
}
var portProbe = new TcpListener(IPAddress.Loopback, 0);
portProbe.Start(); int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
byte[] data = System.Text.Encoding.UTF8.GetBytes("[Basic]\nName=Test\n");
var setup = new LanGameSetup(true, "test", "test.map", data, LanGameSetup.Hash(data), "test-components", 10000, 1, 0, true, true, true, false,
    [new(0, 0, 0, 0, 1, false), new(1, 1, 0, 0, 2, false), new(2, 2, 1, 0, 3, true), new(3, 3, 0, 0, 4, false, true)]);
using var host = new LanLobbyHost("test-room", "host", setup, port, announce: false);
host.PrepareLaunch = _ => Task.FromResult<string?>(null);
host.Start();
host.UpdateHostPlayer(new(0, 1, 0, 0, 2, false));
Check(host.CurrentState.Setup.Slots[0].Start == 2, "开放位置不占用出生点和颜色");
using var guest = new LanLobbyClient();
LanRoomState? latest = null;
guest.StateChanged += state => latest = state;
guest.PrepareLaunch = _ => Task.FromResult<string?>("simulated mismatch");
await guest.ConnectAsync("127.0.0.1", "guest", port);
Check(guest.PlayerId != Guid.Empty && host.CurrentState.Players.Count == 2, "加入握手和玩家编号");
Check(host.CurrentState.Setup.Slots[1].Start != 2 && host.CurrentState.Setup.Slots[1].Color != 1, "加入者分配未占用位置");
host.SetHostReady(true); await guest.SetReadyAsync(true);
await Until(() => host.CanLaunch(out _));
host.UpdateSetup(host.CurrentState.Setup with { GameSpeed = 0, MaxGameTicks = 120 });
await Until(() => latest?.Setup.MaxGameTicks == 120);
Check(latest!.Setup.GameSpeed == 0 && host.CurrentState.Players.All(player => !player.Ready), "房主改速同步全员并清除准备");
try { host.UpdateSetup(host.CurrentState.Setup with { GameSpeed = 1, MaxGameTicks = 120 }); throw new Exception("invalid speed accepted"); }
catch (InvalidDataException) { }
Check(host.CurrentState.Setup.GameSpeed == 0 && host.CurrentState.Setup.MaxGameTicks == 120, "拒绝双重限速组合并保留房主设置");
using (var oldClient = new TcpClient())
{
    await oldClient.ConnectAsync(IPAddress.Loopback, port);
    using var oldWriter = new StreamWriter(oldClient.GetStream()) { AutoFlush = true };
    using var oldReader = new StreamReader(oldClient.GetStream());
    await oldWriter.WriteLineAsync(JsonSerializer.Serialize(new LanMessage { Type = "join", Name = "old", ProtocolVersion = 4 }));
    var rejection = JsonSerializer.Deserialize<LanMessage>((await oldReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
    Check(rejection?.Type == "error" && rejection.Error!.Contains("协议"), "旧加载器不能跳过速度校验");
}
await guest.UpdateNameAsync("renamed");
await Until(() => host.CurrentState.Players[1].Name == "renamed");
Check(true, "改名同步");
string? rejected = null;
guest.Error += error => rejected = error;
await guest.UpdatePlayerAsync(new(0, 2, 0, 0, 3, false));
await Until(() => rejected is not null);
Check(host.CurrentState.Setup.Slots[1].Color != 2, "占用位置拒绝并保留权威配置");
host.SetHostReady(true); await guest.SetReadyAsync(true);
await Until(() => host.CanLaunch(out _));
try { await host.LaunchAsync(); throw new Exception("failure was not reported"); }
catch (InvalidOperationException ex) when (ex.Message == "simulated mismatch") { }
Check(host.CurrentState.Phase == "等待中" && host.CurrentState.Players.All(player => !player.Ready), "任一客户端检查失败取消开局");
guest.PrepareLaunch = _ => Task.FromResult<string?>(null);
int launched = 0;
guest.LaunchReceived += _ => Interlocked.Increment(ref launched);
host.SetHostReady(true); await guest.SetReadyAsync(true);
await Until(() => host.CanLaunch(out _)); await host.LaunchAsync();
await Until(() => launched == 1);
Check(host.CurrentState.Phase == "游戏中", "全部确认后开局");
try { host.UpdateSetup(host.CurrentState.Setup with { GameSpeed = 1, MaxGameTicks = 0 }); throw new Exception("live speed changed"); }
catch (InvalidOperationException) { }
Check(host.CurrentState.Setup.MaxGameTicks == 120, "游戏中房主也不能修改房间速度");
using (var third = new LanLobbyClient())
{
    try { await third.ConnectAsync("127.0.0.1", "third", port); throw new Exception("unexpected join"); }
    catch (Exception ex) when (ex.Message.Contains("游戏中")) { Check(true, "游戏中拒绝加入"); }
}
host.ReportGameExited();
Check(host.CurrentState.Phase == "游戏中", "等待其他玩家退出");
await guest.ReportGameExitedAsync(); await Until(() => host.CurrentState.Phase == "等待中");
Check(true, "所有玩家退出恢复等待及再次准备");
await guest.SetReadyAsync(true); await Until(() => host.CurrentState.Players[1].Ready);
Check(latest?.Setup.MapData.SequenceEqual(data) == true, "省略重复地图时客户端缓存仍完整");
guest.Dispose(); await Until(() => host.CurrentState.Players.Count == 1);
Check(host.CurrentState.Setup.Slots[0].Start == 2, "断线保留房主出生点");

// Closed/open/AI rows keep their identities even when humans occupy non-contiguous slots.
var fixedSlots = host.CurrentState.Setup.Slots.ToList();
fixedSlots[1] = fixedSlots[1] with { Closed = true };
fixedSlots[3] = fixedSlots[3] with { Closed = false };
host.UpdateSetup(host.CurrentState.Setup with { Slots = fixedSlots });
Check(host.CurrentState.Setup.Slots.SequenceEqual(fixedSlots), "关闭和开放只改变原位置，不重排电脑和出生点");
using var laterGuest = new LanLobbyClient();
await laterGuest.ConnectAsync("127.0.0.1", "later", port);
Check(host.CurrentState.Players.Single(player => player.Id == laterGuest.PlayerId).SlotIndex == 3, "加入者使用第4行开放位置，跳过关闭和电脑行");
var beforeUpdate = host.CurrentState.Setup.Slots.ToList();
await laterGuest.UpdatePlayerAsync(new(4, 3, 2, 0, 4, false));
await Until(() => host.CurrentState.Setup.Slots[3].Country == 4);
Check(host.CurrentState.Setup.Slots.Take(3).SequenceEqual(beforeUpdate.Take(3)), "非连续玩家只修改自己的固定位置");
var occupied = host.CurrentState.Setup.Slots.ToList();
try
{
    var invalid = occupied.ToList(); invalid[3] = invalid[3] with { Closed = true };
    host.UpdateSetup(host.CurrentState.Setup with { Slots = invalid });
    throw new Exception("occupied slot was closed");
}
catch (InvalidOperationException) { }
Check(host.CurrentState.Setup.Slots.SequenceEqual(occupied), "拒绝关闭已加入玩家的位置并保留原列表");
var reopened = occupied.ToList(); reopened[1] = reopened[1] with { Closed = false };
host.UpdateSetup(host.CurrentState.Setup with { Slots = reopened });
using var earlierGuest = new LanLobbyClient();
await earlierGuest.ConnectAsync("127.0.0.1", "earlier", port);
Check(host.CurrentState.Players.Single(player => player.Id == earlierGuest.PlayerId).SlotIndex == 1, "重新开放第2行后新玩家回填原行");
var beforeDeparture = host.CurrentState.Setup.Slots.ToList();
laterGuest.Dispose(); await Until(() => host.CurrentState.Players.Count == 2);
Check(host.CurrentState.Players.Single(player => player.Id == earlierGuest.PlayerId).SlotIndex == 1 && host.CurrentState.Setup.Slots.SequenceEqual(beforeDeparture), "中途玩家退出不移动其他玩家或电脑");
earlierGuest.Dispose(); await Until(() => host.CurrentState.Players.Count == 1);

using var udpProbe = new UdpClient(0);
int discoveryPort = ((IPEndPoint)udpProbe.Client.LocalEndPoint!).Port;
udpProbe.Close();
using var discovery = new LanDiscoveryService(discoveryPort);
bool found = false; discovery.RoomFound += _ => found = true;
using var udp = new UdpClient();
await udp.SendAsync(System.Text.Encoding.UTF8.GetBytes("invalid-json"), new IPEndPoint(IPAddress.Loopback, discoveryPort));
var announcement = new LanRoomAnnouncement("test", "host", "map", "map.ini", "hash", "components", true, 1, 2, 1, 10000, 1, 0, true, true, true, false);
await udp.SendAsync(JsonSerializer.SerializeToUtf8Bytes(announcement), new IPEndPoint(IPAddress.Loopback, discoveryPort));
await Until(() => found);
Check(true, "无效广播后仍可发现房间");
Console.WriteLine("All LAN protocol smoke checks passed. No game process was started.");
