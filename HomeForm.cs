using System.Diagnostics;

namespace Ra2ModeLauncher;

internal sealed class HomeForm : Form
{
    private readonly LauncherConfig config = LauncherConfig.Load();
    private readonly TextBox playerName = new() { Dock = DockStyle.Fill };
    private readonly TextBox roomName = new() { Dock = DockStyle.Fill };
    private readonly ComboBox savedGames = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };

    public HomeForm()
    {
        Text = "红色警戒 2 / 尤里的复仇启动器";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 247, 249);
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = 720;
        Height = 520;
        MinimumSize = new Size(660, 480);
        StartPosition = FormStartPosition.CenterScreen;
        playerName.Text = config.PlayerName;
        roomName.Text = $"{config.PlayerName} 的房间";
        BuildLayout();
        ReloadSaves();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 24), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 18) };
        heading.Controls.Add(new Label { Text = "选择进入方式", AutoSize = true, Font = new Font(Font.FontFamily, 16f, FontStyle.Bold) });
        heading.Controls.Add(new Label { Text = "创建房间后设置地图与规则，或者加入同一局域网中的房间。", AutoSize = true, ForeColor = Color.DimGray });
        root.Controls.Add(heading);

        var identity = new GroupBox { Text = "玩家", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) };
        var identityGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        identityGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); identityGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identityGrid.Controls.Add(new Label { Text = "玩家名称", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 12, 0) }, 0, 0);
        identityGrid.Controls.Add(playerName, 1, 0);
        identity.Controls.Add(identityGrid);
        root.Controls.Add(identity);

        var roomBox = new GroupBox { Text = "房间", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) };
        var roomGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        roomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); roomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); roomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        roomGrid.Controls.Add(new Label { Text = "房间名称", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 12, 0) }, 0, 0);
        roomGrid.Controls.Add(roomName, 1, 0);
        Button create = new() { Text = "创建房间", AutoSize = true, Height = 36, Padding = new Padding(16, 0, 16, 0), BackColor = Color.FromArgb(164, 38, 44), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        create.FlatAppearance.BorderSize = 0;
        create.Click += (_, _) => OpenCreatedRoom();
        roomGrid.Controls.Add(create, 2, 0);
        var network = new Label { Text = $"本机地址：{DisplayAddress()}　发现 UDP {LanLobbyHost.DiscoveryPort}　房间 TCP {LanLobbyHost.LobbyPort}　游戏 UDP {LanLobbyHost.GamePort}", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 10, 8, 0) };
        roomGrid.Controls.Add(network, 0, 1); roomGrid.SetColumnSpan(network, 2);
        Button join = new() { Text = "加入局域网房间…", AutoSize = true, Height = 34 };
        join.Click += (_, _) => OpenJoinedRoom();
        roomGrid.Controls.Add(join, 2, 1);
        roomBox.Controls.Add(roomGrid);
        root.Controls.Add(roomBox);

        var saveBox = new GroupBox { Text = "本地存档", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14) };
        var saveGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); saveGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button refresh = new() { Text = "刷新", AutoSize = true }; refresh.Click += (_, _) => ReloadSaves();
        Button load = new() { Text = "加载存档", AutoSize = true }; load.Click += (_, _) => LoadSelectedSave();
        saveGrid.Controls.Add(savedGames, 0, 0); saveGrid.Controls.Add(refresh, 1, 0); saveGrid.Controls.Add(load, 2, 0);
        saveBox.Controls.Add(saveGrid);
        root.Controls.Add(saveBox);
        status.Padding = new Padding(0, 12, 0, 0);
        root.Controls.Add(status);
        Controls.Add(root);
    }

    private string DisplayAddress()
    {
        string address = LanNetworkAddress.GetPreferredIPv4();
        return string.IsNullOrWhiteSpace(address) ? "未检测到局域网 IPv4" : address;
    }

    private bool SaveIdentity()
    {
        string name = playerName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("请输入玩家名称。", "无法继续", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        config.PlayerName = name;
        config.Save();
        return true;
    }

    private void OpenCreatedRoom()
    {
        if (!SaveIdentity()) return;
        string name = roomName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("请输入房间名称。", "无法创建", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        OpenRoom(RoomEntry.Create(config.PlayerName, name));
    }

    private void OpenJoinedRoom()
    {
        if (!SaveIdentity()) return;
        using var browser = new LanRoomBrowserForm();
        if (browser.ShowDialog(this) == DialogResult.OK) OpenRoom(RoomEntry.Join(config.PlayerName, browser.SelectedAddress));
    }

    private void OpenRoom(RoomEntry entry)
    {
        Hide();
        try { using var room = new MainForm(entry); room.ShowDialog(); }
        finally { Show(); Activate(); ReloadSaves(); }
    }

    private void ReloadSaves()
    {
        string directory = Path.Combine(config.RuntimePath, "Saved Games");
        List<SaveInfo> saves = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.SAV").Select(path => new SaveInfo(path, SaveMetadataReader.GetDisplayName(path), File.GetLastWriteTime(path), SaveMetadataReader.UsesAresExtensions(path))).OrderByDescending(save => save.LastWriteTime).ToList()
            : [];
        savedGames.DataSource = saves;
        status.Text = saves.Count == 0 ? "没有找到本地存档。" : $"找到 {saves.Count} 个本地存档。";
    }

    private void LoadSelectedSave()
    {
        try
        {
            if (savedGames.SelectedItem is not SaveInfo save) throw new InvalidOperationException("没有可加载的 .SAV 存档。");
            if (!File.Exists(Path.Combine(config.RuntimePath, "Syringe.exe"))) throw new FileNotFoundException("当前游戏目录缺少 Syringe.exe。");
            IniFileEditor.ConfigureCncDdraw(config.RuntimePath, config.MaxGameTicks);
            SpawnWriter.WriteLoadSave(config.RuntimePath, save);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(config.RuntimePath, "Syringe.exe"),
                Arguments = save.UsesAresExtensions
                    ? "-i=Ares.dll -i=CnCNet-Spawner.dll -i=Phobos.dll gamemd-spawn.exe --args=\"-SPAWN -LOG -CD -Include -Inheritance -RA2ModeSaveID=0x8d113b94\""
                    : "-i=CnCNet-Spawner.dll gamemd-spawn.exe --args=\"-SPAWN -LOG -CD -Include -Inheritance -RA2ModeSaveID=0x8d113b94\"",
                WorkingDirectory = config.RuntimePath,
                UseShellExecute = true
            });
            status.Text = $"正在加载 {save.DisplayName}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法加载存档", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
