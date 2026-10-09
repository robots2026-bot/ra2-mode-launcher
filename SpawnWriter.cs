using System.Text;

namespace Ra2ModeLauncher;

internal sealed record LaunchOptions(bool Ra2Mode, string PlayerName, MapInfo Map, Choice Country, Choice Color, int Team, int Start, int Credits, int GameSpeed, bool Crates, bool SuperWeapons, bool ShortGame, bool RevealAllMap, IReadOnlyList<ParticipantRow> Ais);

internal static class SpawnWriter
{
    public static void Write(string runtimePath, LaunchOptions options)
    {
        Directory.CreateDirectory(runtimePath);
        string spawnMapPath = Path.Combine(runtimePath, "spawnmap.ini");
        File.Copy(options.Map.Path, spawnMapPath, true);
        MapPatcher.AddChronoLegionnaireWallPassThrough(spawnMapPath);
        MapPatcher.SetProductionQueueLimit(spawnMapPath);
        if (options.RevealAllMap) MapPatcher.AddRevealAllTrigger(spawnMapPath);

        var sb = new StringBuilder();
        Section(sb, "Settings",
            ("Name", Sanitize(options.PlayerName)), ("Scenario", "spawnmap.ini"), ("UIGameMode", "RA2 Classic"), ("UIMapName", Sanitize(options.Map.Name)),
            ("PlayerCount", "1"), ("Side", options.Country.Value.ToString()), ("Color", options.Color.Value.ToString()), ("AIPlayers", options.Ais.Count.ToString()),
            ("Seed", Random.Shared.Next(1, int.MaxValue).ToString()), ("Ra2Mode", Bool(options.Ra2Mode)), ("Bases", "true"), ("Credits", options.Credits.ToString()),
            ("Crates", Bool(options.Crates)), ("Superweapons", Bool(options.SuperWeapons)), ("MCVRedeploy", "true"), ("ShortGame", Bool(options.ShortGame)),
            ("TechLevel", "10"), ("GameSpeed", options.GameSpeed.ToString()), ("LauncherCncPacing", Bool(CncSpeedComponent.IsInstalled(runtimePath))), ("LauncherLiveSpeed", Bool(CncSpeedComponent.IsInstalled(runtimePath))), ("DisableGameSpeed", Bool(CncSpeedComponent.IsInstalled(runtimePath))), ("UnitCount", "0"), ("BridgeDestroy", "true"), ("AlliesAllowed", "false"), ("ForceMultiplayer", "false"),
            ("AutoSaveCount", "0"), ("AutoSaveInterval", "0"));

        var countries = new List<(string, string)>();
        var colors = new List<(string, string)>();
        var handicaps = new List<(string, string)>();
        var starts = new List<(string, string)> { ("Multi1", (options.Start - 1).ToString()) };

        for (int i = 0; i < options.Ais.Count; i++)
        {
            string multi = $"Multi{i + 2}";
            ParticipantRow ai = options.Ais[i];
            countries.Add((multi, ai.Country.ToString()));
            colors.Add((multi, ai.Color.ToString()));
            handicaps.Add((multi, ai.Difficulty.ToString()));
            starts.Add((multi, (ai.Start - 1).ToString()));
        }

        Section(sb, "HouseCountries", countries.ToArray());
        Section(sb, "HouseColors", colors.ToArray());
        Section(sb, "HouseHandicaps", handicaps.ToArray());
        Section(sb, "SpawnLocations", starts.ToArray());

        var teams = new Dictionary<int, List<int>>();
        AddTeam(teams, options.Team, 1);
        for (int i = 0; i < options.Ais.Count; i++)
            AddTeam(teams, options.Ais[i].Team, i + 2);

        foreach (List<int> members in teams.Values.Where(m => m.Count > 1))
        {
            foreach (int member in members)
            {
                int allyIndex = 1;
                var allies = new List<(string, string)>();
                foreach (int ally in members.Where(id => id != member))
                    allies.Add(($"HouseAlly{AllyName(allyIndex++)}", (ally - 1).ToString()));
                Section(sb, $"Multi{member}_Alliances", allies.ToArray());
            }
        }

        File.WriteAllText(Path.Combine(runtimePath, "spawn.ini"), sb.ToString(), new UTF8Encoding(false));
        string savedGames = Path.Combine(runtimePath, "Saved Games");
        Directory.CreateDirectory(savedGames);
        File.Copy(Path.Combine(runtimePath, "spawn.ini"), Path.Combine(savedGames, "spawnSG.ini"), true);
    }

    public static void WriteLoadSave(string runtimePath, SaveInfo save)
    {
        string savedGames = Path.Combine(runtimePath, "Saved Games");
        if (!File.Exists(save.Path) || !string.Equals(Path.GetDirectoryName(save.Path), savedGames, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只能加载独立运行目录 Saved Games 中的存档。");

        var sb = new StringBuilder();
        Section(sb, "Settings", ("LoadSaveGame", "yes"), ("SaveGameName", Path.GetFileName(save.Path)), ("LauncherCncPacing", Bool(CncSpeedComponent.IsInstalled(runtimePath))), ("LauncherLiveSpeed", Bool(CncSpeedComponent.IsInstalled(runtimePath))), ("ForceMultiplayer", "false"));
        File.WriteAllText(Path.Combine(runtimePath, "spawn.ini"), sb.ToString(), new UTF8Encoding(false));
    }

    public static void WriteLan(string runtimePath, LanLaunchPackage package, Guid localPlayerId, string hostAddress)
    {
        LanGameSetup options = package.Setup;
        options.ValidateSpeed();
        if (LanGameSetup.Hash(options.MapData) != options.MapHash) throw new InvalidDataException("收到的地图内容与房主哈希不一致。");
        if (package.Players.Count < 2 || package.Players.Count > options.Slots.Count) throw new InvalidDataException("联机玩家数量不合法。");
        int localIndex = package.Players.FindIndex(p => p.Id == localPlayerId);
        if (localIndex < 0) throw new InvalidDataException("房间启动清单中没有本机玩家。");

        Directory.CreateDirectory(runtimePath);
        string spawnMapPath = Path.Combine(runtimePath, "spawnmap.ini");
        File.WriteAllBytes(spawnMapPath, options.MapData);
        MapPatcher.AddChronoLegionnaireWallPassThrough(spawnMapPath);
        MapPatcher.SetProductionQueueLimit(spawnMapPath);
        if (options.RevealAllMap) MapPatcher.AddRevealAllTrigger(spawnMapPath);

        int humanCount = package.Players.Count;
        if (package.Players.Select(player => player.SlotIndex).Distinct().Count() != humanCount || package.Players.Any(player => player.SlotIndex < 0 || player.SlotIndex >= options.Slots.Count || options.Slots[player.SlotIndex].Closed || options.Slots[player.SlotIndex].Computer))
            throw new InvalidDataException("联机玩家的位置编号无效。");
        List<LanSlot> humanSlots = package.Players.Select(player => options.Slots[player.SlotIndex]).ToList();
        List<LanSlot> aiSlots = options.Slots.Where(slot => slot.Computer && !slot.Closed).ToList();
        LanPlayer localPlayer = package.Players[localIndex];
        LanSlot localSlot = humanSlots[localIndex];
        var sb = new StringBuilder();
        Section(sb, "Settings",
            ("Name", Sanitize(localPlayer.Name)), ("Scenario", "spawnmap.ini"), ("UIGameMode", "RA2 Classic"), ("UIMapName", Sanitize(options.MapName)),
            ("PlayerCount", humanCount.ToString()), ("Side", localSlot.Country.ToString()), ("Color", localSlot.Color.ToString()), ("AIPlayers", aiSlots.Count.ToString()),
            ("Seed", package.GameId.ToString()), ("GameID", package.GameId.ToString()), ("Port", LanLobbyHost.GamePort.ToString()), ("Host", Bool(localPlayer.IsHost)),
            ("Ra2Mode", Bool(options.Ra2Mode)), ("Bases", "true"), ("Credits", options.Credits.ToString()), ("Crates", Bool(options.Crates)),
            ("Superweapons", Bool(options.SuperWeapons)), ("MCVRedeploy", "true"), ("ShortGame", Bool(options.ShortGame)), ("TechLevel", "10"),
            ("GameSpeed", options.GameSpeed.ToString()), ("LauncherCncPacing", Bool(options.GameSpeed == 0 && CncSpeedComponent.IsInstalled(runtimePath))), ("LauncherLiveSpeed", "false"), ("DisableGameSpeed", "true"), ("UnitCount", "0"), ("BridgeDestroy", "true"), ("AlliesAllowed", "false"), ("ForceMultiplayer", "true"),
            ("AutoSaveCount", "0"), ("AutoSaveInterval", "0"));

        int other = 1;
        for (int i = 0; i < humanCount; i++)
        {
            if (i == localIndex) continue;
            LanPlayer player = package.Players[i];
            LanSlot slot = humanSlots[i];
            string address = player.IsHost && (string.IsNullOrWhiteSpace(player.Address) || IsLoopback(player.Address)) ? hostAddress : player.Address;
            Section(sb, $"Other{other++}", ("Name", Sanitize(player.Name)), ("Side", slot.Country.ToString()), ("Color", slot.Color.ToString()), ("Ip", address), ("Port", LanLobbyHost.GamePort.ToString()));
        }

        var countries = new List<(string, string)>();
        var colors = new List<(string, string)>();
        var handicaps = new List<(string, string)>();
        for (int i = 0; i < aiSlots.Count; i++)
        {
            string multi = $"Multi{humanCount + i + 1}";
            countries.Add((multi, aiSlots[i].Country.ToString()));
            colors.Add((multi, aiSlots[i].Color.ToString()));
            handicaps.Add((multi, aiSlots[i].Difficulty.ToString()));
        }
        Section(sb, "HouseCountries", countries.ToArray());
        Section(sb, "HouseColors", colors.ToArray());
        Section(sb, "HouseHandicaps", handicaps.ToArray());

        Dictionary<int, int> humanMulti = package.Players.Select((_, index) => (index, color: humanSlots[index].Color)).OrderBy(x => x.color).Select((x, multi) => (x.index, multi: multi + 1)).ToDictionary(x => x.index, x => x.multi);
        var starts = new List<(string, string)>();
        for (int i = 0; i < humanCount; i++) starts.Add(($"Multi{humanMulti[i]}", (humanSlots[i].Start - 1).ToString()));
        for (int i = 0; i < aiSlots.Count; i++) starts.Add(($"Multi{humanCount + i + 1}", (aiSlots[i].Start - 1).ToString()));
        Section(sb, "SpawnLocations", starts.ToArray());

        var teams = new Dictionary<int, List<int>>();
        for (int i = 0; i < humanCount; i++) AddTeam(teams, humanSlots[i].Team, humanMulti[i]);
        for (int i = 0; i < aiSlots.Count; i++) AddTeam(teams, aiSlots[i].Team, humanCount + i + 1);
        foreach (List<int> members in teams.Values.Where(m => m.Count > 1))
        {
            foreach (int member in members)
            {
                int allyIndex = 1;
                var allies = new List<(string, string)>();
                foreach (int ally in members.Where(id => id != member)) allies.Add(($"HouseAlly{AllyName(allyIndex++)}", (ally - 1).ToString()));
                Section(sb, $"Multi{member}_Alliances", allies.ToArray());
            }
        }

        string spawnPath = Path.Combine(runtimePath, "spawn.ini");
        File.WriteAllText(spawnPath, sb.ToString(), new UTF8Encoding(false));
        string savedGames = Path.Combine(runtimePath, "Saved Games");
        Directory.CreateDirectory(savedGames);
        File.Copy(spawnPath, Path.Combine(savedGames, "spawnSG.ini"), true);
    }

    private static void AddTeam(Dictionary<int, List<int>> teams, int team, int multi)
    {
        if (team <= 0) return;
        if (!teams.TryGetValue(team, out List<int>? members)) teams[team] = members = [];
        members.Add(multi);
    }

    private static void Section(StringBuilder sb, string name, params (string Key, string Value)[] values)
    {
        sb.Append('[').Append(name).AppendLine("]");
        if (name == "Settings") sb.AppendLine("QuickExit=true");
        foreach ((string key, string value) in values) sb.Append(key).Append('=').AppendLine(value);
        sb.AppendLine();
    }

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Sanitize(string value) => value.Replace("\r", " ").Replace("\n", " ").Replace("=", "-").Trim();
    private static bool IsLoopback(string value) => System.Net.IPAddress.TryParse(value, out System.Net.IPAddress? address) && System.Net.IPAddress.IsLoopback(address);
    private static string AllyName(int index) => index switch { 1 => "One", 2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six", 7 => "Seven", _ => index.ToString() };
}
