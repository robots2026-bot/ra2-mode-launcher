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
    private readonly NumericUpDown customSpeed = new() { Minimum = 1, Maximum = 1000, Value = 60, Width = 90, Enabled = false };
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
    private bool editingParticipant;
    private bool startingRoom;
    private bool localRoomReady;
    private LanRoomState? lastRoomState;
    private readonly Label roomTitle = new() { AutoSize = true };
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
        gameSpeed.SelectedIndexChanged += (_, _) => { customSpeed.Enabled = gameSpeed.Enabled && gameSpeed.SelectedItem is GameSpeedChoice { Custom: true }; PublishHostSetup(); };
        customSpeed.ValueChanged += (_, _) => { if (gameSpeed.SelectedItem is GameSpeedChoice { Custom: true }) PublishHostSetup(); };
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
        SelectSpeed(config.GameSpeed, config.MaxGameTicks);
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
            if (grid.BeginEdit(true) && grid.EditingControl is DataGridViewComboBoxEditingControl editor)
                editor.DroppedDown = true;
        };
        grid.DataError += (_, e) =>
        {
            StartupTrace.Mark($"grid data error row={e.RowIndex} column={e.ColumnIndex}: {e.Exception?.Message}");
            e.ThrowException = false;
        };
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= participants.Count || e.ColumnIndex < 0) return;
            ParticipantRow participant = participants[e.RowIndex];
            string column = grid.Columns[e.ColumnIndex].Name;
            Guid? owner = e.RowIndex < rowPlayerIds.Count ? rowPlayerIds[e.RowIndex] : null;
            if (e.CellStyle is not null)
            {
                if (owner == LocalRoomPlayerId && owner.HasValue) e.CellStyle.BackColor = Color.FromArgb(232, 244, 255);
                if (owner.HasValue && owner != LocalRoomPlayerId || roomClient is not null && !owner.HasValue) e.CellStyle.ForeColor = Color.Gray;
            }
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
            if (e.RowIndex < 0 || e.RowIndex >= participants.Count) { e.Cancel = true; return; }
            if (startingRoom || lastRoomState is null || lastRoomState.Phase != "等待中") { e.Cancel = true; return; }
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
            if (applyingRoomState || editingParticipant || e.RowIndex < 0 || e.RowIndex >= participants.Count || e.ColumnIndex < 0) return;
            editingParticipant = true;
            try
            {
                ParticipantRow changed = participants[e.RowIndex];
                string column = grid.Columns[e.ColumnIndex].Name;
                Guid? rowPlayerId = e.RowIndex < rowPlayerIds.Count ? rowPlayerIds[e.RowIndex] : null;
                if (column == nameof(ParticipantRow.SlotType) && !rowPlayerId.HasValue)
                {
                    if (changed.SlotType == 0) changed.SlotType = 2;
                    changed.Name = changed.SlotType switch { 1 => $"电脑 {e.RowIndex}", 2 => "开放", _ => "关闭" };
                    changed.ReadyStatus = changed.SlotType switch { 1 => "就绪", 2 => "等待加入", _ => "—" };
                }
                grid.Invalidate();
                mapPreview.SetPlayers(participants.Where(row => row.SlotType is 0 or 1).Select(row => (row.Start, row.Color, row.Name)));
                // Finish the DataGridView commit before publishing and applying a room snapshot.
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || applyingRoomState) return;
                    if (rowPlayerId == LocalRoomPlayerId && rowPlayerId.HasValue)
                    {
                        if (column == nameof(ParticipantRow.Name)) _ = UpdateRoomNameAsync(changed.Name);
                        else UpdateLocalRoomPlayer(changed);
                    }
                    else PublishHostSetup();
                }));
                }
            finally { editingParticipant = false; }
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
        roomTitle.Text = "正在进入房间…"; roomTitle.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
        titleBlock.Controls.Add(roomTitle);
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
        AddSetting(rules, 0, 0, "分辨率", resolution); AddSetting(rules, 0, 1, "初始速度", gameSpeed); AddSetting(rules, 0, 2, "自定义速度/秒", customSpeed); AddSetting(rules, 0, 3, "初始资金", credits);
        int ruleRow = 4;
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

    private GameSpeedChoice SelectedSpeed => gameSpeed.SelectedItem is GameSpeedChoice { Custom: true } ? new("自定义", 0, (int)customSpeed.Value, true) : (GameSpeedChoice)gameSpeed.SelectedItem!;

    private void SelectSpeed(int engineSpeed, int maxGameTicks)
    {
        GameSpeedChoice? preset = GameData.GameSpeeds.FirstOrDefault(item => !item.Custom && item.GameSpeed == engineSpeed && item.MaxGameTicks == maxGameTicks);
        if (preset is not null) gameSpeed.SelectedItem = preset;
        else if (engineSpeed == 0 && maxGameTicks is >= 1 and <= 1000)
        {
            customSpeed.Value = maxGameTicks;
            gameSpeed.SelectedItem = GameData.GameSpeeds.Single(item => item.Custom);
        }
        else gameSpeed.SelectedItem = GameData.GameSpeeds[4];
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
        GameSpeedChoice selectedSpeed = SelectedSpeed;
        config.GameSpeed = selectedSpeed.GameSpeed;
        config.MaxGameTicks = selectedSpeed.MaxGameTicks;
        config.ShortGame = shortGame.Checked;
        config.Save();

        IniFileEditor.SetVideoResolution(config.RuntimePath, selectedResolution.Width, selectedResolution.Height);
        int launchSpeed = IniFileEditor.ConfigureSinglePlayerSpeed(config.RuntimePath, selectedSpeed.GameSpeed, selectedSpeed.MaxGameTicks);
        SpawnWriter.Write(config.RuntimePath, new LaunchOptions(gameMode.SelectedIndex == 0, config.PlayerName, map, new Choice("", human.Country), new Choice("", human.Color), human.Team, human.Start, (int)credits.Value, launchSpeed, crates.Checked, superWeapons.Checked, config.ShortGame, revealAllMap.Checked, ais));
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
            LanGameSetup setup = BuildRoomSetup();
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
            GameSpeedChoice selectedSpeed = SelectedSpeed;
            IniFileEditor.ConfigureSinglePlayerSpeed(runtimePath.Text.Trim(), selectedSpeed.GameSpeed, selectedSpeed.MaxGameTicks);
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
            int exitCode = game.ExitCode;
            StartupTrace.Mark($"game exited: pid={game.Id}, exitCode=0x{unchecked((uint)exitCode):X8}");
            if (!IsDisposed) status.Text = exitCode == 0
                ? "游戏已退出，等待其他玩家返回房间。"
                : $"游戏已退出（退出码 0x{unchecked((uint)exitCode):X8}），详情见启动日志。";
        }
        catch (Exception ex) { if (!IsDisposed) status.Text = $"游戏状态检测失败：{ex.Message}"; }
        finally
        {
            game?.Dispose();
            if (!IsDisposed)
            {
                startingRoom = false;
                roomHost?.ReportGameExited();
                if (roomClient is not null) { try { await roomClient.ReportGameExitedAsync(); } catch (Exception ex) { roomStatus.Text = ex.Message; } }
                if (lastRoomState is not null) ApplyRoomState(lastRoomState);
            }
        }
    }

    private Guid LocalRoomPlayerId => roomHost?.HostId ?? roomClient?.PlayerId ?? Guid.Empty;

    private void SelectPreviewStart(int selected)
    {
        int index = rowPlayerIds.FindIndex(id => id == LocalRoomPlayerId);
        if (index < 0 || index >= participants.Count || startingRoom || lastRoomState?.Phase != "等待中") return;
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
        if (maps.SelectedItem is not MapInfo map) throw new InvalidOperationException("没有选择有效地图。");
        string runtime = runtimePath.Text.Trim();
        if (!Directory.Exists(runtime)) throw new DirectoryNotFoundException("当前游戏目录不存在。");
        if (!File.Exists(Path.Combine(runtime, "Syringe.exe"))) throw new FileNotFoundException("运行目录中缺少 Syringe.exe。");

        Guid localId = LocalRoomPlayerId;
        var humanIndices = new HashSet<int>();
        for (int i = 0; i < participants.Count; i++)
        {
            Guid? playerId = i < rowPlayerIds.Count ? rowPlayerIds[i] : null;
            if (playerId.HasValue && (!leavingJoinedRoom || playerId.Value == localId)) humanIndices.Add(i);
        }
        if (humanIndices.Count == 0)
        {
            int human = participants.ToList().FindIndex(row => row.SlotType == 0);
            humanIndices.Add(human >= 0 ? human : 0);
        }
        // UI row index is the room slot identity; slot type must never sort the list.
        List<LanSlot> slots = participants.Select((row, index) => new LanSlot(
            row.Country,
            row.Color,
            row.Team,
            row.Difficulty,
            row.Start,
            !humanIndices.Contains(index) && row.SlotType == 1,
            !humanIndices.Contains(index) && row.SlotType == 3)).ToList();
        List<LanSlot> active = slots.Where((slot, index) => !slot.Closed && (humanIndices.Contains(index) || slot.Computer)).ToList();
        if (slots.Count(slot => !slot.Closed) < 2) throw new InvalidOperationException("至少需要两个未关闭位置。");
        if (active.Select(slot => slot.Start).Distinct().Count() != active.Count) throw new InvalidOperationException("可用位置的出生点不能重复。");
        if (active.Select(slot => slot.Color).Distinct().Count() != active.Count) throw new InvalidOperationException("可用位置的颜色不能重复。");
        GameSpeedChoice speed = SelectedSpeed;
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
            roomHost.PrepareLaunch = package => PrepareLaunchOnUiAsync(package, roomHost?.HostId ?? Guid.Empty, "127.0.0.1");
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
        catch (Exception ex) { if (lastRoomState is not null) ApplyRoomState(lastRoomState); roomStatus.Text = ex.Message; }
    }

    private void ApplyRoomState(LanRoomState state)
    {
        lastRoomState = state;
        int selectedRow = grid.CurrentCell?.RowIndex ?? -1;
        int selectedColumn = grid.CurrentCell?.ColumnIndex ?? -1;
        applyingRoomState = true;
        try
        {
            grid.EndEdit();
            if (maps.DataSource is IEnumerable<MapInfo> mapItems)
            {
                MapInfo? match = mapItems.FirstOrDefault(map => Path.GetFileName(map.Path).Equals(state.Setup.MapFileName, StringComparison.OrdinalIgnoreCase) && LanGameSetup.Hash(File.ReadAllBytes(map.Path)) == state.Setup.MapHash);
                if (match is not null) maps.SelectedItem = match;
                if (match is null && roomClient is not null && LanGameSetup.Hash(state.Setup.MapData) == state.Setup.MapHash)
                {
                    string cache = Path.Combine(Path.GetTempPath(), "Ra2ModeLauncher-map-preview", state.Setup.MapHash + ".map");
                    Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                    if (!File.Exists(cache) || LanGameSetup.Hash(File.ReadAllBytes(cache)) != state.Setup.MapHash) File.WriteAllBytes(cache, state.Setup.MapData);
                    match = MapScanner.Read(cache) with { Name = state.Setup.MapName };
                    maps.DataSource = mapItems.Append(match).ToList();
                    maps.SelectedItem = match;
                }
                mapPreview.Map = match;
            }
            gameMode.SelectedIndex = state.Setup.Ra2Mode ? 0 : 1;
            SelectSpeed(state.Setup.GameSpeed, state.Setup.MaxGameTicks);
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
                LanPlayer? player = state.Players.FirstOrDefault(player => player.SlotIndex == i);
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
                if (player is null)
                {
                    // Human ownership comes from joining the room, never from a dropdown.
                    ((DataGridViewComboBoxCell)grid.Rows[i].Cells[nameof(ParticipantRow.SlotType)]).DataSource = GameData.SlotTypes.Where(choice => choice.Value != 0).ToArray();
                }
            }
            if (selectedRow >= 0 && selectedRow < grid.Rows.Count && selectedColumn >= 0 && selectedColumn < grid.Columns.Count)
                grid.CurrentCell = grid[selectedColumn, selectedRow];
            grid.Refresh();
            LanPlayer? local = state.Players.FirstOrDefault(player => player.Id == LocalRoomPlayerId);
            localRoomReady = local?.Ready ?? false;
            int localRow = rowPlayerIds.FindIndex(id => id == LocalRoomPlayerId);
            mapPreview.LocalStart = localRow >= 0 ? participants[localRow].Start : 0;
            mapPreview.Invalidate();
            bool waiting = state.Phase == "等待中" && !startingRoom;
            roomReady.Enabled = waiting && state.Players.Count > 1;
            roomReady.Text = localRoomReady ? "取消准备" : "准备";
            roomReady.Visible = state.Players.Count > 1;
            bool isHost = roomHost is not null;
            roomStart.Visible = isHost;
            string launchReason = "";
            roomStart.Enabled = isHost && !startingRoom && roomHost!.CanLaunch(out launchReason);
            SetHostControlsEnabled(isHost && waiting);
            runtimePath.Enabled = waiting && isHost;
            resolution.Enabled = waiting;
            grid.Enabled = waiting;
            mapPreview.AllowStartSelection = waiting && local is not null;
            roomTitle.Text = $"{state.RoomName} · {(isHost ? "房主" : "玩家")} · {state.Phase}";
            string address = isHost ? LanNetworkAddress.GetPreferredIPv4() : roomClient?.HostAddress ?? "";
            string role = isHost ? "房主" : "玩家";
            roomStatus.Text = $"{role}｜{state.RoomName}｜{state.Players.Count}/{state.MaxHumanPlayers} 人｜{address}:{LanLobbyHost.LobbyPort}" + (launchReason.Length > 0 ? $"｜{launchReason}" : "");
            if (state.Players.Count > 1) roomStatus.Text += "｜速度由房主统一设置，局内锁定";
        }
        finally { applyingRoomState = false; }
    }

    private void SetHostControlsEnabled(bool enabled)
    {
        gameMode.Enabled = enabled;
        maps.Enabled = enabled;
        gameSpeed.Enabled = enabled;
        customSpeed.Enabled = enabled && gameSpeed.SelectedItem is GameSpeedChoice { Custom: true };
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
        catch (Exception ex) { if (lastRoomState is not null) ApplyRoomState(lastRoomState); roomStatus.Text = ex.Message; }
    }

    private async Task UpdateRoomNameAsync(string name)
    {
        try
        {
            if (roomHost is not null) roomHost.UpdateHostName(name);
            else if (roomClient is not null) await roomClient.UpdateNameAsync(name);
            config.PlayerName = name.Trim(); config.Save();
        }
        catch (Exception ex) { if (lastRoomState is not null) ApplyRoomState(lastRoomState); roomStatus.Text = ex.Message; }
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
            client.PrepareLaunch = package => PrepareLaunchOnUiAsync(package, client.PlayerId, client.HostAddress);
            client.StateChanged += state => Ui(() => ApplyRoomState(state));
            client.LaunchReceived += package => Ui(() => LaunchRoomPackage(package, client.PlayerId, client.HostAddress));
            client.Error += message => Ui(() => roomStatus.Text = message);
            client.Disconnected += message => Ui(() => { grid.Enabled = false; roomReady.Enabled = false; mapPreview.AllowStartSelection = false; roomStatus.Text = $"连接已断开：{message}"; ReturnHomeRequested?.Invoke(); });
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
                roomHost.SetSoloPlaying();
                StartGameProcess(loadSave: false);
                status.Text = "单机游戏已启动";
            }
            else await roomHost.LaunchAsync();
        }
        catch (Exception ex) { roomHost?.ReportGameExited(); MessageBox.Show(ex.Message, "无法开始", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private Task<string?> PrepareLaunchOnUiAsync(LanLaunchPackage package, Guid localId, string hostAddress)
    {
        var check = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed || !IsHandleCreated) { check.SetResult("房间窗口已关闭。"); return check.Task; }
        void Prepare()
        {
            try
            {
                string runtime = runtimePath.Text.Trim();
                if (localId == Guid.Empty) throw new InvalidOperationException("本机玩家编号尚未收到。");
                if (!File.Exists(Path.Combine(runtime, "Syringe.exe"))) throw new FileNotFoundException("缺少 Syringe.exe。");
                if (LanCompatibility.ComputeComponentHash(runtime) != package.Setup.ComponentHash) throw new InvalidOperationException("联机组件与房主不一致，请更新后重新准备。");
                ResolutionChoice display = (ResolutionChoice)resolution.SelectedItem!;
                IniFileEditor.SetVideoResolution(runtime, display.Width, display.Height);
                package.Setup.ValidateSpeed();
                IniFileEditor.ConfigureCncDdraw(runtime, package.Setup.EffectiveMaxGameTicks);
                SpawnWriter.WriteLan(runtime, package, localId, hostAddress);
                IniFileEditor.VerifyLanSpeed(runtime, package.Setup);
                status.Text = "本机检查通过，等待所有玩家确认。";
                check.SetResult(null);
            }
            catch (Exception ex) { check.SetResult(ex.Message); }
        }
        if (InvokeRequired) BeginInvoke((Action)Prepare); else Prepare();
        return check.Task;
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
            package.Setup.ValidateSpeed();
            IniFileEditor.ConfigureCncDdraw(runtime, package.Setup.EffectiveMaxGameTicks);
            SpawnWriter.WriteLan(runtime, package, localId, hostAddress);
            IniFileEditor.VerifyLanSpeed(runtime, package.Setup);
            StartGameProcess(loadSave: false);
            status.Text = "联机配置已同步，游戏正在启动";
        }
        catch (Exception ex)
        {
            startingRoom = false;
            roomHost?.ReportGameExited();
            if (roomClient is not null) _ = roomClient.ReportGameExitedAsync();
            MessageBox.Show(ex.Message, "联机启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke((Action)(() => { if (!IsDisposed) action(); }));
    }

    private void DisposeRoomNetworking()
    {
        roomClient?.Dispose();
        roomHost?.Dispose();
        roomClient = null;
        roomHost = null;
        startingRoom = false;
        lastRoomState = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) mapPreview.StartSelected -= SelectPreviewStart;
        if (disposing) DisposeRoomNetworking();
        base.Dispose(disposing);
    }
}
