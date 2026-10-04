using System.Diagnostics;

namespace Ra2ModeLauncher;

internal sealed class HomeForm : Form
{
    private const int MapColumnWidth = 190;
    private sealed record RoomSeen(DiscoveredLanRoom Room, DateTime SeenAt);

    private readonly LauncherConfig config = LauncherConfig.Load();
    private readonly TextBox playerName = new() { Width = 150 };
    private readonly TextBox roomName = new() { Width = 190 };
    private readonly ComboBox savedGames = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListView rooms = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, GridLines = true };
    public event Action<MapInfo?>? PreviewRequested;
    private MapInfo? selectedPreview;
    public void RefreshPreview() => PreviewRequested?.Invoke(selectedPreview);
    private readonly Label roomDetails = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(8), ForeColor = Color.FromArgb(55, 58, 64) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button joinSelected = new() { Text = "加入选中房间", AutoSize = true, Height = 36, Enabled = false };
    private readonly Dictionary<string, RoomSeen> discovered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> localMapHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer expiryTimer = new() { Interval = 1000 };
    private readonly List<MapInfo> localMaps;
    private readonly LanDiscoveryService discovery;
    private string? localComponentHash;
    private bool discoveryDisposed;
    private bool roomActive;
    public void SetRoomActive(bool active)
    {
        roomActive = active;
        savedGames.Enabled = !active;
    }
    public event Action<RoomEntry>? OpenRoomRequested;

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
        string defaultPlayerName = string.IsNullOrWhiteSpace(config.PlayerName) || config.PlayerName == "Player" ? Environment.MachineName : config.PlayerName;
        playerName.Text = defaultPlayerName;
        roomName.Text = string.IsNullOrWhiteSpace(config.RoomName) || config.RoomName == "Player 的房间" ? $"{defaultPlayerName} 的房间" : config.RoomName;
        localMaps = MapScanner.Scan(config.RuntimePath);
        BuildLayout();
        ReloadSaves();
        discovery = new LanDiscoveryService();
        discovery.RoomFound += room => Ui(() => AddOrUpdateRoom(room));
        expiryTimer.Tick += (_, _) => ExpireRooms();
        expiryTimer.Start();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 9, 10), ColumnCount = 1, RowCount = 6 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 7) };
        title.Controls.Add(new Label { Text = "局域网大厅", AutoSize = true, Font = new Font(Font.FontFamily, 13f, FontStyle.Bold) });
        title.Controls.Add(new Label { Text = "房间列表与右侧当前房间同时显示", AutoSize = true, ForeColor = Color.DimGray });
        root.Controls.Add(title, 0, 0);

        var createBar = new GroupBox { Text = "创建房间", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 7) };
        var createGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        createGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); createGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); createGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button create = new() { Text = "创建", AutoSize = true, Height = 32, BackColor = Color.FromArgb(164, 38, 44), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        create.FlatAppearance.BorderSize = 0; create.Click += (_, _) => OpenCreatedRoom();
        createGrid.Controls.Add(new Label { Text = "名称", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) }, 0, 0); createGrid.Controls.Add(roomName, 1, 0); createGrid.Controls.Add(create, 2, 0);
        Button manualJoin = new() { Text = "手动连接…", AutoSize = true, Height = 28 }; manualJoin.Click += (_, _) => OpenDirectRoom();
        createGrid.Controls.Add(new Label { Text = $"{DisplayAddress()}  ·  UDP {LanLobbyHost.DiscoveryPort} / TCP {LanLobbyHost.LobbyPort}", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 7, 4, 0) }, 0, 1); createGrid.SetColumnSpan(createGrid.GetControlFromPosition(0, 1)!, 2); createGrid.Controls.Add(manualJoin, 2, 1);
        createBar.Controls.Add(createGrid); root.Controls.Add(createBar, 0, 1);

        var listBox = new GroupBox { Text = "可用对局（自动刷新）", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 0, 0, 7) };
        rooms.Columns.Add("房间", 100); rooms.Columns.Add("地图", MapColumnWidth); rooms.Columns.Add("人数", 45); rooms.Columns.Add("状态", 52);
        rooms.ColumnWidthChanging += (_, e) => { if (e.ColumnIndex == 1) { e.NewWidth = MapColumnWidth; e.Cancel = true; } };
        rooms.SelectedIndexChanged += (_, _) => ShowSelectedRoom(); rooms.DoubleClick += (_, _) => JoinSelectedRoom();
        listBox.Controls.Add(rooms); root.Controls.Add(listBox, 0, 2);

        var infoBox = new GroupBox { Text = "选中对局", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 0, 0, 7) };
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0)); info.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        info.Controls.Add(roomDetails, 0, 0); info.SetColumnSpan(roomDetails, 2);
        joinSelected.Click += (_, _) => JoinSelectedRoom();
        var joinRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        joinRow.Controls.Add(joinSelected); joinRow.Controls.Add(playerName); joinRow.Controls.Add(new Label { Text = "玩家名称", AutoSize = true, Padding = new Padding(0, 7, 3, 0) }); info.Controls.Add(joinRow, 0, 1); info.SetColumnSpan(joinRow, 2);
        infoBox.Controls.Add(info); root.Controls.Add(infoBox, 0, 3);

        var saveBox = new GroupBox { Text = "本地存档", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(7) };
        var saveGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button refresh = new() { Text = "刷新", AutoSize = true }; refresh.Click += (_, _) => ReloadSaves();
        Button load = new() { Text = "加载存档", AutoSize = true }; load.Click += (_, _) => LoadSelectedSave();
        saveGrid.Controls.Add(savedGames, 0, 0); saveGrid.Controls.Add(refresh, 1, 0); saveGrid.Controls.Add(load, 2, 0); saveBox.Controls.Add(saveGrid); root.Controls.Add(saveBox, 0, 4);
        status.Text = "正在搜索局域网房间…"; status.Padding = new Padding(0, 4, 0, 0); root.Controls.Add(status, 0, 5); Controls.Add(root);
    }

    private string DisplayAddress() { string address = LanNetworkAddress.GetPreferredIPv4(); return string.IsNullOrWhiteSpace(address) ? "未检测到 IPv4" : address; }

    private void AddOrUpdateRoom(DiscoveredLanRoom room)
    {
        string key = $"{room.Address}:{LanLobbyHost.LobbyPort}";
        string? selectedKey = SelectedRoomKey();
        bool unchanged = discovered.TryGetValue(key, out RoomSeen? previous) && previous.Room == room;
        discovered[key] = new RoomSeen(room, DateTime.UtcNow);
        if (unchanged) return;
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
            item.SubItems.Add(value.MapName); item.SubItems.Add($"{value.Players}/{value.Capacity}"); item.SubItems.Add(value.OpenSlots > 0 ? value.Status : "已满");
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
        if (selected is null) { selectedPreview = null; RefreshPreview(); roomDetails.Text = "选择房间后，右侧显示地图预览。"; return; }
        LanRoomAnnouncement room = selected.Announcement;
        MapInfo? localMap = localMaps.FirstOrDefault(map => string.Equals(Path.GetFileName(map.Path), room.MapFileName, StringComparison.OrdinalIgnoreCase)) ?? localMaps.FirstOrDefault(map => string.Equals(map.Name, room.MapName, StringComparison.OrdinalIgnoreCase));
        selectedPreview = GetMapCompatibility(room, localMap) == "本机地图一致" ? localMap : null;
        RefreshPreview();
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
        config.RoomName = name;
        config.Save();
        OpenRoom(RoomEntry.Create(config.PlayerName, name));
    }

    private void JoinSelectedRoom() { DiscoveredLanRoom? selected = SelectedRoom(); if (selected is null || !SaveIdentity()) return; OpenRoom(RoomEntry.Join(config.PlayerName, selected.Address)); }
    private void OpenDirectRoom()
    {
        if (!SaveIdentity()) return;
        using var dialog = new Form { Text = "手动连接", Font = Font, Width = 430, Height = 155, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var address = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "例如 192.168.1.20" };
        layout.Controls.Add(new Label { Text = "房主 IP", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 12, 0) }, 0, 0); layout.Controls.Add(address, 1, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button connect = new() { Text = "连接", AutoSize = true, DialogResult = DialogResult.OK }; Button cancel = new() { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([connect, cancel]); layout.Controls.Add(actions, 0, 1); layout.SetColumnSpan(actions, 2); dialog.Controls.Add(layout); dialog.AcceptButton = connect; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(address.Text)) OpenRoom(RoomEntry.Join(config.PlayerName, address.Text.Trim()));
    }

    private void OpenRoom(RoomEntry entry)
    {
        OpenRoomRequested?.Invoke(entry);
    }

    public void RefreshAfterRoom()
    {
        ReloadSaves();
        ExpireRooms();
    }

    private void ReloadSaves()
    {
        string directory = Path.Combine(config.RuntimePath, "Saved Games");
        List<SaveInfo> saves = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.SAV").Select(path => new SaveInfo(path, SaveMetadataReader.GetDisplayName(path), File.GetLastWriteTime(path), SaveMetadataReader.UsesAresExtensions(path))).OrderByDescending(save => save.LastWriteTime).ToList() : [];
        savedGames.DataSource = saves;
    }

    private void LoadSelectedSave()
    {
        if (roomActive) { MessageBox.Show("请先离开当前房间，再加载本地存档。"); return; }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !discoveryDisposed)
        {
            discoveryDisposed = true;
            expiryTimer.Dispose();
            discovery.Dispose();
        }
        base.Dispose(disposing);
    }
}
