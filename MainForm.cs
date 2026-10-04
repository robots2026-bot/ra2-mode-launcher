using System.ComponentModel;
using System.Diagnostics;

namespace Ra2ModeLauncher;

internal sealed class MainForm : Form
{
    private readonly RoomEntry entry;
    private readonly LauncherConfig config = LauncherConfig.Load();
    private readonly TextBox runtimePath = new() { Dock = DockStyle.Fill };
    private readonly ComboBox gameMode = Combo();
    private readonly ComboBox maps = Combo();
    private readonly ComboBox resolution = Combo();
    private readonly ComboBox gameSpeed = Combo();
    private readonly NumericUpDown credits = new() { Minimum = 5000, Maximum = 100000, Increment = 5000, Value = 10000, Width = 90 };
    private readonly CheckBox crates = new() { Text = "随机箱", Checked = true, AutoSize = true };
    private readonly CheckBox superWeapons = new() { Text = "超级武器", Checked = true, AutoSize = true };
    private readonly CheckBox shortGame = new() { Text = "摧毁全部建筑即失败", Checked = true, AutoSize = true };
    private readonly CheckBox revealAllMap = new() { Text = "开局全图（无黑幕）", Checked = true, AutoSize = true };
    private readonly ComboBox savedGames = Combo();
    private readonly MapPreviewControl mapPreview;
    private readonly BindingList<ParticipantRow> participants = [];
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkSlateBlue };
    private readonly Label roomStatus = new() { AutoSize = true, ForeColor = Color.DarkGreen };
    private readonly Button roomReady = new() { Text = "准备", AutoSize = true, Height = 36, Enabled = false };
    private readonly Button roomStart = new() { Text = "开始游戏", AutoSize = true, Height = 38, Padding = new Padding(16, 0, 16, 0), BackColor = Color.FromArgb(164, 38, 44), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly List<Guid?> rowPlayerIds = [];
    private LanLobbyHost? roomHost;
    private LanLobbyClient? roomClient;
    private bool applyingRoomState;
    private bool startingRoom;
    private bool localRoomReady;
    public event Action? ReturnHomeRequested;

    public MainForm(RoomEntry entry, MapPreviewControl sharedPreview)
    {
        mapPreview = sharedPreview;
        mapPreview.AllowStartSelection = true;
        participants.ListChanged += (_, _) => mapPreview.SetPlayers(participants.Where(row => row.SlotType is 0 or 1).Select(row => (row.Start, row.Color, row.Name)));
        this.entry = entry;
        StartupTrace.Mark("form constructor entered");
        Text = entry.Mode == RoomEntryMode.Create ? $"房间：{entry.RoomName}" : $"正在加入：{entry.HostAddress}";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 247, 249);
        AutoScaleMode = AutoScaleMode.Dpi;
        Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Width = Math.Clamp((int)(workArea.Width * 0.76), 1020, 1180);
        Height = Math.Clamp((int)(workArea.Height * 0.76), 700, 800);
        MinimumSize = new Size(920, 640);
        StartPosition = FormStartPosition.CenterScreen;
        FormClosed += (_, _) => DisposeRoomNetworking();

        runtimePath.Text = config.RuntimePath;
        gameMode.Items.AddRange(["红警2经典", "尤里复仇"]);
        gameMode.SelectedIndexChanged += (_, _) => { ApplyModeCountries(); PublishHostSetup(); };
        maps.SelectedIndexChanged += (_, _) =>
        {
            mapPreview.Map = maps.SelectedItem as MapInfo;
            if (maps.SelectedItem is MapInfo map) SyncParticipantSlots(map.StartingPoints);
            PublishHostSetup();
        };
        mapPreview.StartSelected += SelectPreviewStart;
        resolution.Items.AddRange(GameData.Resolutions);
        gameSpeed.Items.AddRange(GameData.GameSpeeds);
        gameSpeed.SelectedIndexChanged += (_, _) => PublishHostSetup();
        credits.ValueChanged += (_, _) => PublishHostSetup();
        crates.CheckedChanged += (_, _) => PublishHostSetup();
        superWeapons.CheckedChanged += (_, _) => PublishHostSetup();
        shortGame.CheckedChanged += (_, _) => PublishHostSetup();
        revealAllMap.CheckedChanged += (_, _) => PublishHostSetup();
        roomReady.Click += async (_, _) => await SetRoomReadyAsync(!localRoomReady);
        roomStart.Click += async (_, _) => await StartRoomGameAsync();
        StartupTrace.Mark("basic controls initialized");

        config.PlayerName = entry.PlayerName;
        participants.Add(new ParticipantRow { SlotType = 0, Name = entry.PlayerName, ReadyStatus = "房主", Country = GameData.Countries[0].Value, Color = GameData.Colors[0].Value, Team = 0, Difficulty = GameData.Difficulties[1].Value, Start = 1 });
        BuildGrid();
        StartupTrace.Mark("grid built");
        BuildLayout();
        ApplyVisualStyle();
        StartupTrace.Mark("layout built");
        gameMode.SelectedIndex = 0;
        resolution.SelectedItem = GameData.Resolutions.FirstOrDefault(item => item.Width == config.ResolutionWidth && item.Height == config.ResolutionHeight) ?? GameData.Resolutions[5];
        gameSpeed.SelectedItem = GameData.GameSpeeds.FirstOrDefault(item => item.GameSpeed == config.GameSpeed && item.MaxGameTicks == config.MaxGameTicks) ?? GameData.GameSpeeds[4];
        shortGame.Checked = config.ShortGame;
        ReloadMaps();
        StartupTrace.Mark("maps loaded");
        StartupTrace.Mark("form constructor finished");
        Shown += async (_, _) =>
        {
            if (entry.Mode == RoomEntryMode.Create) StartOwnRoom();
            else await JoinRoomAsync(entry.HostAddress);
        };
    }

    private static ComboBox Combo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && combo.Enabled && combo.Items.Count > 0)
                combo.DroppedDown = true;
        };
        return combo;
    }

    private void BuildGrid()
    {
        grid.DataSource = participants;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 237, 240);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(45, 48, 54);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        grid.ColumnHeadersHeight = 30;
        grid.RowTemplate.Height = 29;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 224, 239);
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex] is not DataGridViewComboBoxColumn) return;
            grid.CurrentCell = grid[e.ColumnIndex, e.RowIndex];
            grid.BeginEdit(true);
            if (grid.EditingControl is DataGridViewComboBoxEditingControl editor)
                editor.DroppedDown = true;
        };
        grid.DataError += (_, e) =>
        {
            StartupTrace.Mark($"grid data error row={e.RowIndex} column={e.ColumnIndex}: {e.Exception?.Message}");
            e.ThrowException = false;
        };
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            ParticipantRow participant = participants[e.RowIndex];
            string column = grid.Columns[e.ColumnIndex].Name;
            bool inactive = participant.SlotType is 2 or 3;
            if ((participant.SlotType == 0 && column == nameof(ParticipantRow.Difficulty)) || (inactive && column != nameof(ParticipantRow.SlotType) && column != nameof(ParticipantRow.ReadyStatus)))
            {
                e.Value = "—";
                e.FormattingApplied = true;
                if (e.CellStyle is not null) e.CellStyle.ForeColor = Color.Gray;
            }
        };
        grid.CellBeginEdit += (_, e) =>
        {
            ParticipantRow participant = participants[e.RowIndex];
            string column = grid.Columns[e.ColumnIndex].Name;
            if (column == nameof(ParticipantRow.ReadyStatus)) { e.Cancel = true; return; }
            Guid? rowPlayerId = e.RowIndex < rowPlayerIds.Count ? rowPlayerIds[e.RowIndex] : null;
            if (rowPlayerId.HasValue)
            {
                if (column == nameof(ParticipantRow.SlotType) || rowPlayerId.Value != LocalRoomPlayerId) { e.Cancel = true; return; }
            }
            else if (roomClient is not null) { e.Cancel = true; return; }
            if ((participant.SlotType == 0 && column == nameof(ParticipantRow.Difficulty)) || (participant.SlotType is 2 or 3 && column != nameof(ParticipantRow.SlotType))) e.Cancel = true;
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (applyingRoomState || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            ParticipantRow changed = participants[e.RowIndex];
            string column = grid.Columns[e.ColumnIndex].Name;
            Guid? rowPlayerId = e.RowIndex < rowPlayerIds.Count ? rowPlayerIds[e.RowIndex] : null;
            if (column == nameof(ParticipantRow.SlotType) && !rowPlayerId.HasValue)
            {
                if (changed.SlotType == 0) changed.SlotType = 2;
                changed.Name = changed.SlotType switch { 1 => $"电脑 {e.RowIndex}", 2 => "开放", _ => "关闭" };
                changed.ReadyStatus = changed.SlotType switch { 1 => "就绪", 2 => "等待加入", _ => "—" };
                if (changed.SlotType == 1)
                {
                    changed.Country = GameData.Countries[e.RowIndex % GameData.Countries.Length].Value;
                    changed.Color = GameData.Colors[e.RowIndex % GameData.Colors.Length].Value;
                    changed.Team = 1;
                    changed.Difficulty = 0;
                    changed.Start = e.RowIndex + 1;
                }
            }
            grid.Refresh();
            mapPreview.SetPlayers(participants.Where(row => row.SlotType is 0 or 1).Select(row => (row.Start, row.Color, row.Name)));
            if (rowPlayerId == LocalRoomPlayerId && rowPlayerId.HasValue) UpdateLocalRoomPlayer(changed);
            else PublishHostSetup();
        };
        grid.Columns.Add(ChoiceColumn(nameof(ParticipantRow.SlotType), "位置状态", GameData.SlotTypes, 105));
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(ParticipantRow.Name), DataPropertyName = nameof(ParticipantRow.Name), HeaderText = "名称", FillWeight = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(ParticipantRow.ReadyStatus), DataPropertyName = nameof(ParticipantRow.ReadyStatus), HeaderText = "准备状态", FillWeight = 90, ReadOnly = true });
        grid.Columns.Add(ChoiceColumn(nameof(ParticipantRow.Country), "国家", GameData.Countries, 125));
        grid.Columns.Add(ChoiceColumn(nameof(ParticipantRow.Color), "颜色", GameData.Colors, 90));
        grid.Columns.Add(ChoiceColumn(nameof(ParticipantRow.Team), "队伍", GameData.Teams, 100));
        grid.Columns.Add(ChoiceColumn(nameof(ParticipantRow.Difficulty), "难度", GameData.Difficulties, 90));
        grid.Columns.Add(ChoiceColumn(nameof(ParticipantRow.Start), "出生点", GameData.Starts, 85));
    }

    private static DataGridViewComboBoxColumn ChoiceColumn(string property, string title, Choice[] values, int width) => new()
    {
        Name = property, DataPropertyName = property, HeaderText = title, DataSource = values.ToArray(), DisplayMember = nameof(Choice.Name), ValueMember = nameof(Choice.Value), Width = width
    };

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 12), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleBlock = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        titleBlock.Controls.Add(new Label { Text = "红色警戒 2 / 尤里的复仇", AutoSize = true, Font = new Font(Font.FontFamily, 13f, FontStyle.Bold) });
        titleBlock.Controls.Add(new Label { Text = "房间内同时设置地图、位置和规则；一人为单机，多人为局域网", AutoSize = true, ForeColor = Color.DimGray });
        var advanced = new Panel { Dock = DockStyle.Fill, AutoSize = true, Visible = false, Padding = new Padding(0, 4, 0, 8) };
        var paths = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 1 };
        paths.Controls.Add(PathRow("当前游戏目录", runtimePath, () => { ReloadMaps(); ReloadSaves(); }));
        advanced.Controls.Add(paths);
        Button advancedButton = new() { Text = "游戏目录设置 ▾", AutoSize = true, Anchor = AnchorStyles.Right };
        advancedButton.Click += (_, _) => { advanced.Visible = !advanced.Visible; advancedButton.Text = advanced.Visible ? "收起目录设置 ▴" : "游戏目录设置 ▾"; };
        heading.Controls.Add(titleBlock, 0, 0); heading.Controls.Add(advancedButton, 1, 0);
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(advanced, 0, 1);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 6, 0, 8) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var previewBox = new GroupBox { Text = "地图预览 · 点击编号选择自己的出生点", Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(0, 0, 8, 0) };
        previewBox.Controls.Add(mapPreview); content.Controls.Add(previewBox, 0, 0);
        var settingsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        settingsScroll.Controls.Add(settings); content.Controls.Add(settingsScroll, 1, 0);

        var battlefieldBox = new GroupBox { Text = "地图与模式", Dock = DockStyle.Top, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 6) };
        var battlefield = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        battlefield.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); battlefield.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        battlefield.RowStyles.Add(new RowStyle(SizeType.AutoSize)); battlefield.RowStyles.Add(new RowStyle(SizeType.AutoSize)); battlefield.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddSetting(battlefield, 0, 0, "游戏模式", gameMode);
        AddSetting(battlefield, 0, 1, "地图", maps);
        battlefield.AutoSize = true;
        battlefieldBox.AutoSize = true;
        battlefieldBox.Controls.Add(battlefield);
        settings.Controls.Add(battlefieldBox, 0, 0);

        var participantsBox = new GroupBox { Text = "玩家与出生位置", Dock = DockStyle.Fill, Padding = new Padding(8), Margin = Padding.Empty };
        participantsBox.Controls.Add(grid);
        root.Controls.Add(participantsBox, 0, 2);
        root.Controls.Add(content, 0, 3);

        var mapVisibilityTip = new ToolTip();
        mapVisibilityTip.SetToolTip(revealAllMap, "开局揭示整张地图，移除初始黑幕。");

        var rulesBox = new GroupBox { Text = "规则与显示", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var rules = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 7 };
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddSetting(rules, 0, 0, "分辨率", resolution); AddSetting(rules, 0, 1, "游戏速度", gameSpeed); AddSetting(rules, 0, 2, "初始资金", credits);
        int ruleRow = 3;
        foreach (CheckBox option in new[] { crates, superWeapons, shortGame, revealAllMap }) { rules.Controls.Add(option, 0, ruleRow++); rules.SetColumnSpan(option, 2); }
        rulesBox.Controls.Add(rules);
        settings.Controls.Add(rulesBox, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        roomStart.FlatAppearance.BorderSize = 0;
        Button leave = new() { Text = "离开房间", AutoSize = true, Height = 36 }; leave.Click += (_, _) => ReturnHomeRequested?.Invoke();
        roomStatus.Padding = new Padding(0, 9, 12, 0);
        actions.Controls.AddRange([roomStart, roomReady, leave]);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var messages = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        roomStatus.AutoSize = false; roomStatus.AutoEllipsis = true; roomStatus.Height = 28;
        messages.SizeChanged += (_, _) => roomStatus.Width = Math.Max(1, messages.ClientSize.Width - 8);
        messages.Controls.AddRange([roomStatus, status]); footer.Controls.Add(messages, 0, 0); footer.Controls.Add(actions, 1, 0);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);
    }

    private void ApplyVisualStyle()
    {
        foreach (ComboBox combo in ControlsOfType<ComboBox>(this)) combo.IntegralHeight = false;
        foreach (GroupBox group in ControlsOfType<GroupBox>(this)) group.ForeColor = Color.FromArgb(50, 53, 59);
        status.Padding = new Padding(0, 9, 10, 0);
    }

    private static IEnumerable<T> ControlsOfType<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (T descendant in ControlsOfType<T>(child)) yield return descendant;
        }
    }

    private static void AddSetting(TableLayoutPanel panel, int column, int row, string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 3, 12, 5);
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 5) }, column, row);
        panel.Controls.Add(control, column + 1, row);
    }

    private static Control PathRow(string label, TextBox textBox, Action? changed)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 125, Height = 29, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        row.Controls.Add(textBox, 1, 0);
        Button browse = new() { Text = "浏览…", AutoSize = true };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = textBox.Text }; if (dialog.ShowDialog() == DialogResult.OK) { textBox.Text = dialog.SelectedPath; changed?.Invoke(); } };
        row.Controls.Add(browse, 2, 0);
        return row;
    }

    private void ReloadMaps()
    {
        List<MapInfo> found = MapScanner.Scan(runtimePath.Text.Trim());
        maps.DataSource = found;
        maps.Width = 250;
        status.Text = $"找到 {found.Count} 张可用地图";
    }

    private void ApplyModeCountries()
    {
        Choice[] choices = gameMode.SelectedIndex == 0 ? GameData.Countries : GameData.YuriCountries;
        if (grid.Columns[nameof(ParticipantRow.Country)] is DataGridViewComboBoxColumn column)
            column.DataSource = choices.ToArray();
        foreach (ParticipantRow participant in participants)
            if (!choices.Any(c => c.Value == participant.Country)) participant.Country = choices[0].Value;
        grid.Refresh();
    }

    private void ReloadSaves()
    {
        string directory = Path.Combine(runtimePath.Text.Trim(), "Saved Games");
        List<SaveInfo> saves = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.SAV", SearchOption.TopDirectoryOnly).Select(path => new SaveInfo(path, SaveMetadataReader.GetDisplayName(path), File.GetLastWriteTime(path), SaveMetadataReader.UsesAresExtensions(path))).OrderByDescending(save => save.LastWriteTime).ToList()
            : [];
        savedGames.DataSource = saves;
    }

    private void SyncParticipantSlots(int capacity)
    {
        capacity = Math.Clamp(capacity, 1, 8);
        while (participants.Count > capacity) participants.RemoveAt(participants.Count - 1);
        while (participants.Count < capacity)
        {
            int index = participants.Count;
            participants.Add(new ParticipantRow
            {
                SlotType = index == 1 ? 2 : 1,
                Name = index == 1 ? "开放" : $"电脑 {index}",
                ReadyStatus = index == 1 ? "等待加入" : "就绪",
                Country = GameData.Countries[index % GameData.Countries.Length].Value,
                Color = GameData.Colors[index].Value,
                Team = 1,
                Difficulty = 0,
                Start = index + 1
            });
        }
        if (participants.All(item => item.SlotType != 0))
        {
            participants[0].SlotType = 0;
            participants[0].Name = config.PlayerName;
        }
        grid.Refresh();
    }

    private void Generate(bool showSuccess)
    {
        grid.EndEdit();
        if (maps.SelectedItem is not MapInfo map) throw new InvalidOperationException("没有选择有效地图。");
        ValidateInputs(map);

        config.RuntimePath = runtimePath.Text.Trim();
        ParticipantRow human = participants.Single(item => item.SlotType == 0);
        List<ParticipantRow> ais = participants.Where(item => item.SlotType == 1).ToList();
        config.PlayerName = human.Name.Trim();
        ResolutionChoice selectedResolution = (ResolutionChoice)resolution.SelectedItem!;
        config.ResolutionWidth = selectedResolution.Width;
        config.ResolutionHeight = selectedResolution.Height;
        GameSpeedChoice selectedSpeed = (GameSpeedChoice)gameSpeed.SelectedItem!;
        config.GameSpeed = selectedSpeed.GameSpeed;
        config.MaxGameTicks = selectedSpeed.MaxGameTicks;
        config.ShortGame = shortGame.Checked;
        config.Save();

        IniFileEditor.SetVideoResolution(config.RuntimePath, selectedResolution.Width, selectedResolution.Height);
        IniFileEditor.ConfigureCncDdraw(config.RuntimePath, selectedSpeed.MaxGameTicks);
        SpawnWriter.Write(config.RuntimePath, new LaunchOptions(gameMode.SelectedIndex == 0, config.PlayerName, map, new Choice("", human.Country), new Choice("", human.Color), human.Team, human.Start, (int)credits.Value, config.GameSpeed, crates.Checked, superWeapons.Checked, config.ShortGame, revealAllMap.Checked, ais));
        status.Text = "配置已生成";
        if (showSuccess) MessageBox.Show("已生成 spawn.ini 和 spawnmap.ini。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ValidateInputs(MapInfo map)
    {
        if (!Directory.Exists(runtimePath.Text.Trim())) throw new DirectoryNotFoundException("独立运行目录尚未建立。请先执行部署脚本。");
        if (!File.Exists(Path.Combine(runtimePath.Text.Trim(), "Syringe.exe"))) throw new FileNotFoundException("运行目录中缺少 Syringe.exe。请先执行部署脚本。");
        List<ParticipantRow> active = participants.Where(item => item.SlotType is 0 or 1).ToList();
        if (active.Count(item => item.SlotType == 0) != 1) throw new InvalidOperationException("必须且只能保留一个“玩家”位置。");
        if (active.Count < 2) throw new InvalidOperationException("至少需要一个电脑玩家。");
        if (active.Any(item => item.Start < 1 || item.Start > map.StartingPoints)) throw new InvalidOperationException($"这张地图只有 {map.StartingPoints} 个出生点。");
        var positions = active.Select(item => item.Start).ToList();
        if (positions.Distinct().Count() != positions.Count) throw new InvalidOperationException("出生点不能重复。");
        var colors = active.Select(item => item.Color).ToList();
        if (colors.Distinct().Count() != colors.Count) throw new InvalidOperationException("颜色不能重复。");
    }

    private void Launch()
    {
        try
        {
            Generate(false);
            StartGameProcess(loadSave: false);
            status.Text = "游戏已启动";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLanLobby()
    {
        try
        {
            grid.EndEdit();
            if (maps.SelectedItem is not MapInfo map) throw new InvalidOperationException("没有选择有效地图。");
            string runtime = runtimePath.Text.Trim();
            if (!Directory.Exists(runtime)) throw new DirectoryNotFoundException("当前游戏目录不存在。");
            if (!File.Exists(Path.Combine(runtime, "Syringe.exe"))) throw new FileNotFoundException("运行目录中缺少 Syringe.exe。");
            ParticipantRow human = participants.Single(item => item.SlotType == 0);
            List<ParticipantRow> available = [human, .. participants.Where(item => item.SlotType == 2), .. participants.Where(item => item.SlotType == 1)];
            if (available.Count < 2) throw new InvalidOperationException("局域网地图至少需要两个未关闭的位置。请把一个位置设为“开放”或“电脑”。");
            if (available.Select(item => item.Start).Distinct().Count() != available.Count) throw new InvalidOperationException("可用位置的出生点不能重复。");
            if (available.Select(item => item.Color).Distinct().Count() != available.Count) throw new InvalidOperationException("可用位置的颜色不能重复。");

            GameSpeedChoice selectedSpeed = (GameSpeedChoice)gameSpeed.SelectedItem!;
            byte[] mapData = File.ReadAllBytes(map.Path);
            List<LanSlot> slots = available.Select((item, index) => new LanSlot(
                item.SlotType == 2 ? GameData.Countries[index % GameData.Countries.Length].Value : item.Country,
                item.SlotType == 2 ? GameData.Colors[index % GameData.Colors.Length].Value : item.Color,
                item.SlotType == 2 ? 0 : item.Team,
                item.SlotType == 2 ? 0 : item.Difficulty,
                item.Start,
                item.SlotType == 1)).ToList();
            var setup = new LanGameSetup(gameMode.SelectedIndex == 0, map.Name, Path.GetFileName(map.Path), mapData, LanGameSetup.Hash(mapData), LanCompatibility.ComputeComponentHash(runtime), (int)credits.Value, selectedSpeed.GameSpeed, selectedSpeed.MaxGameTicks, crates.Checked, superWeapons.Checked, shortGame.Checked, revealAllMap.Checked, slots);
            using var lobby = new LanLobbyForm(runtime, human.Name.Trim(), setup);
            lobby.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法打开局域网", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSelectedSave()
    {
        try
        {
            if (savedGames.SelectedItem is not SaveInfo save) throw new InvalidOperationException("没有可加载的 .SAV 存档。");
            GameSpeedChoice selectedSpeed = (GameSpeedChoice)gameSpeed.SelectedItem!;
            IniFileEditor.ConfigureCncDdraw(runtimePath.Text.Trim(), selectedSpeed.MaxGameTicks);
            SpawnWriter.WriteLoadSave(runtimePath.Text.Trim(), save);
            StartGameProcess(loadSave: true, useAresExtensions: save.UsesAresExtensions);
            status.Text = $"正在加载 {save.DisplayName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法加载存档", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartGameProcess(bool loadSave, bool useAresExtensions = true)
    {
        string runtime = runtimePath.Text.Trim();
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(runtime, "Syringe.exe"),
            Arguments = useAresExtensions
                ? "-i=Ares.dll -i=CnCNet-Spawner.dll -i=Phobos.dll gamemd-spawn.exe --args=\"-SPAWN -LOG -CD -Include -Inheritance -RA2ModeSaveID=0x8d113b94\""
                : "-i=CnCNet-Spawner.dll gamemd-spawn.exe --args=\"-SPAWN -LOG -CD -Include -Inheritance -RA2ModeSaveID=0x8d113b94\"",
            WorkingDirectory = runtime,
            UseShellExecute = true
        };
        Process.Start(psi);
        startingRoom = true;
        roomStart.Enabled = false;
        _ = ObserveGameExitAsync(runtime);
    }

    private async Task ObserveGameExitAsync(string runtime)
    {
        // Syringe is only the injector; wait for the actual game process instead.
        Process? game = null;
        try
        {
            for (int attempt = 0; attempt < 60 && !IsDisposed; attempt++)
            {
                await Task.Delay(500);
                foreach (Process candidate in Process.GetProcessesByName("gamemd-spawn"))
                {
                    bool matches = false;
                    try { matches = string.Equals(candidate.MainModule?.FileName, Path.Combine(runtime, "gamemd-spawn.exe"), StringComparison.OrdinalIgnoreCase); }
                    catch { }
                    if (matches) { game = candidate; break; }
                    candidate.Dispose();
                }
                if (game is not null) break;
            }
            if (game is null)
            {
                if (!IsDisposed) status.Text = "未检测到游戏进程，请检查启动日志。";
                return;
            }
            await game.WaitForExitAsync();
            if (!IsDisposed) { status.Text = "游戏已退出，可以重新准备并开始。"; await SetRoomReadyAsync(false); }
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = $"游戏状态检测失败：{ex.Message}"; }
        finally
        {
            game?.Dispose();
            if (!IsDisposed) { startingRoom = false; roomStart.Enabled = roomHost is not null && roomHost.CanLaunch(out _); }
        }
    }

    private Guid LocalRoomPlayerId => roomHost?.HostId ?? roomClient?.PlayerId ?? Guid.Empty;

    private void SelectPreviewStart(int selected)
    {
        int index = rowPlayerIds.FindIndex(id => id == LocalRoomPlayerId);
        if (index < 0 || index >= participants.Count || startingRoom) return;
        if (participants.Where((row, position) => position != index).Any(row => row.SlotType is 0 or 1 && row.Start == selected))
        {
            status.Text = "这个出生点已经被占用，请选择其他位置。";
            return;
        }
        participants[index].Start = selected;
        mapPreview.SetPlayers(participants.Where(row => row.SlotType is 0 or 1).Select(row => (row.Start, row.Color, row.Name)));
        grid.Refresh();
        UpdateLocalRoomPlayer(participants[index]);
    }

    private LanGameSetup BuildRoomSetup(bool leavingJoinedRoom = false)
    {
        grid.EndEdit();
        if (maps.SelectedItem is not MapInfo map) throw new InvalidOperationException("没有选择有效地图。");
        string runtime = runtimePath.Text.Trim();
        if (!Directory.Exists(runtime)) throw new DirectoryNotFoundException("当前游戏目录不存在。");
        if (!File.Exists(Path.Combine(runtime, "Syringe.exe"))) throw new FileNotFoundException("运行目录中缺少 Syringe.exe。");

        Guid localId = LocalRoomPlayerId;
        var humans = new List<ParticipantRow>();
        var open = new List<ParticipantRow>();
        var computers = new List<ParticipantRow>();
        var closed = new List<ParticipantRow>();
        for (int i = 0; i < participants.Count; i++)
        {
            ParticipantRow row = participants[i];
            Guid? playerId = i < rowPlayerIds.Count ? rowPlayerIds[i] : null;
            if (playerId.HasValue && (!leavingJoinedRoom || playerId.Value == localId)) humans.Add(row);
            else if (playerId.HasValue || row.SlotType == 2) open.Add(row);
            else if (row.SlotType == 1) computers.Add(row);
            else closed.Add(row);
        }
        if (humans.Count == 0)
        {
            ParticipantRow human = participants.FirstOrDefault(row => row.SlotType == 0) ?? participants[0];
            humans.Add(human);
            open.Remove(human); computers.Remove(human); closed.Remove(human);
        }
        List<ParticipantRow> ordered = [.. humans, .. open, .. computers, .. closed];
        Choice[] countries = gameMode.SelectedIndex == 0 ? GameData.Countries : GameData.YuriCountries;
        List<LanSlot> slots = ordered.Select((row, index) => new LanSlot(
            row.SlotType == 2 ? countries[index % countries.Length].Value : row.Country,
            row.SlotType == 2 ? GameData.Colors[index % GameData.Colors.Length].Value : row.Color,
            row.SlotType == 2 ? 0 : row.Team,
            row.SlotType == 2 ? 0 : row.Difficulty,
            row.Start,
            row.SlotType == 1,
            row.SlotType == 3)).ToList();
        List<LanSlot> active = slots.Where(slot => !slot.Closed).ToList();
        if (active.Count < 2) throw new InvalidOperationException("至少需要两个未关闭位置。");
        if (active.Select(slot => slot.Start).Distinct().Count() != active.Count) throw new InvalidOperationException("可用位置的出生点不能重复。");
        if (active.Select(slot => slot.Color).Distinct().Count() != active.Count) throw new InvalidOperationException("可用位置的颜色不能重复。");
        GameSpeedChoice speed = (GameSpeedChoice)gameSpeed.SelectedItem!;
        byte[] mapData = File.ReadAllBytes(map.Path);
        return new LanGameSetup(gameMode.SelectedIndex == 0, map.Name, Path.GetFileName(map.Path), mapData, LanGameSetup.Hash(mapData), LanCompatibility.ComputeComponentHash(runtime), (int)credits.Value, speed.GameSpeed, speed.MaxGameTicks, crates.Checked, superWeapons.Checked, shortGame.Checked, revealAllMap.Checked, slots);
    }

    private void StartOwnRoom()
    {
        try
        {
            LanGameSetup setup = BuildRoomSetup(leavingJoinedRoom: roomClient is not null);
            int hostRow = rowPlayerIds.FindIndex(id => id == LocalRoomPlayerId);
            string hostName = hostRow >= 0 && hostRow < participants.Count ? participants[hostRow].Name : config.PlayerName;
            DisposeRoomNetworking();
            string selectedRoomName = string.IsNullOrWhiteSpace(entry.RoomName) ? $"{hostName} 的房间" : entry.RoomName.Trim();
            roomHost = new LanLobbyHost(selectedRoomName, hostName, setup);
            roomHost.StateChanged += state => Ui(() => ApplyRoomState(state));
            roomHost.Launching += package => Ui(() => LaunchRoomPackage(package, roomHost?.HostId ?? Guid.Empty, "127.0.0.1"));
            roomHost.Error += message => Ui(() => roomStatus.Text = message);
            roomHost.Start();
        }
        catch (Exception ex)
        {
            roomStatus.Text = $"无法创建房间：{ex.Message}";
        }
    }

    private void PublishHostSetup()
    {
        if (applyingRoomState || roomHost is null) return;
        try { roomHost.UpdateSetup(BuildRoomSetup()); }
        catch (Exception ex) { roomStatus.Text = ex.Message; }
    }

    private void ApplyRoomState(LanRoomState state)
    {
        applyingRoomState = true;
        try
        {
            if (maps.DataSource is IEnumerable<MapInfo> mapItems)
            {
                MapInfo? match = mapItems.FirstOrDefault(map => Path.GetFileName(map.Path).Equals(state.Setup.MapFileName, StringComparison.OrdinalIgnoreCase) && LanGameSetup.Hash(File.ReadAllBytes(map.Path)) == state.Setup.MapHash);
                if (match is not null) maps.SelectedItem = match;
                mapPreview.Map = match;
            }
            gameMode.SelectedIndex = state.Setup.Ra2Mode ? 0 : 1;
            gameSpeed.SelectedItem = GameData.GameSpeeds.FirstOrDefault(speed => speed.GameSpeed == state.Setup.GameSpeed && speed.MaxGameTicks == state.Setup.MaxGameTicks) ?? gameSpeed.SelectedItem;
            credits.Value = Math.Clamp(state.Setup.Credits, (int)credits.Minimum, (int)credits.Maximum);
            crates.Checked = state.Setup.Crates;
            superWeapons.Checked = state.Setup.SuperWeapons;
            shortGame.Checked = state.Setup.ShortGame;
            revealAllMap.Checked = state.Setup.RevealAllMap;

            participants.Clear();
            rowPlayerIds.Clear();
            for (int i = 0; i < state.Setup.Slots.Count; i++)
            {
                LanSlot slot = state.Setup.Slots[i];
                LanPlayer? player = i < state.Players.Count ? state.Players[i] : null;
                int slotType = player is not null ? 0 : slot.Closed ? 3 : slot.Computer ? 1 : 2;
                participants.Add(new ParticipantRow
                {
                    SlotType = slotType,
                    Name = player?.Name ?? (slotType == 1 ? $"电脑 {i}" : slotType == 2 ? "开放" : "关闭"),
                    ReadyStatus = player is not null
                        ? state.Players.Count == 1 && player.IsHost ? "房主" : player.Ready ? "已准备" : player.IsHost ? "房主未准备" : "未准备"
                        : slotType == 1 ? "就绪" : slotType == 2 ? "等待加入" : "—",
                    Country = slot.Country,
                    Color = slot.Color,
                    Team = slot.Team,
                    Difficulty = slot.Difficulty,
                    Start = slot.Start
                });
                rowPlayerIds.Add(player?.Id);
            }
            grid.Refresh();
            LanPlayer? local = state.Players.FirstOrDefault(player => player.Id == LocalRoomPlayerId);
            localRoomReady = local?.Ready ?? false;
            roomReady.Enabled = state.Players.Count > 1;
            roomReady.Text = localRoomReady ? "取消准备" : "准备";
            roomReady.Visible = state.Players.Count > 1;
            bool isHost = roomHost is not null;
            roomStart.Visible = isHost;
            string launchReason = "";
            roomStart.Enabled = isHost && !startingRoom && roomHost!.CanLaunch(out launchReason);
            SetHostControlsEnabled(isHost);
            string address = isHost ? LanNetworkAddress.GetPreferredIPv4() : roomClient?.HostAddress ?? "";
            string role = isHost ? "房主" : "玩家";
            roomStatus.Text = $"{role}｜{state.RoomName}｜{state.Players.Count}/{state.MaxHumanPlayers} 人｜{address}:{LanLobbyHost.LobbyPort}" + (launchReason.Length > 0 ? $"｜{launchReason}" : "");
        }
        finally { applyingRoomState = false; }
    }

    private void SetHostControlsEnabled(bool enabled)
    {
        gameMode.Enabled = enabled;
        maps.Enabled = enabled;
        gameSpeed.Enabled = enabled;
        credits.Enabled = enabled;
        crates.Enabled = enabled;
        superWeapons.Enabled = enabled;
        shortGame.Enabled = enabled;
        revealAllMap.Enabled = enabled;
    }

    private void UpdateLocalRoomPlayer(ParticipantRow row)
    {
        if (applyingRoomState || LocalRoomPlayerId == Guid.Empty) return;
        var slot = new LanSlot(row.Country, row.Color, row.Team, row.Difficulty, row.Start, false);
        try
        {
            if (roomHost is not null) roomHost.UpdateHostPlayer(slot);
            else if (roomClient is not null) _ = UpdateClientPlayerAsync(slot);
        }
        catch (Exception ex) { roomStatus.Text = ex.Message; }
    }

    private async Task UpdateClientPlayerAsync(LanSlot slot)
    {
        try { if (roomClient is not null) await roomClient.UpdatePlayerAsync(slot); }
        catch (Exception ex) { roomStatus.Text = ex.Message; }
    }

    private async Task SetRoomReadyAsync(bool ready)
    {
        if (applyingRoomState || !roomReady.Enabled) return;
        try
        {
            if (roomHost is not null) roomHost.SetHostReady(ready);
            else if (roomClient is not null) await roomClient.SetReadyAsync(ready);
        }
        catch (Exception ex) { roomStatus.Text = ex.Message; }
    }

    private async Task JoinRoomAsync(string hostAddress, string? requestedPlayerName = null)
    {
        try
        {
            string player = string.IsNullOrWhiteSpace(requestedPlayerName) ? entry.PlayerName : requestedPlayerName;
            DisposeRoomNetworking();
            var client = new LanLobbyClient();
            roomClient = client;
            client.StateChanged += state => Ui(() => ApplyRoomState(state));
            client.LaunchReceived += package => Ui(() => LaunchRoomPackage(package, client.PlayerId, client.HostAddress));
            client.Error += message => Ui(() => roomStatus.Text = message);
            roomStatus.Text = $"正在连接 {hostAddress}:{LanLobbyHost.LobbyPort}…";
            await client.ConnectAsync(hostAddress, player);
        }
        catch (Exception ex)
        {
            roomStatus.Text = $"加入失败：{ex.Message}";
            MessageBox.Show(ex.Message, "无法加入房间", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ReturnHomeRequested?.Invoke();
        }
    }

    private async Task StartRoomGameAsync()
    {
        if (roomHost is null) return;
        try
        {
            if (roomHost.CurrentState.Players.Count == 1)
            {
                Generate(false);
                StartGameProcess(loadSave: false);
                status.Text = "单机游戏已启动";
            }
            else await roomHost.LaunchAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法开始", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LaunchRoomPackage(LanLaunchPackage package, Guid localId, string hostAddress)
    {
        if (startingRoom) return;
        startingRoom = true;
        try
        {
            string runtime = runtimePath.Text.Trim();
            if (localId == Guid.Empty) throw new InvalidOperationException("尚未收到本机玩家编号。");
            if (!string.Equals(LanCompatibility.ComputeComponentHash(runtime), package.Setup.ComponentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("本机的联机组件与房主不一致。");
            IniFileEditor.ConfigureCncDdraw(runtime, package.Setup.MaxGameTicks);
            SpawnWriter.WriteLan(runtime, package, localId, hostAddress);
            StartGameProcess(loadSave: false);
            status.Text = "联机配置已同步，游戏正在启动";
        }
        catch (Exception ex)
        {
            startingRoom = false;
            MessageBox.Show(ex.Message, "联机启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private void DisposeRoomNetworking()
    {
        roomClient?.Dispose();
        roomHost?.Dispose();
        roomClient = null;
        roomHost = null;
        startingRoom = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) mapPreview.StartSelected -= SelectPreviewStart;
        if (disposing) DisposeRoomNetworking();
        base.Dispose(disposing);
    }
}
