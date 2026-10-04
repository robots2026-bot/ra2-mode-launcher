namespace Ra2ModeLauncher;

internal sealed class LauncherShellForm : Form
{
    private readonly SplitContainer workspace = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
    private readonly HomeForm lobbyPanel = new();
    private readonly Panel roomHost = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 243, 246) };
    private MainForm? roomPanel;
    private readonly MapPreviewControl sharedPreview = new() { Dock = DockStyle.Fill, AllowStartSelection = false };

    public LauncherShellForm()
    {
        StartupTrace.Mark("shell constructor entered");
        Text = "红色警戒 2 / 尤里的复仇启动器";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 247, 249);
        AutoScaleMode = AutoScaleMode.Dpi;
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Width = Math.Min(area.Width, Math.Clamp((int)(area.Width * 0.94), 1280, 1800));
        Height = Math.Min(area.Height, Math.Clamp((int)(area.Height * 0.90), 720, 950));
        MinimumSize = new Size(Math.Min(area.Width, 1280), Math.Min(area.Height, 720));
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(workspace);
        workspace.Panel1.Padding = new Padding(0, 0, 3, 0);
        workspace.Panel2.Padding = new Padding(3, 0, 0, 0);
        workspace.Panel2.Controls.Add(roomHost);
        lobbyPanel.PreviewRequested += map => { if (roomPanel is null) { sharedPreview.Map = map; sharedPreview.SetPlayers([]); } };
        AttachEmbeddedForm(lobbyPanel, workspace.Panel1);
        ShowRoomPlaceholder();
        lobbyPanel.OpenRoomRequested += ShowRoom;
        Load += (_, _) => { workspace.SplitterDistance = 460; workspace.Panel1MinSize = 440; workspace.Panel2MinSize = Math.Min(700, ClientSize.Width - 466); };
        FormClosing += (_, e) => StartupTrace.Mark($"shell closing reason={e.CloseReason}");
        FormClosed += (_, _) =>
        {
            StartupTrace.Mark("shell closed");
            sharedPreview.Parent?.Controls.Remove(sharedPreview);
            roomPanel?.Dispose();
            sharedPreview.Dispose();
            if (!lobbyPanel.IsDisposed) lobbyPanel.Dispose();
        };
        StartupTrace.Mark("shell constructor finished");
    }

    private void ShowRoom(RoomEntry entry)
    {
        if (roomPanel is not null && entry.Mode == RoomEntryMode.Join && entry.HostAddress == LanNetworkAddress.GetPreferredIPv4()) { MessageBox.Show(this, "已经在本机房间中，无需再次加入。"); return; }
        if (roomPanel is not null && MessageBox.Show(this, "切换房间会离开当前房间，是否继续？", "切换房间", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        sharedPreview.Parent?.Controls.Remove(sharedPreview);
        roomPanel?.Dispose();
        roomHost.Controls.Clear();
        roomPanel = new MainForm(entry, sharedPreview);
        roomPanel.ReturnHomeRequested += RequestLeaveRoom;
        AttachEmbeddedForm(roomPanel, roomHost);
        lobbyPanel.SetRoomActive(true);
        Text = entry.Mode == RoomEntryMode.Create ? $"红色警戒 2——{entry.RoomName}" : $"红色警戒 2——正在加入 {entry.HostAddress}";
    }

    private void RequestLeaveRoom()
    {
        if (!IsDisposed && IsHandleCreated) BeginInvoke(LeaveRoom);
    }

    private void LeaveRoom()
    {
        if (roomPanel is not null)
        {
            roomPanel.ReturnHomeRequested -= RequestLeaveRoom;
            sharedPreview.Parent?.Controls.Remove(sharedPreview);
            roomPanel.Dispose();
            roomPanel = null;
        }
        roomHost.Controls.Clear();
        ShowRoomPlaceholder();
        lobbyPanel.RefreshAfterRoom();
        lobbyPanel.SetRoomActive(false);
        sharedPreview.AllowStartSelection = false;
        sharedPreview.SetPlayers([]);
        lobbyPanel.RefreshPreview();
        Text = "红色警戒 2 / 尤里的复仇启动器";
    }

    private void ShowRoomPlaceholder()
    {
        var message = new Label
        {
            Dock = DockStyle.Fill,
            Text = "当前未进入房间\r\n\r\n请在左侧创建房间，或选择一个局域网对局加入。",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Font = new Font(Font.FontFamily, 12f)
        };
        var idle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        idle.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); idle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        idle.Controls.Add(message, 0, 0); idle.Controls.Add(sharedPreview, 0, 1);
        roomHost.Controls.Add(idle);
    }

    private static void AttachEmbeddedForm(Form form, Control host)
    {
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.MinimumSize = Size.Empty;
        form.Dock = DockStyle.Fill;
        host.Controls.Add(form);
        form.Show();
    }
}
