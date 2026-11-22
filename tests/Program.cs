using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ActFloorCustomizer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves.Runs;
using System.Text.Json;
using MegaCrit.Sts2.Core.Unlocks;

AssemblyLoadContext.Default.Resolving += (_, name) => {
    string path = Path.Combine(@"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64", name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
RunTests();

[MethodImpl(MethodImplOptions.NoInlining)]
static void RunTests()
{
    // Emulate a game where room-generation code is compiled before mod loading.
    RuntimeHelpers.PrepareMethod(typeof(ActModel).GetMethod("GenerateRooms", new[] { typeof(Rng), typeof(UnlockState), typeof(bool), typeof(bool) })!.MethodHandle);

    ModEntry.LoadAndPatch();
    ConfigHarness.Check();
    var managerState = typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!;
    managerState.SetValue(null, Enum.Parse(managerState.PropertyType, "Initialized"));
    ModelDb.Init();
    MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
    new HarmonyLib.Harmony("smoke.headless.logging").Patch(
        HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache), "Init"),
        transpiler: new HarmonyLib.HarmonyMethod(typeof(HeadlessLogging), nameof(HeadlessLogging.Transpiler)));
    MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init();
    ModelDb.InitIds();
    Console.WriteLine("Patches loaded; creating act models.");
    ActModel[] acts = [ModelDb.Act<Overgrowth>(), ModelDb.Act<Underdocks>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()];
    int checks = 0;
    foreach (var act in acts)
    {
        int vanilla = act.Index switch { 0 => 15, 1 => 14, 2 => 13, _ => throw new Exception() };
        for (int extra = 0; extra <= 30; extra++)
        {
            ModEntry.Settings.Act1ExtraFloors = extra;
            ModEntry.Settings.Act2ExtraFloors = (extra + 7) % 31;
            ModEntry.Settings.Act3ExtraFloors = (extra + 19) % 31;
            int expected = vanilla + ModEntry.Settings.ForAct(act.Index);
            Require(act.GetNumberOfRooms(false) == expected, "room count");
            Require(act.GetNumberOfRooms(true) == expected - 1, "multiplayer count");
            Require(act.GetNumberOfFloors(false) == expected + 2, "floor count");
            if (!new[] { 0, 1, 5, 10, 15, 20, 30 }.Contains(extra)) continue;
            for (ulong seed = 0; seed < 2; seed++)
            foreach (bool multiplayer in new[] { false, true })
            {
                int target = expected - (multiplayer ? 1 : 0);
                Console.WriteLine($"Checking {act.GetType().Name}: setting {extra}, seed {seed}");
                var map = new StandardActMap(new Rng(seed), act, multiplayer, false, seed % 2 == 0);
                Require(map.GetRowCount() == target + 1, "map height");
                Require(map.BossMapPoint.coord.row == target + 1, "boss row");
                var counts = (MapPointTypeCounts)HarmonyLib.AccessTools.Field(typeof(StandardActMap), "_pointTypeCounts").GetValue(map)!;
                var baseline = act.GetMapPointTypes(new Rng(seed));
                double ratio = (double)target / (vanilla - (multiplayer ? 1 : 0));
                int Scale(int n) => (int)Math.Round(n * ratio, MidpointRounding.AwayFromZero);
                Require(counts.NumOfUnknowns == Scale(baseline.NumOfUnknowns), "unknown scaling");
                Require(counts.NumOfRests == Scale(baseline.NumOfRests), "rest scaling");
                Require(counts.NumOfShops == Scale(baseline.NumOfShops), "shop scaling");
                Require(counts.NumOfElites == Math.Min(14, Scale(baseline.NumOfElites)), "elite scaling");
                var peerMap = new StandardActMap(new Rng(seed), act, multiplayer, false, seed % 2 == 0);
                Require(Signature(map) == Signature(peerMap), "peer determinism");
                for (int row = 1; row <= target; row++)
                    Require(map.GetPointsInRow(row).Any(), "empty row");
                foreach (var point in map.GetAllMapPoints())
                    Require(ReachesBoss(point, map.BossMapPoint), "dead-end path");
                string json = JsonSerializer.Serialize(SerializableActMap.FromActMap(map));
                var saved = new SavedActMap(JsonSerializer.Deserialize<SerializableActMap>(json)!);
                Require(Signature(map) == Signature(saved), "saved map topology changed");
                var mutable = (ActModel)act.ToMutable();
                mutable.GenerateRooms(new Rng(seed), UnlockState.all, multiplayer, false);
                Require(mutable.GetRoomsDebug().normalEncounters.Count == target, "encounter pool mismatch");
                RunSettings.Bind(mutable, ModEntry.Settings.ForAct(act.Index));
                int frozen = mutable.GetNumberOfRooms(multiplayer);
                int oldFirst = ModEntry.Settings.Act1ExtraFloors;
                ModEntry.Settings.Act1ExtraFloors = 30;
                Require(mutable.GetNumberOfRooms(multiplayer) == frozen, "active run settings changed");
                var restoredAct = ActModel.FromSave(mutable.ToSave());
                RunSettings.BindLoadedAct(restoredAct, multiplayer);
                Require(restoredAct.GetNumberOfRooms(multiplayer) == frozen, "saved run adopted new menu defaults");
                ModEntry.Settings.Act1ExtraFloors = oldFirst;
                checks++;
            }
        }
    }
    Console.WriteLine($"PASS: {checks} cases across all four acts; 0-30 count settings; single/multiplayer maps, scaled room budgets, matching encounter pools after pre-JIT, peer determinism, boss placement, reachability and JSON save/load.");
}

static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
static bool ReachesBoss(MapPoint point, MapPoint boss)
{
    var seen = new HashSet<MapPoint>();
    var queue = new Queue<MapPoint>();
    queue.Enqueue(point);
    while (queue.TryDequeue(out var current))
    {
        if (current == boss) return true;
        if (!seen.Add(current)) continue;
        foreach (var child in current.Children) queue.Enqueue(child);
    }
    return false;
}
static string Signature(ActMap map) => string.Join(";", map.GetAllMapPoints()
    .Append(map.StartingMapPoint).Append(map.BossMapPoint)
    .Concat(map.SecondBossMapPoint is { } second ? new[] { second } : Array.Empty<MapPoint>())
    .OrderBy(p => p.coord.row).ThenBy(p => p.coord.col)
    .Select(p => $"{p.coord}:{p.PointType}:" + string.Join(",", p.Children.Select(c => c.coord.ToString()).Order())));

static class HeadlessLogging
{
    public static void Info(string message, int skipFrames) { }
    public static IEnumerable<HarmonyLib.CodeInstruction> Transpiler(IEnumerable<HarmonyLib.CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType?.FullName == "MegaCrit.Sts2.Core.Logging.Log" && method.Name == "Info")
                instruction.operand = typeof(HeadlessLogging).GetMethod(nameof(Info));
            yield return instruction;
        }
    }
}

static class ConfigHarness
{
    public static void Check()
    {
        foreach (string key in new[] { "Name", "NextRun", "Act1", "Act2", "Act3", "ExtraHint", "Coop", "CurrentRun" })
        {
            string english = InGameConfig.Translate(key, "eng");
            string chinese = InGameConfig.Translate(key, "zhs");
            if (english == key || chinese == key || english == chinese) throw new Exception("Missing translation: " + key);
        }
        foreach (string name in new[] { "Act1ExtraFloors", "Act2ExtraFloors", "Act3ExtraFloors" })
        {
            var property = typeof(FloorConfig).GetProperty(name)!;
            var slider = property.GetCustomAttribute<BaseLib.Config.ConfigSliderAttribute>()!;
            if (slider.Min != 0 || slider.Max != 30 || slider.Step != 1 || slider.Format != "{0:0}") throw new Exception("BaseLib slider metadata");
            property.SetValue(null, 99);
            if ((int)property.GetValue(null)! != 30) throw new Exception("Slider clamping");
            property.SetValue(null, 5);
        }
        if (!typeof(BaseLib.Config.SimpleModConfig).IsAssignableFrom(typeof(FloorConfig))) throw new Exception("Wrong config backend");
        if (typeof(ModEntry).Assembly.GetReferencedAssemblies().Any(a => a.Name == "BonModConfig")) throw new Exception("Old dependency remains");
        Console.WriteLine("PASS: BaseLib config type, slider metadata, setters, clamping, translations; no BonModConfig assembly dependency.");
    }
}

