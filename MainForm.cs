using System.ComponentModel;
using System.Diagnostics;

namespace Ra2ModeLauncher;

internal sealed class MainForm : Form
{
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
    private readonly MapPreviewControl mapPreview = new() { Dock = DockStyle.Fill };
    private readonly BindingList<ParticipantRow> participants = [];
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DarkSlateBlue };

    public MainForm()
    {
        StartupTrace.Mark("form constructor entered");
        Text = "红色警戒 2 / 尤里的复仇启动器";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 247, 249);
        AutoScaleMode = AutoScaleMode.Dpi;
        Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Width = Math.Clamp((int)(workArea.Width * 0.76), 1020, 1180);
        Height = Math.Clamp((int)(workArea.Height * 0.76), 700, 800);
        MinimumSize = new Size(920, 640);
        StartPosition = FormStartPosition.CenterScreen;

        runtimePath.Text = config.RuntimePath;
        gameMode.Items.AddRange(["红警2经典", "尤里复仇"]);
        gameMode.SelectedIndexChanged += (_, _) => ApplyModeCountries();
        maps.SelectedIndexChanged += (_, _) =>
        {
            mapPreview.Map = maps.SelectedItem as MapInfo;
            if (maps.SelectedItem is MapInfo map) SyncParticipantSlots(map.StartingPoints);
        };
        mapPreview.StartSelected += selected =>
        {
            ParticipantRow? human = participants.FirstOrDefault(item => item.SlotType == 0);
            if (human is not null) { human.Start = selected; grid.Refresh(); }
        };
        resolution.Items.AddRange(GameData.Resolutions);
        gameSpeed.Items.AddRange(GameData.GameSpeeds);
        StartupTrace.Mark("basic controls initialized");

        participants.Add(new ParticipantRow { SlotType = 0, Name = config.PlayerName, Country = GameData.Countries[0].Value, Color = GameData.Colors[0].Value, Team = 0, Difficulty = GameData.Difficulties[1].Value, Start = 1 });
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
        ReloadSaves();
        StartupTrace.Mark("form constructor finished");
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
            if ((participant.SlotType == 0 && column == nameof(ParticipantRow.Difficulty)) || (inactive && column != nameof(ParticipantRow.SlotType)))
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
            if ((participant.SlotType == 0 && column == nameof(ParticipantRow.Difficulty)) || (participant.SlotType is 2 or 3 && column != nameof(ParticipantRow.SlotType))) e.Cancel = true;
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != nameof(ParticipantRow.SlotType)) return;
            ParticipantRow changed = participants[e.RowIndex];
            if (changed.SlotType == 0)
                foreach (ParticipantRow other in participants.Where((_, index) => index != e.RowIndex && _.SlotType == 0)) other.SlotType = 2;
            changed.Name = changed.SlotType switch { 0 => config.PlayerName, 1 => $"电脑 {e.RowIndex}", 2 => "开放", _ => "关闭" };
            if (changed.SlotType == 1)
            {
                changed.Country = GameData.Countries[e.RowIndex % GameData.Countries.Length].Value;
                changed.Color = GameData.Colors[e.RowIndex % GameData.Colors.Length].Value;
                changed.Team = 1;
                changed.Difficulty = 0;
                changed.Start = e.RowIndex + 1;
            }
            grid.Refresh();
        };
        grid.Columns.Add(ChoiceColumn(nameof(ParticipantRow.SlotType), "位置状态", GameData.SlotTypes, 105));
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(ParticipantRow.Name), DataPropertyName = nameof(ParticipantRow.Name), HeaderText = "名称", FillWeight = 105 });
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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 12), ColumnCount = 1, RowCount = 6 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleBlock = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        titleBlock.Controls.Add(new Label { Text = "红色警戒 2 / 尤里的复仇", AutoSize = true, Font = new Font(Font.FontFamily, 13f, FontStyle.Bold) });
        titleBlock.Controls.Add(new Label { Text = "选择战场 → 设置参战方 → 调整规则 → 启动游戏", AutoSize = true, ForeColor = Color.DimGray });
        var advanced = new Panel { Dock = DockStyle.Fill, AutoSize = true, Visible = false, Padding = new Padding(0, 4, 0, 8) };
        var paths = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 1 };
        paths.Controls.Add(PathRow("当前游戏目录", runtimePath, () => { ReloadMaps(); ReloadSaves(); }));
        advanced.Controls.Add(paths);
        Button advancedButton = new() { Text = "游戏目录设置 ▾", AutoSize = true, Anchor = AnchorStyles.Right };
        advancedButton.Click += (_, _) => { advanced.Visible = !advanced.Visible; advancedButton.Text = advanced.Visible ? "收起目录设置 ▴" : "游戏目录设置 ▾"; };
        heading.Controls.Add(titleBlock, 0, 0); heading.Controls.Add(advancedButton, 1, 0);
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(advanced, 0, 1);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        var battlefieldBox = new GroupBox { Text = "1  选择战场", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 12), Margin = new Padding(0, 0, 6, 0) };
        var battlefield = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        battlefield.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); battlefield.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        battlefield.RowStyles.Add(new RowStyle(SizeType.AutoSize)); battlefield.RowStyles.Add(new RowStyle(SizeType.AutoSize)); battlefield.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddSetting(battlefield, 0, 0, "游戏模式", gameMode);
        AddSetting(battlefield, 0, 1, "地图", maps);
        var previewBox = new GroupBox { Text = "地图缩略图（点击编号选择玩家出生点）", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(3, 7, 3, 3) };
        previewBox.Controls.Add(mapPreview);
        battlefield.Controls.Add(previewBox, 0, 2); battlefield.SetColumnSpan(previewBox, 2);
        battlefieldBox.Controls.Add(battlefield);
        content.Controls.Add(battlefieldBox, 0, 0);

        var participantsBox = new GroupBox { Text = "2  游戏位置（由地图容量决定）", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 12), Margin = new Padding(6, 0, 0, 0) };
        participantsBox.Controls.Add(grid);
        content.Controls.Add(participantsBox, 1, 0);
        root.Controls.Add(content, 0, 2);

        var mapVisibilityTip = new ToolTip();
        mapVisibilityTip.SetToolTip(revealAllMap, "开局揭示整张地图，移除初始黑幕。");

        var rulesBox = new GroupBox { Text = "3  游戏规则与显示", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var rules = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        rules.Controls.AddRange([new Label { Text = "分辨率", AutoSize = true, Padding = new Padding(0, 6, 2, 0) }, resolution,
            new Label { Text = "游戏速度", AutoSize = true, Padding = new Padding(12, 6, 2, 0) }, gameSpeed,
            new Label { Text = "初始资金", AutoSize = true, Padding = new Padding(12, 6, 2, 0) }, credits, crates, superWeapons, shortGame, revealAllMap]);
        rulesBox.Controls.Add(rules);
        root.Controls.Add(rulesBox, 0, 3);

        var savesBox = new GroupBox { Text = "4  读取存档（可选）", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10) };
        var saves = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4 };
        saves.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); saves.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); saves.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); saves.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button refreshSaves = new() { Text = "刷新存档", AutoSize = true }; refreshSaves.Click += (_, _) => ReloadSaves();
        Button loadSave = new() { Text = "加载存档", AutoSize = true }; loadSave.Click += (_, _) => LoadSelectedSave();
        savedGames.Dock = DockStyle.Fill;
        saves.Controls.Add(new Label { Text = "存档", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0); saves.Controls.Add(savedGames, 1, 0); saves.Controls.Add(refreshSaves, 2, 0); saves.Controls.Add(loadSave, 3, 0);
        savesBox.Controls.Add(saves);
        root.Controls.Add(savesBox, 0, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button launch = new() { Text = "启动新游戏", AutoSize = true, Height = 38, Padding = new Padding(16, 0, 16, 0), BackColor = Color.FromArgb(164, 38, 44), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; launch.FlatAppearance.BorderSize = 0; launch.Click += (_, _) => Launch();
        Button lan = new() { Text = "局域网联机", AutoSize = true, Height = 38, Padding = new Padding(14, 0, 14, 0) }; lan.Click += (_, _) => OpenLanLobby();
        Button prepare = new() { Text = "仅保存配置", AutoSize = true, Height = 36 }; prepare.Click += (_, _) => Generate(false);
        actions.Controls.AddRange([launch, lan, prepare, status]);
        root.Controls.Add(actions, 0, 5);
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
                SlotType = 1,
                Name = $"电脑 {index}",
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
    }
}
