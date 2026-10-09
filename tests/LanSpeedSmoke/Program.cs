using Ra2ModeLauncher;

static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
static void Reject(Action action, string name)
{
    try { action(); }
    catch (InvalidDataException) { Console.WriteLine("PASS " + name); return; }
    throw new Exception(name);
}
string root = Path.Combine(Path.GetTempPath(), "ra2-lan-speed-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    byte[] map = System.Text.Encoding.UTF8.GetBytes("[Basic]\nName=Speed test\n");
    Guid host = Guid.NewGuid(), guest = Guid.NewGuid();
    var players = new List<LanPlayer> { new(host, "host", "127.0.0.1", true, true, 0), new(guest, "guest", "127.0.0.2", true, false, 1) };
    foreach (var speed in GameData.GameSpeeds.Concat(new[] { new GameSpeedChoice("custom 37", 0, 37, true), new GameSpeedChoice("custom 1000", 0, 1000, true) }))
    {
        var setup = new LanGameSetup(true, "test", "test.map", map, LanGameSetup.Hash(map), "fixture", 10000, speed.GameSpeed, speed.MaxGameTicks, false, true, true, false, [new(0, 0, 0, 0, 1, false), new(1, 1, 0, 0, 2, false)]);
        var package = new LanLaunchPackage(123, setup, players);
        foreach (Guid local in new[] { host, guest })
        {
            File.WriteAllText(Path.Combine(root, "ddraw.ini"), "[ddraw]\nmaxgameticks=25\nmaxfps=144\n[gamemd-spawn]\nmaxgameticks=30\nlimiter_type=4\nmaxfps=60\n[other]\nmaxgameticks=9\n");
            IniFileEditor.ConfigureCncDdraw(root, setup.EffectiveMaxGameTicks);
            SpawnWriter.WriteLan(root, package, local, "127.0.0.1");
            IniFileEditor.VerifyLanSpeed(root, setup);
            string ddraw = File.ReadAllText(Path.Combine(root, "ddraw.ini"));
            Check(ddraw.Contains("maxfps=144") && ddraw.Contains("maxfps=60") && ddraw.Contains("maxgameticks=9"), "preserve rendering and unrelated profile: " + speed.Name);
            string spawnPath = Path.Combine(root, "spawn.ini");
            File.WriteAllText(spawnPath, File.ReadAllText(spawnPath).Replace("DisableGameSpeed=true", "DisableGameSpeed=false"));
            Reject(() => IniFileEditor.VerifyLanSpeed(root, setup), "reject unlocked game menu");
            SpawnWriter.WriteLan(root, package, local, "127.0.0.1");
            File.WriteAllText(Path.Combine(root, "ddraw.ini"), ddraw.Replace("[gamemd-spawn]", "[wrong-profile]"));
            Reject(() => IniFileEditor.VerifyLanSpeed(root, setup), "reject missing executable speed override");
        }
    }
    foreach (string name in new[] { "gamemd-spawn.exe", "Ares.dll", "Phobos.dll", "CnCNet-Spawner.dll", "ra2mode.mix", "ddraw.dll" }) File.WriteAllText(Path.Combine(root, name), "fixture");
    string original = LanCompatibility.ComputeComponentHash(root);
    File.WriteAllText(Path.Combine(root, "ddraw.dll"), "different version");
    Check(original != LanCompatibility.ComputeComponentHash(root), "cnc-ddraw binary participates in compatibility check");
    File.WriteAllText(Path.Combine(root, "ddraw.ini"), "[ddraw]\n[gamemd-spawn]\n");
    Check(IniFileEditor.ConfigureSinglePlayerSpeed(root, 2, 0) == 2 && File.ReadAllText(Path.Combine(root, "ddraw.ini")).Contains("maxgameticks=-1"), "stock DLL keeps original launch presets and disables extra cap");
    string native = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/cnc-speed/ddraw.dll"));
    if (!File.Exists(native)) throw new Exception("Build actual native component before integration checks");
    string packageDir = Path.Combine(AppContext.BaseDirectory, "native-runtime");
    Directory.CreateDirectory(packageDir);
    File.Copy(native, Path.Combine(packageDir, "ddraw.dll"), true);
    File.Copy(native, Path.Combine(root, "ddraw.dll"), true);
    Check(CncSpeedComponent.IsInstalled(root), "actual built DLL matches packaged controller");
    foreach (var speed in GameData.GameSpeeds)
    {
        Check(IniFileEditor.ConfigureSinglePlayerSpeed(root, speed.GameSpeed, speed.MaxGameTicks) == 0, "managed engine starts unlimited: " + speed.Name);
        int target = GameSpeedTarget.FromLegacy(speed.GameSpeed, speed.MaxGameTicks).CncMaxGameTicks;
        Check(GamePerformanceOverlay.ReadIniInt(Path.Combine(root, "ddraw.ini"), "gamemd-spawn", "maxgameticks", 0) == target, "managed target written: " + speed.Name);
    }
    var singleMap = new MapInfo(Path.Combine(root, "single.map"), "single", 2, []);
    File.WriteAllBytes(singleMap.Path, map);
    SpawnWriter.Write(root, new LaunchOptions(true, "single", singleMap, new("host", 0), new("color", 0), 0, 1, 10000, 0, false, false, true, false, []));
    string singleSpawn = File.ReadAllText(Path.Combine(root, "spawn.ini"));
    Check(singleSpawn.Contains("GameSpeed=0") && singleSpawn.Contains("LauncherCncPacing=true") && singleSpawn.Contains("LauncherLiveSpeed=true") && singleSpawn.Contains("DisableGameSpeed=true"), "single-player opts into actual controller and locks original slider");
    Console.WriteLine("All speed configuration checks passed. No game was started; real two-machine pacing remains unverified.");
}
finally { Directory.Delete(root, true); }
