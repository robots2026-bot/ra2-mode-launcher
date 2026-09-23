using System.Diagnostics;

namespace Ra2ModeLauncher;

internal sealed class HomeForm : Form
{
    private sealed record RoomSeen(DiscoveredLanRoom Room, DateTime SeenAt);

    private readonly LauncherConfig config = LauncherConfig.Load();
    private readonly TextBox playerName = new() { Width = 150 };
    private readonly TextBox roomName = new() { Width = 190 };
    private readonly TextBox directAddress = new() { Width = 145, PlaceholderText = "房主局域网 IP" };
    private readonly ComboBox savedGames = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListView rooms = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, GridLines = true };
    private readonly MapPreviewControl mapPreview = new() { Dock = DockStyle.Fill };
    private readonly Label roomDetails = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(8), ForeColor = Color.FromArgb(55, 58, 64) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button joinSelected = new() { Text = "加入选中房间", AutoSize = true, Height = 36, Enabled = false };
    private readonly Dictionary<string, RoomSeen> discovered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> localMapHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer expiryTimer = new() { Interval = 1000 };
    private readonly List<MapInfo> localMaps;
    private readonly LanDiscoveryService discovery;
    private string? localComponentHash;

    public HomeForm()
    {
        Text = "红色警戒 2 / 尤里的复仇——局域网大厅";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 247, 249);
        AutoScaleMode = AutoScaleMode.Dpi;
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Width = Math.Clamp((int)(area.Width * 0.72), 980, 1220);
        Height = Math.Clamp((int)(area.Height * 0.76), 680, 850);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        playerName.Text = config.PlayerName;
        roomName.Text = $"{config.PlayerName} 的房间";
        localMaps = MapScanner.Scan(config.RuntimePath);
        BuildLayout();
        ReloadSaves();
        discovery = new LanDiscoveryService();
        discovery.RoomFound += room => Ui(() => AddOrUpdateRoom(room));
        expiryTimer.Tick += (_, _) => ExpireRooms();
        expiryTimer.Start();
        FormClosed += (_, _) => { expiryTimer.Dispose(); discovery.Dispose(); };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        title.Controls.Add(new Label { Text = "局域网对局", AutoSize = true, Font = new Font(Font.FontFamily, 15f, FontStyle.Bold) });
        title.Controls.Add(new Label { Text = "自动发现同一局域网中的房间；选中后可查看地图与规则。", AutoSize = true, ForeColor = Color.DimGray });
        var identity = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
        identity.Controls.Add(new Label { Text = "玩家名称", AutoSize = true, Padding = new Padding(0, 7, 4, 0) }); identity.Controls.Add(playerName);
        heading.Controls.Add(title, 0, 0); heading.Controls.Add(identity, 1, 0); root.Controls.Add(heading, 0, 0);

        var createBar = new GroupBox { Text = "创建或直接加入", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 9) };
        var createActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        Button create = new() { Text = "创建房间", AutoSize = true, Height = 34, BackColor = Color.FromArgb(164, 38, 44), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        create.FlatAppearance.BorderSize = 0; create.Click += (_, _) => OpenCreatedRoom();
        Button joinIp = new() { Text = "按 IP 加入", AutoSize = true, Height = 32 }; joinIp.Click += (_, _) => OpenDirectRoom();
        createActions.Controls.AddRange([new Label { Text = "房间名称", AutoSize = true, Padding = new Padding(0, 7, 2, 0) }, roomName, create, new Label { Text = "房主 IP", AutoSize = true, Padding = new Padding(16, 7, 2, 0) }, directAddress, joinIp, new Label { Text = $"本机 {DisplayAddress()}  ·  UDP {LanLobbyHost.DiscoveryPort} / TCP {LanLobbyHost.LobbyPort}", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(16, 7, 0, 0) }]);
        createBar.Controls.Add(createActions); root.Controls.Add(createBar, 0, 1);

        var lobby = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 580, Margin = new Padding(0, 0, 0, 9) };
        var listBox = new GroupBox { Text = "可用对局（自动刷新）", Dock = DockStyle.Fill, Padding = new Padding(9) };
        rooms.Columns.Add("房间", 155); rooms.Columns.Add("房主", 90); rooms.Columns.Add("模式", 78); rooms.Columns.Add("地图", 155); rooms.Columns.Add("人数", 60); rooms.Columns.Add("状态", 70);
        rooms.SelectedIndexChanged += (_, _) => ShowSelectedRoom(); rooms.DoubleClick += (_, _) => JoinSelectedRoom();
        listBox.Controls.Add(rooms); lobby.Panel1.Controls.Add(listBox);
        var infoBox = new GroupBox { Text = "选中对局信息", Dock = DockStyle.Fill, Padding = new Padding(9) };
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        info.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); info.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var previewBorder = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(3) }; previewBorder.Controls.Add(mapPreview);
        info.Controls.Add(previewBorder, 0, 0); info.Controls.Add(roomDetails, 0, 1);
        joinSelected.Click += (_, _) => JoinSelectedRoom();
        var joinRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft }; joinRow.Controls.Add(joinSelected); info.Controls.Add(joinRow, 0, 2);
        infoBox.Controls.Add(info); lobby.Panel2.Controls.Add(infoBox); root.Controls.Add(lobby, 0, 2);

        var saveBox = new GroupBox { Text = "本地存档", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(9) };
        var saveGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button refresh = new() { Text = "刷新", AutoSize = true }; refresh.Click += (_, _) => ReloadSaves();
        Button load = new() { Text = "加载存档", AutoSize = true }; load.Click += (_, _) => LoadSelectedSave();
        saveGrid.Controls.Add(savedGames, 0, 0); saveGrid.Controls.Add(refresh, 1, 0); saveGrid.Controls.Add(load, 2, 0); saveBox.Controls.Add(saveGrid); root.Controls.Add(saveBox, 0, 3);
        status.Text = "正在搜索局域网房间…"; status.Padding = new Padding(0, 6, 0, 0); root.Controls.Add(status, 0, 4); Controls.Add(root);
    }

    private string DisplayAddress() { string address = LanNetworkAddress.GetPreferredIPv4(); return string.IsNullOrWhiteSpace(address) ? "未检测到 IPv4" : address; }

    private void AddOrUpdateRoom(DiscoveredLanRoom room)
    {
        string key = $"{room.Address}:{LanLobbyHost.LobbyPort}";
        string? selectedKey = SelectedRoomKey();
        discovered[key] = new RoomSeen(room, DateTime.UtcNow);
        RebuildRoomList(selectedKey ?? key);
    }

    private void ExpireRooms()
    {
        string? selectedKey = SelectedRoomKey();
        string[] expired = discovered.Where(pair => DateTime.UtcNow - pair.Value.SeenAt > TimeSpan.FromSeconds(3.5)).Select(pair => pair.Key).ToArray();
        foreach (string key in expired) discovered.Remove(key);
        if (expired.Length > 0) RebuildRoomList(selectedKey);
        status.Text = discovered.Count == 0 ? "未发现等待中的局域网房间。" : $"已发现 {discovered.Count} 个局域网房间。";
    }

    private string? SelectedRoomKey() => rooms.SelectedItems.Count == 0 ? null : rooms.SelectedItems[0].Name;

    private void RebuildRoomList(string? selectKey)
    {
        rooms.BeginUpdate(); rooms.Items.Clear();
        foreach ((string key, RoomSeen seen) in discovered.OrderBy(pair => pair.Value.Room.Announcement.RoomName))
        {
            LanRoomAnnouncement value = seen.Room.Announcement;
            var item = new ListViewItem(value.RoomName) { Name = key, Tag = seen.Room };
            item.SubItems.Add(value.HostName); item.SubItems.Add(value.Ra2Mode ? "红警2" : "尤里"); item.SubItems.Add(value.MapName); item.SubItems.Add($"{value.Players}/{value.Capacity}"); item.SubItems.Add(value.OpenSlots > 0 ? value.Status : "已满");
            rooms.Items.Add(item); if (key == selectKey) item.Selected = true;
        }
        rooms.EndUpdate();
        if (rooms.Items.Count > 0 && rooms.SelectedItems.Count == 0) rooms.Items[0].Selected = true;
        ShowSelectedRoom();
    }

    private DiscoveredLanRoom? SelectedRoom() => rooms.SelectedItems.Count == 0 ? null : rooms.SelectedItems[0].Tag as DiscoveredLanRoom;

    private void ShowSelectedRoom()
    {
        DiscoveredLanRoom? selected = SelectedRoom();
        joinSelected.Enabled = selected is not null && selected.Announcement.OpenSlots > 0;
        if (selected is null) { mapPreview.Map = null; roomDetails.Text = "选中左侧房间后，这里显示地图和规则。"; return; }
        LanRoomAnnouncement room = selected.Announcement;
        MapInfo? localMap = localMaps.FirstOrDefault(map => string.Equals(Path.GetFileName(map.Path), room.MapFileName, StringComparison.OrdinalIgnoreCase)) ?? localMaps.FirstOrDefault(map => string.Equals(map.Name, room.MapName, StringComparison.OrdinalIgnoreCase));
        mapPreview.Map = localMap;
        string speed = GameData.GameSpeeds.FirstOrDefault(item => item.GameSpeed == room.GameSpeed && item.MaxGameTicks == room.MaxGameTicks)?.Name ?? $"{room.GameSpeed}/{room.MaxGameTicks}";
        roomDetails.Text = $"房主：{room.HostName}\r\n地址：{selected.Address}:{LanLobbyHost.LobbyPort}\r\n模式：{(room.Ra2Mode ? "红警2经典" : "尤里复仇")}\r\n人数：{room.Players}/{room.Capacity}，开放 {room.OpenSlots}\r\n资金：{room.Credits}，速度：{speed}\r\n规则：{Flag(room.Crates, "箱子")} {Flag(room.SuperWeapons, "超武")} {Flag(room.ShortGame, "短局")} {Flag(room.RevealAllMap, "全图")}\r\n地图：{GetMapCompatibility(room, localMap)}\r\n组件：{GetCompatibility(room)}";
    }

    private string GetMapCompatibility(LanRoomAnnouncement room, MapInfo? localMap)
    {
        if (localMap is null) return "本机没有同名地图，加入后由房主同步";
        try
        {
            if (!localMapHashes.TryGetValue(localMap.Path, out string? hash)) localMapHashes[localMap.Path] = hash = LanGameSetup.Hash(File.ReadAllBytes(localMap.Path));
            return string.Equals(hash, room.MapHash, StringComparison.OrdinalIgnoreCase) ? "本机地图一致" : "同名地图内容不同，加入后由房主同步";
        }
        catch (Exception ex) { return $"无法校验：{ex.Message}"; }
    }

    private string GetCompatibility(LanRoomAnnouncement room)
    {
        try { localComponentHash ??= LanCompatibility.ComputeComponentHash(config.RuntimePath); return string.Equals(localComponentHash, room.ComponentHash, StringComparison.OrdinalIgnoreCase) ? "关键组件一致" : "组件不一致，不建议加入"; }
        catch (Exception ex) { return $"无法校验：{ex.Message}"; }
    }

    private static string Flag(bool enabled, string name) => enabled ? name : $"无{name}";

    private bool SaveIdentity()
    {
        string name = playerName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("请输入玩家名称。", "无法继续", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        config.PlayerName = name; config.Save(); return true;
    }

    private void OpenCreatedRoom()
    {
        if (!SaveIdentity()) return;
        string name = roomName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("请输入房间名称。", "无法创建", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        OpenRoom(RoomEntry.Create(config.PlayerName, name));
    }

    private void JoinSelectedRoom() { DiscoveredLanRoom? selected = SelectedRoom(); if (selected is null || !SaveIdentity()) return; OpenRoom(RoomEntry.Join(config.PlayerName, selected.Address)); }
    private void OpenDirectRoom() { if (!SaveIdentity() || string.IsNullOrWhiteSpace(directAddress.Text)) return; OpenRoom(RoomEntry.Join(config.PlayerName, directAddress.Text.Trim())); }

    private void OpenRoom(RoomEntry entry)
    {
        Hide();
        try { using var room = new MainForm(entry); room.ShowDialog(); }
        finally { Show(); Activate(); ReloadSaves(); }
    }

    private void ReloadSaves()
    {
        string directory = Path.Combine(config.RuntimePath, "Saved Games");
        List<SaveInfo> saves = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.SAV").Select(path => new SaveInfo(path, SaveMetadataReader.GetDisplayName(path), File.GetLastWriteTime(path), SaveMetadataReader.UsesAresExtensions(path))).OrderByDescending(save => save.LastWriteTime).ToList() : [];
        savedGames.DataSource = saves;
    }

    private void LoadSelectedSave()
    {
        try
        {
            if (savedGames.SelectedItem is not SaveInfo save) throw new InvalidOperationException("没有可加载的 .SAV 存档。");
            if (!File.Exists(Path.Combine(config.RuntimePath, "Syringe.exe"))) throw new FileNotFoundException("当前游戏目录缺少 Syringe.exe。");
            IniFileEditor.ConfigureCncDdraw(config.RuntimePath, config.MaxGameTicks); SpawnWriter.WriteLoadSave(config.RuntimePath, save);
            Process.Start(new ProcessStartInfo { FileName = Path.Combine(config.RuntimePath, "Syringe.exe"), Arguments = save.UsesAresExtensions ? "-i=Ares.dll -i=CnCNet-Spawner.dll -i=Phobos.dll gamemd-spawn.exe --args=\"-SPAWN -LOG -CD -Include -Inheritance -RA2ModeSaveID=0x8d113b94\"" : "-i=CnCNet-Spawner.dll gamemd-spawn.exe --args=\"-SPAWN -LOG -CD -Include -Inheritance -RA2ModeSaveID=0x8d113b94\"", WorkingDirectory = config.RuntimePath, UseShellExecute = true });
            status.Text = $"正在加载 {save.DisplayName}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法加载存档", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Ui(Action action) { if (IsDisposed || !IsHandleCreated) return; if (InvokeRequired) BeginInvoke(action); else action(); }
}
