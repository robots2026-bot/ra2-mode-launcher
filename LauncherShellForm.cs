namespace Ra2ModeLauncher;

internal sealed class LauncherShellForm : Form
{
    private readonly SplitContainer workspace = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
    private readonly HomeForm lobbyPanel = new();
    private readonly Panel roomHost = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 243, 246) };
    private MainForm? roomPanel;

    public LauncherShellForm()
    {
        StartupTrace.Mark("shell constructor entered");
        Text = "红色警戒 2 / 尤里的复仇启动器";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 247, 249);
        AutoScaleMode = AutoScaleMode.Dpi;
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Width = Math.Clamp((int)(area.Width * 0.90), 1380, 1800);
        Height = Math.Clamp((int)(area.Height * 0.88), 760, 950);
        MinimumSize = new Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(workspace);
        workspace.Panel1.Padding = new Padding(0, 0, 3, 0);
        workspace.Panel2.Padding = new Padding(3, 0, 0, 0);
        workspace.Panel2.Controls.Add(roomHost);
        AttachEmbeddedForm(lobbyPanel, workspace.Panel1);
        ShowRoomPlaceholder();
        lobbyPanel.OpenRoomRequested += ShowRoom;
        Load += (_, _) => workspace.SplitterDistance = Math.Clamp((int)(ClientSize.Width * 0.34), 460, 580);
        FormClosing += (_, e) => StartupTrace.Mark($"shell closing reason={e.CloseReason}");
        FormClosed += (_, _) =>
        {
            StartupTrace.Mark("shell closed");
            roomPanel?.Dispose();
            if (!lobbyPanel.IsDisposed) lobbyPanel.Dispose();
        };
        StartupTrace.Mark("shell constructor finished");
    }

    private void ShowRoom(RoomEntry entry)
    {
        roomPanel?.Dispose();
        roomHost.Controls.Clear();
        roomPanel = new MainForm(entry);
        roomPanel.ReturnHomeRequested += RequestLeaveRoom;
        AttachEmbeddedForm(roomPanel, roomHost);
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
            roomPanel.Dispose();
            roomPanel = null;
        }
        roomHost.Controls.Clear();
        ShowRoomPlaceholder();
        lobbyPanel.RefreshAfterRoom();
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
        roomHost.Controls.Add(message);
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
