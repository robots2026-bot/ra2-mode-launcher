namespace Ra2ModeLauncher;

internal sealed class LanRoomBrowserForm : Form
{
    private readonly ListBox rooms = new() { Dock = DockStyle.Fill };
    private readonly TextBox address = new() { Width = 180, PlaceholderText = "房主局域网 IP" };
    private readonly Dictionary<string, DiscoveredLanRoom> found = new(StringComparer.OrdinalIgnoreCase);
    private readonly LanDiscoveryService discovery;
    public string SelectedAddress { get; private set; } = "";

    public LanRoomBrowserForm()
    {
        Text = "附近的局域网房间";
        Font = new Font("Microsoft YaHei UI", 9f);
        Width = 660;
        Height = 390;
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = $"自动发现端口：UDP {LanLobbyHost.DiscoveryPort}　房间连接端口：TCP {LanLobbyHost.LobbyPort}", AutoSize = true, Margin = new Padding(3, 3, 3, 9) });
        root.Controls.Add(rooms);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        Button join = new() { Text = "加入选中房间", AutoSize = true }; join.Click += (_, _) => SelectRoom();
        Button joinIp = new() { Text = "按 IP 加入", AutoSize = true }; joinIp.Click += (_, _) => SelectAddress();
        actions.Controls.AddRange([join, new Label { Text = "房主 IP", AutoSize = true, Padding = new Padding(12, 6, 0, 0) }, address, joinIp]);
        root.Controls.Add(actions);
        Controls.Add(root);
        discovery = new LanDiscoveryService();
        discovery.RoomFound += room => BeginInvoke(() => AddRoom(room));
        FormClosed += (_, _) => discovery.Dispose();
        rooms.DoubleClick += (_, _) => SelectRoom();
    }

    private void AddRoom(DiscoveredLanRoom room)
    {
        found[room.Address] = room;
        rooms.BeginUpdate();
        rooms.Items.Clear();
        foreach (DiscoveredLanRoom value in found.Values.OrderBy(item => item.Announcement.RoomName)) rooms.Items.Add(value);
        rooms.EndUpdate();
    }

    private void SelectRoom()
    {
        if (rooms.SelectedItem is not DiscoveredLanRoom room) return;
        SelectedAddress = room.Address;
        DialogResult = DialogResult.OK;
    }

    private void SelectAddress()
    {
        if (string.IsNullOrWhiteSpace(address.Text)) return;
        SelectedAddress = address.Text.Trim();
        DialogResult = DialogResult.OK;
    }
}
