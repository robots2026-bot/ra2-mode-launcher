using System.Diagnostics;

namespace Ra2ModeLauncher;

internal sealed class LanLobbyForm : Form
{
    private readonly string runtimePath;
    private readonly string playerName;
    private readonly LanGameSetup setup;
    private readonly TextBox roomName = new() { Text = "红警局域网房间", Width = 190 };
    private readonly TextBox hostAddress = new() { Text = "127.0.0.1", Width = 150 };
    private readonly ListBox rooms = new() { Dock = DockStyle.Fill, Height = 125 };
    private readonly ListView players = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, Height = 190 };
    private readonly CheckBox ready = new() { Text = "我已准备", AutoSize = true, Enabled = false };
    private readonly Button start = new() { Text = "开始游戏", AutoSize = true, Enabled = false };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkSlateBlue, Text = "请选择创建或加入房间。" };
    private readonly Dictionary<string, DiscoveredLanRoom> discovered = new(StringComparer.OrdinalIgnoreCase);
    private LanDiscoveryService? discovery;
    private LanLobbyHost? host;
    private LanLobbyClient? client;
    private bool launching;

    public LanLobbyForm(string runtimePath, string playerName, LanGameSetup setup)
    {
        this.runtimePath = runtimePath;
        this.playerName = playerName;
        this.setup = setup;
        Text = $"局域网联机 - {setup.MapName}";
        Font = new Font("Microsoft YaHei UI", 9f);
        Width = 760;
        Height = 610;
        MinimumSize = new Size(680, 540);
        StartPosition = FormStartPosition.CenterParent;

        players.Columns.Add("玩家", 180);
        players.Columns.Add("地址", 170);
        players.Columns.Add("身份", 90);
        players.Columns.Add("状态", 100);
        BuildLayout();
        FormClosed += (_, _) => DisposeNetworking();
        StartDiscovery();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label { Text = $"当前战场：{setup.MapName}　容量：{setup.Slots.Count} 人　端口：UDP 1232/1234、TCP 1233", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 3, 3, 10) });

        var discoveryBox = new GroupBox { Text = "1  创建或发现房间", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var discoveryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        var createRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        Button create = new() { Text = "创建房间", AutoSize = true };
        create.Click += (_, _) => CreateRoom();
        createRow.Controls.AddRange([new Label { Text = "房间名", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, roomName, create,
            new Label { Text = "房主 IP", AutoSize = true, Padding = new Padding(18, 6, 0, 0) }, hostAddress]);
        discoveryLayout.Controls.Add(createRow, 0, 0);
        discoveryLayout.Controls.Add(rooms, 0, 1);
        discoveryBox.Controls.Add(discoveryLayout);
        root.Controls.Add(discoveryBox);

        var roomBox = new GroupBox { Text = "2  房间玩家", Dock = DockStyle.Fill, Padding = new Padding(10) };
        roomBox.Controls.Add(players);
        root.Controls.Add(roomBox);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        Button join = new() { Text = "加入选中房间", AutoSize = true };
        join.Click += async (_, _) => await JoinSelectedAsync();
        Button joinIp = new() { Text = "按 IP 加入", AutoSize = true };
        joinIp.Click += async (_, _) => await JoinAsync(hostAddress.Text.Trim());
        ready.CheckedChanged += async (_, _) => await SetReadyAsync();
        start.Click += async (_, _) => await StartGameAsync();
        actions.Controls.AddRange([join, joinIp, ready, start]);
        root.Controls.Add(actions);
        status.Padding = new Padding(3, 8, 0, 0);
        root.Controls.Add(status);
        Controls.Add(root);
    }

    private void StartDiscovery()
    {
        try
        {
            discovery = new LanDiscoveryService();
            discovery.RoomFound += room => Ui(() =>
            {
                discovered[room.Address] = room;
                object? selected = rooms.SelectedItem;
                rooms.BeginUpdate();
                rooms.Items.Clear();
                foreach (DiscoveredLanRoom item in discovered.Values.OrderBy(r => r.Announcement.RoomName)) rooms.Items.Add(item);
                if (selected is DiscoveredLanRoom old) rooms.SelectedItem = rooms.Items.Cast<DiscoveredLanRoom>().FirstOrDefault(r => r.Address == old.Address);
                rooms.EndUpdate();
            });
        }
        catch (Exception ex) { status.Text = $"无法监听局域网广播：{ex.Message}；仍可按 IP 加入。"; }
    }

    private void CreateRoom()
    {
        if (host is not null || client is not null) { MessageBox.Show("已经在一个房间中。", "局域网"); return; }
        try
        {
            host = new LanLobbyHost(string.IsNullOrWhiteSpace(roomName.Text) ? "红警局域网房间" : roomName.Text.Trim(), playerName, setup);
            host.StateChanged += state => Ui(() => ApplyState(state, host.HostId));
            host.Launching += package => Ui(() => Launch(package, host.HostId, "127.0.0.1"));
            host.Error += error => Ui(() => status.Text = error);
            host.Start();
            ready.Enabled = true;
            start.Enabled = false;
            status.Text = "房间已创建，等待其他玩家加入。首次使用时请允许 Windows 防火墙访问专用网络。";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "创建房间失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task JoinSelectedAsync()
    {
        if (rooms.SelectedItem is not DiscoveredLanRoom selected) { MessageBox.Show("请先选择一个房间。", "局域网"); return; }
        await JoinAsync(selected.Address);
    }

    private async Task JoinAsync(string address)
    {
        if (host is not null || client is not null) { MessageBox.Show("已经在一个房间中。", "局域网"); return; }
        if (string.IsNullOrWhiteSpace(address)) { MessageBox.Show("请输入房主 IP。", "局域网"); return; }
        try
        {
            client = new LanLobbyClient();
            client.StateChanged += state => Ui(() => ApplyState(state, client.PlayerId));
            client.LaunchReceived += package => Ui(() => Launch(package, client.PlayerId, client.HostAddress));
            client.Error += error => Ui(() => { status.Text = error; ready.Enabled = false; });
            await client.ConnectAsync(address, playerName);
            ready.Enabled = true;
            status.Text = $"已连接房主 {address}，请确认玩家列表后准备。";
        }
        catch (Exception ex)
        {
            client?.Dispose();
            client = null;
            MessageBox.Show(ex.Message, "加入房间失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SetReadyAsync()
    {
        if (!ready.Enabled) return;
        try
        {
            if (host is not null) host.SetHostReady(ready.Checked);
            else if (client is not null) await client.SetReadyAsync(ready.Checked);
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private void ApplyState(LanRoomState state, Guid localId)
    {
        players.BeginUpdate();
        players.Items.Clear();
        foreach (LanPlayer player in state.Players)
        {
            var item = new ListViewItem(player.Name);
            item.SubItems.Add(player.Address);
            item.SubItems.Add(player.IsHost ? "房主" : "玩家");
            item.SubItems.Add(player.Ready ? "已准备" : "未准备");
            if (player.Id == localId) item.Font = new Font(players.Font, FontStyle.Bold);
            players.Items.Add(item);
        }
        players.EndUpdate();
        if (host is not null) start.Enabled = host.CanLaunch(out _);
        status.Text = $"房间：{state.RoomName}　地图：{state.MapName}　玩家：{state.Players.Count}/{state.Capacity}";
    }

    private async Task StartGameAsync()
    {
        if (host is null) return;
        try { await host.LaunchAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法开始", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void Launch(LanLaunchPackage package, Guid localId, string actualHostAddress)
    {
        if (launching) return;
        launching = true;
        try
        {
            if (localId == Guid.Empty) throw new InvalidOperationException("尚未收到本机玩家编号。");
            string localComponentHash = LanCompatibility.ComputeComponentHash(runtimePath);
            if (!string.Equals(localComponentHash, package.Setup.ComponentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("本机的游戏 EXE、Ares、Phobos、CnCNet Spawner 或 ra2mode.mix 与房主不一致，已阻止启动以避免联机不同步。");
            IniFileEditor.ConfigureCncDdraw(runtimePath, package.Setup.MaxGameTicks);
            SpawnWriter.WriteLan(runtimePath, package, localId, actualHostAddress);
            string syringe = Path.Combine(runtimePath, "Syringe.exe");
            if (!File.Exists(syringe)) throw new FileNotFoundException("运行目录缺少 Syringe.exe。", syringe);
            Process.Start(new ProcessStartInfo
            {
                FileName = syringe,
                Arguments = "-i=Ares.dll -i=CnCNet-Spawner.dll -i=Phobos.dll gamemd-spawn.exe --args=\"-SPAWN -LOG -CD -Include -Inheritance -RA2ModeSaveID=0x8d113b94\"",
                WorkingDirectory = runtimePath,
                UseShellExecute = true
            });
            status.Text = "联机配置已同步，游戏正在启动。";
        }
        catch (Exception ex)
        {
            launching = false;
            MessageBox.Show(ex.Message, "联机启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private void DisposeNetworking()
    {
        discovery?.Dispose();
        client?.Dispose();
        host?.Dispose();
    }
}
