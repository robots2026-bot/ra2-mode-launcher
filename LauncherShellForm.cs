namespace Ra2ModeLauncher;

internal sealed class LauncherShellForm : Form
{
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    private readonly HomeForm homePage = new();
    private MainForm? roomPage;

    public LauncherShellForm()
    {
        StartupTrace.Mark("shell constructor entered");
        Text = "红色警戒 2 / 尤里的复仇启动器";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(246, 247, 249);
        AutoScaleMode = AutoScaleMode.Dpi;
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Width = Math.Clamp((int)(area.Width * 0.78), 1040, 1280);
        Height = Math.Clamp((int)(area.Height * 0.82), 720, 900);
        MinimumSize = new Size(960, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(pageHost);
        homePage.OpenRoomRequested += ShowRoom;
        FormClosing += (_, e) => StartupTrace.Mark($"shell closing reason={e.CloseReason}");
        FormClosed += (_, _) =>
        {
            StartupTrace.Mark("shell closed");
            roomPage?.Dispose();
            if (!homePage.IsDisposed) homePage.Dispose();
        };
        ShowHome();
        StartupTrace.Mark("shell constructor finished");
    }

    private void ShowRoom(RoomEntry entry)
    {
        roomPage?.Dispose();
        roomPage = new MainForm(entry);
        roomPage.ReturnHomeRequested += RequestShowHome;
        Text = entry.Mode == RoomEntryMode.Create ? $"房间：{entry.RoomName}" : $"正在加入：{entry.HostAddress}";
        ShowPage(roomPage);
    }

    private void ShowHome()
    {
        StartupTrace.Mark("show home requested");
        if (roomPage is not null)
        {
            roomPage.ReturnHomeRequested -= RequestShowHome;
            roomPage.Dispose();
            roomPage = null;
        }
        Text = "红色警戒 2 / 尤里的复仇——局域网大厅";
        homePage.RefreshAfterRoom();
        ShowPage(homePage);
        StartupTrace.Mark("home attached");
    }

    private void RequestShowHome()
    {
        if (!IsDisposed && IsHandleCreated) BeginInvoke(ShowHome);
    }

    private void ShowPage(Form page)
    {
        pageHost.SuspendLayout();
        foreach (Control control in pageHost.Controls.Cast<Control>().ToArray())
        {
            pageHost.Controls.Remove(control);
            control.Hide();
        }
        page.TopLevel = false;
        page.FormBorderStyle = FormBorderStyle.None;
        page.Dock = DockStyle.Fill;
        pageHost.Controls.Add(page);
        page.Show();
        pageHost.ResumeLayout(true);
    }
}
