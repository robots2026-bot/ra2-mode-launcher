using System.Collections;
using System.Reflection;
using System.Windows.Forms;
using Ra2ModeLauncher;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static void Pump() { for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(2); } }

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        string runtime = Path.Combine(AppContext.BaseDirectory, "fixture-runtime");
        Directory.CreateDirectory(runtime);
        foreach (string file in new[] { "Syringe.exe", "gamemd-spawn.exe", "Ares.dll", "Phobos.dll", "CnCNet-Spawner.dll", "ra2mode.mix", "ddraw.dll" }) File.WriteAllText(Path.Combine(runtime, file), "fixture only");
        File.WriteAllText(Path.Combine(runtime, "fixture.map"), "[Basic]\nName=Fixed slot test\nNumberStartingPoints=8\n");
        new LauncherConfig { RuntimePath = runtime }.Save();
        using var preview = new MapPreviewControl();
        using var form = new MainForm(RoomEntry.Create("host", "slot regression"), preview);
        _ = form.Handle; // Create native controls without showing the form or running its auto-connect Shown handler.
        var grid = Field<DataGridView>(form, "grid");
        _ = grid.Handle;
        var rows = Field<IList>(form, "participants");
        Check(rows.Count == 8 && grid.Rows.Count == 8, "8 fixed rows bind to the real DataGridView");
        var setup = (LanGameSetup)Call(form, "BuildRoomSetup", false)!;
        using var host = new LanLobbyHost("fixture", "host", setup, 0, announce: false);
        form.GetType().GetField("roomHost", Private)!.SetValue(form, host);
        host.StateChanged += state => Call(form, "Ui", (Action)(() => Call(form, "ApplyRoomState", state)));
        Call(form, "ApplyRoomState", host.CurrentState);
        Pump();
        var available = (IEnumerable<Choice>)((DataGridViewComboBoxCell)grid.Rows[1].Cells[nameof(ParticipantRow.SlotType)]).DataSource!;
        Check(available.All(choice => choice.Value != 0), "Vacant dropdown does not offer human ownership");

        // Exercise the actual edit/commit path, including a stale invalid human selection.
        grid.CurrentCell = grid.Rows[1].Cells[nameof(ParticipantRow.SlotType)];
        grid.CurrentCell.Value = 0;
        grid.EndEdit();
        Pump();
        Check(((ParticipantRow)rows[1]!).SlotType == 2 && host.CurrentState.Players.Count == 1, "Stale open-to-human edit safely returns to open without recursive publication");
        for (int cycle = 0; cycle < 20; cycle++)
        {
            foreach (int type in new[] { 3, 2, 1, 2 })
            {
                grid.CurrentCell = grid.Rows[2].Cells[nameof(ParticipantRow.SlotType)];
                grid.BeginEdit(true);
                if (grid.EditingControl is DataGridViewComboBoxEditingControl editor) editor.SelectedValue = type;
                else grid.CurrentCell.Value = type;
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                grid.EndEdit();
                Pump();
                Check(((ParticipantRow)rows[2]!).SlotType == type && ((ParticipantRow)rows[2]!).Start == 3 && ((ParticipantRow)rows[3]!).Start == 4 && grid.CurrentCell?.RowIndex == 2, $"Edit cycle {cycle + 1}, type {type}: rows stay fixed");
            }
        }
        var saved = host.CurrentState.Setup.Slots;
        Check(saved.Count == 8 && saved.Select(slot => slot.Start).SequenceEqual(Enumerable.Range(1, 8)), "Close/reopen never changes row order or spawn selection");
        var guestId = Guid.NewGuid();
        var guestSlots = saved.ToList(); guestSlots[6] = guestSlots[6] with { Computer = false, Closed = false };
        var stateWithGuest = host.CurrentState with { Players = [.. host.CurrentState.Players, new LanPlayer(guestId, "guest", "127.0.0.1", false, false, 6)], Setup = setup with { Slots = guestSlots } };
        Call(form, "ApplyRoomState", stateWithGuest);
        Check(Field<List<Guid?>>(form, "rowPlayerIds")[6] == guestId && ((ParticipantRow)rows[6]!).Name == "guest" && ((ParticipantRow)rows[1]!).SlotType == 2, "Non-contiguous guest displays in row 7 instead of moving to row 2");
        var customState = stateWithGuest with { Setup = stateWithGuest.Setup with { GameSpeed = 0, MaxGameTicks = 37 } };
        Call(form, "ApplyRoomState", customState);
        Check(Field<NumericUpDown>(form, "customSpeed").Value == 37 && ((GameSpeedChoice)Field<ComboBox>(form, "gameSpeed").SelectedItem!).Custom, "room custom speed restores exact synchronized value");

        var package = new LanLaunchPackage(1, stateWithGuest.Setup, stateWithGuest.Players);
        SpawnWriter.WriteLan(runtime, package, guestId, "127.0.0.1");
        Check(File.ReadAllText(Path.Combine(runtime, "spawnmap.ini")).Contains("MaximumQueuedObjects=100"), "LAN launch map raises the production queue limit to 100");
        string spawn = File.ReadAllText(Path.Combine(runtime, "spawn.ini"));
        Check(spawn.Contains("Side=" + guestSlots[6].Country) && spawn.Contains("Color=" + guestSlots[6].Color) && spawn.Contains("Multi2=6"), "Game config maps non-contiguous human slot to the correct country, color and spawn");
        Console.WriteLine("All room-slot UI regression checks passed. Fixture files only; no game started.");
    }
}
