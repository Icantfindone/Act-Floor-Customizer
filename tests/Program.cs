using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
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
    LayoutHarness.Check();
    LayoutHarness.CheckTranspiler();
    ConfigHarness.Check();
    NameHarness.Check();
    SyncHarness.Check();
    var managerState = typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!;
    managerState.SetValue(null, Enum.Parse(managerState.PropertyType, "Initialized"));
    ModelDb.Init();
    MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
    new HarmonyLib.Harmony("smoke.headless.logging").Patch(
        HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache), "Init"),
        transpiler: new HarmonyLib.HarmonyMethod(typeof(HeadlessLogging), nameof(HeadlessLogging.Transpiler)));
    MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init();
    ModelDb.InitIds();
    MapCompatHarness.Check();
    RoomMixHarness.Check();
    Console.WriteLine("Patches loaded; creating act models.");
    ActModel[] acts = [ModelDb.Act<Overgrowth>(), ModelDb.Act<Underdocks>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()];
    int checks = 0;
    foreach (var act in acts)
    {
        int vanilla = act.Index switch { 0 => 15, 1 => 14, 2 => 13, _ => throw new Exception() };
        for (int extra = 0; extra <= FloorSettings.MaxFloors; extra++)
        {
            ModEntry.Settings.Act1ExtraFloors = extra;
            ModEntry.Settings.Act2ExtraFloors = (extra + 7) % (FloorSettings.MaxFloors + 1);
            ModEntry.Settings.Act3ExtraFloors = (extra + 19) % (FloorSettings.MaxFloors + 1);
            int expected = vanilla + ModEntry.Settings.ForAct(act.Index);
            Require(act.GetNumberOfRooms(false) == expected, "room count");
            Require(act.GetNumberOfRooms(true) == expected - 1, "multiplayer count");
            Require(act.GetNumberOfFloors(false) == expected + 2, "floor count");
            if (!new[] { 0, 1, 5, 10, 15, 20, 30, 50 }.Contains(extra)) continue;
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
                ModEntry.Settings.Act1ExtraFloors = FloorSettings.MaxFloors;
                Require(mutable.GetNumberOfRooms(multiplayer) == frozen, "active run settings changed");
                var restoredAct = ActModel.FromSave(mutable.ToSave());
                RunSettings.BindLoadedAct(restoredAct, multiplayer);
                Require(restoredAct.GetNumberOfRooms(multiplayer) == frozen, "saved run adopted new menu defaults");
                ModEntry.Settings.Act1ExtraFloors = oldFirst;
                checks++;
            }
        }
    }
    Console.WriteLine($"PASS: {checks} cases across all four acts; 0-50 count settings; single/multiplayer maps, scaled room budgets, matching encounter pools after pre-JIT, peer determinism, boss placement, reachability and JSON save/load.");
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

static class LayoutHarness
{
    public static void Check()
    {
        // These counters prove the transpilers found the real constants in the game assembly.
        if (MapLayout.HeightReplacements != 1) throw new Exception($"map height constant replaced {MapLayout.HeightReplacements} times, expected 1");
        if (MapLayout.BossReplacements != 2) throw new Exception($"boss constants replaced {MapLayout.BossReplacements} times, expected 2");
        if (MapLayout.ScrollReplacements < 3) throw new Exception($"scroll limit constants replaced {MapLayout.ScrollReplacements} times, expected at least 3");

        // At stretch 1 every replacement must reproduce the original constant exactly.
        MapLayout.SetStretch(1f);
        Require(Near(MapLayout.BaseHeight, 2325f), "vanilla height changed");
        Require(Near(MapLayout.ScrollTop, 1800f), "vanilla scroll top changed");
        Require(Near(MapLayout.FixBossY(-1980f), -1980f), "vanilla boss position changed");
        Require(Near(MapLayout.FixSecondBossY(-2280f), -2280f), "vanilla second boss changed");
        Require(Near(MapLayout.FixBossY(-1782f), -1782f), "vanilla two-boss position changed");
        Require(Near(MapLayout.FixSecondBossY(-2052f), -2052f), "vanilla two-boss second position changed");

        // The distance from the boss marker to the last row must not change with stretch:
        // lastRowY = 1280 - BaseHeight * num, and both the last row and the boss move up by
        // exactly the same amount, so the gap the act art relies on stays put.
        var gaps = new (float Stretch, float Num, float Expected)[]
        {
            (1f, 1f, -935f), (1f, 0.9f, -969.5f),
            (2f, 1f, -935f), (2f, 0.9f, -969.5f),
            (3f, 1f, -935f), (3f, 0.9f, -969.5f),
        };
        foreach (var (stretch, num, expected) in gaps)
        {
            MapLayout.SetStretch(stretch);
            float gap = MapLayout.FixBossY(-1980f * num) - (1280f - MapLayout.BaseHeight * num);
            Require(Near(gap, expected), $"boss gap changed at stretch {stretch}, num {num}: {gap} != {expected}");
        }

        // Node pitch: stretch must grow the canvas by exactly one vanilla height per unit.
        MapLayout.SetStretch(2f);
        Require(Near(MapLayout.BaseHeight, 4650f), "stretched height");
        Require(Near(MapLayout.ScrollTop, 4125f), "stretched scroll top");
        Require(Near(MapLayout.FixBossY(-1980f), -4305f), "stretched boss");

        // Ratio maths: extra floors over the act's vanilla row count.
        Require(Near(MapLayout.ComputeStretch(16, 0), 1f), "no extra floors must not stretch");
        Require(Near(MapLayout.ComputeStretch(26, 10), 25f / 15f), "10 extra floors on a 15-row act");
        Require(Near(MapLayout.ComputeStretch(46, 30), 3f), "30 extra floors on a 15-row act");
        Require(Near(MapLayout.ComputeStretch(16, 99), 1f), "degenerate row count must fall back to vanilla");
        Require(Near(MapLayout.ComputeStretch(6, 0), 1f), "short act must not stretch");

        MapLayout.SetStretch(1f);
        Console.WriteLine("PASS: map layout stretch maths, vanilla equivalence, boss gap preservation and patch coverage.");
    }

    public static void CheckTranspiler()
    {
        // Synthetic instruction stream shaped like NMapScreen.SetMap, to pin the rewrite rules.
        var setMap = new List<HarmonyLib.CodeInstruction>
        {
            new(System.Reflection.Emit.OpCodes.Ldc_R4, 2325f),
            new(System.Reflection.Emit.OpCodes.Ldloc_0),
            new(System.Reflection.Emit.OpCodes.Mul),
            new(System.Reflection.Emit.OpCodes.Ldc_R4, -1980f),
            new(System.Reflection.Emit.OpCodes.Ldloc_0),
            new(System.Reflection.Emit.OpCodes.Mul),
            new(System.Reflection.Emit.OpCodes.Ldc_R4, -2280f),
            new(System.Reflection.Emit.OpCodes.Ldloc_0),
            new(System.Reflection.Emit.OpCodes.Mul),
        };
        var rewritten = MapLayout.StretchSetMap(setMap).ToList();
        if (rewritten.Any(i => i.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && (float)i.operand! == 2325f))
            throw new Exception("2325 was not replaced");
        var calls = rewritten.Where(i => i.opcode == System.Reflection.Emit.OpCodes.Call).Select(i => ((MethodInfo)i.operand!).Name).ToList();
        if (calls.Count(n => n == "get_BaseHeight") != 1) throw new Exception("base height call missing");
        if (calls.Count(n => n == "FixBossY") != 1) throw new Exception("boss fix call missing");
        if (calls.Count(n => n == "FixSecondBossY") != 1) throw new Exception("second boss fix call missing");
        if (calls.Count != 3) throw new Exception("unexpected extra calls in SetMap rewrite: " + string.Join(",", calls));

        var scroll = MapLayout.StretchScrollTop(new List<HarmonyLib.CodeInstruction>
        {
            new(System.Reflection.Emit.OpCodes.Ldc_R4, 1800f),
            new(System.Reflection.Emit.OpCodes.Ldc_R4, 1800f),
        }).ToList();
        if (scroll.Count != 2 || scroll.Any(i => i.opcode != System.Reflection.Emit.OpCodes.Call))
            throw new Exception("scroll limit constants were not both replaced");

        Console.WriteLine("PASS: layout transpilers rewrite the expected instruction patterns.");
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}

static class ConfigHarness
{
    public static void Check()
    {
        foreach (string key in new[] { "Name", "Desc", "NextRun", "Act1", "Act2", "Act3", "ExtraHint", "Coop", "PerfHint", "CurrentRun", "Treasure", "TreasureHint", "MixTitle", "MixHint", "Unknown", "Shop", "Rest", "Elite", "SecFloors", "SecTreasure", "SecMix" })
        {
            string english = InGameConfig.Translate(key, "eng");
            string chinese = InGameConfig.Translate(key, "zhs");
            if (english == key || chinese == key || english == chinese) throw new Exception("Missing translation: " + key);
        }
        foreach (string name in new[] { "Act1ExtraFloors", "Act2ExtraFloors", "Act3ExtraFloors" })
        {
            var property = typeof(FloorConfig).GetProperty(name)!;
            var slider = property.GetCustomAttribute<BaseLib.Config.ConfigSliderAttribute>()!;
            if (slider.Min != 0 || slider.Max != FloorSettings.MaxFloors || slider.Step != 1 || slider.Format != "{0:0}") throw new Exception("BaseLib slider metadata");
            property.SetValue(null, 99);
            if ((int)property.GetValue(null)! != FloorSettings.MaxFloors) throw new Exception("Slider clamping");
            property.SetValue(null, 5);
        }
        foreach (string name in new[] { "UnknownPercent", "ShopPercent", "RestPercent", "ElitePercent" })
        {
            var property = typeof(FloorConfig).GetProperty(name)!;
            var slider = property.GetCustomAttribute<BaseLib.Config.ConfigSliderAttribute>()!;
            if (slider.Min != 0 || slider.Max != RoomMix.MaxPercent || slider.Step != 5) throw new Exception("room mix slider metadata");
            property.SetValue(null, 150);
            if ((int)property.GetValue(null)! != RoomMix.MaxPercent) throw new Exception("room mix slider clamping");
            property.SetValue(null, 0);
        }
        if (!typeof(BaseLib.Config.SimpleModConfig).IsAssignableFrom(typeof(FloorConfig))) throw new Exception("Wrong config backend");
        if (typeof(ModEntry).Assembly.GetReferencedAssemblies().Any(a => a.Name == "BonModConfig")) throw new Exception("Old dependency remains");
        Console.WriteLine("PASS: BaseLib config type, slider metadata, setters, clamping, translations; no BonModConfig assembly dependency.");
    }
}


static class SyncHarness
{
    public static void Check()
    {
        int saved1 = ModEntry.Settings.Act1ExtraFloors, saved2 = ModEntry.Settings.Act2ExtraFloors, saved3 = ModEntry.Settings.Act3ExtraFloors;
        int savedUnknown = ModEntry.Settings.UnknownPercent, savedShop = ModEntry.Settings.ShopPercent;
        int savedRest = ModEntry.Settings.RestPercent, savedElite = ModEntry.Settings.ElitePercent;
        try
        {
            ModEntry.Settings.Act1ExtraFloors = 7;
            ModEntry.Settings.Act2ExtraFloors = 8;
            ModEntry.Settings.Act3ExtraFloors = 9;

            HostSync.Forget();
            Require(!HostSync.TryGet(out _), "no host values expected");
            Require(RunSettings.ExtraForNewRun(0, coop: true) == 7, "coop without host values falls back to local");
            Require(RunSettings.ExtraForNewRun(2, coop: true) == 9, "coop fallback act 3");

            HostSync.Apply(2, 3, 4);
            Require(RunSettings.ExtraForNewRun(0, coop: true) == 2, "host act 1 wins in coop");
            Require(RunSettings.ExtraForNewRun(1, coop: true) == 3, "host act 2 wins in coop");
            Require(RunSettings.ExtraForNewRun(2, coop: true) == 4, "host act 3 wins in coop");
            Require(RunSettings.ExtraForNewRun(0, coop: false) == 7, "singleplayer ignores host values");
            Require(RunSettings.ExtraForNewRun(2, coop: false) == 9, "singleplayer ignores host values act 3");

            HostSync.Apply(-5, 99, FloorSettings.MaxFloors);
            Require(RunSettings.ExtraForNewRun(0, coop: true) == 0, "host value clamp low");
            Require(RunSettings.ExtraForNewRun(1, coop: true) == FloorSettings.MaxFloors, "host value clamp high");
            Require(RunSettings.ExtraForNewRun(2, coop: true) == FloorSettings.MaxFloors, "host value clamp high act 3");

            HostSync.Apply(new HostValues(4, 5, 6, 25, 10, 10, 10));
            var hostMix = RunSettings.MixForNewRun(coop: true);
            Require(hostMix.Unknown == 25 && hostMix.Shop == 10 && hostMix.Rest == 10 && hostMix.Elite == 10 && hostMix.Monster == 45,
                $"host room mix wins in coop, got {hostMix.Unknown}/{hostMix.Shop}/{hostMix.Rest}/{hostMix.Elite}/{hostMix.Monster}");
            ModEntry.Settings.UnknownPercent = 0;
            ModEntry.Settings.ShopPercent = 0;
            ModEntry.Settings.RestPercent = 0;
            ModEntry.Settings.ElitePercent = 0;
            Require(!RunSettings.MixForNewRun(coop: false).Custom, "singleplayer keeps the local (vanilla) mix");

            HostSync.Apply(2, 3, 4);
            HostSync.Forget();
            Require(RunSettings.ExtraForNewRun(0, coop: true) == 7, "forget restores local values");

            var message = new HostFloorsMessage { Act1 = 2, Act2 = 3, Act3 = FloorSettings.MaxFloors, Unknown = 25, Shop = 10, Rest = 10, Elite = 10 };
            Require(message.ShouldBroadcast, "message must broadcast");
            Require(message.Mode == MegaCrit.Sts2.Core.Multiplayer.Transport.NetTransferMode.Reliable, "message must be reliable");
            Require(message.ShouldBuffer, "message must buffer");
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            message.Serialize(writer);
            Require(writer.BytePosition == 7, "seven bytes on the wire, got " + writer.BytePosition);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            var copy = new HostFloorsMessage();
            copy.Deserialize(reader);
            Require(copy.Act1 == 2 && copy.Act2 == 3 && copy.Act3 == FloorSettings.MaxFloors, $"roundtrip {copy.Act1}/{copy.Act2}/{copy.Act3}");
            Require(copy.Unknown == 25 && copy.Shop == 10 && copy.Rest == 10 && copy.Elite == 10, "room mix roundtrip");
            Console.WriteLine("PASS: host floor values and room mix win in co-op only, clamping, message contract and 7-byte roundtrip.");
        }
        finally
        {
            HostSync.Forget();
            ModEntry.Settings.Act1ExtraFloors = saved1;
            ModEntry.Settings.Act2ExtraFloors = saved2;
            ModEntry.Settings.Act3ExtraFloors = saved3;
            ModEntry.Settings.UnknownPercent = savedUnknown;
            ModEntry.Settings.ShopPercent = savedShop;
            ModEntry.Settings.RestPercent = savedRest;
            ModEntry.Settings.ElitePercent = savedElite;
        }
    }

    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}

static class NameHarness
{
    public static void Check()
    {
        // The manifest name is one fixed string; the mod list must follow the game language.
        foreach (string chinese in new[] { "zhs", "zht", "zh_CN" })
        {
            if (ModNameLocalization.Replace(ModNameLocalization.EnglishName, chinese) != "每幕自定义加层")
                throw new Exception("English name was not translated for " + chinese);
        }
        if (ModNameLocalization.Replace(ModNameLocalization.EnglishName, "eng") != ModNameLocalization.EnglishName)
            throw new Exception("English name changed for English");
        if (ModNameLocalization.Replace(ModNameLocalization.LegacyName, "zhs") != "每幕自定义加层")
            throw new Exception("legacy bilingual name was not translated");
        if (ModNameLocalization.Replace("Some other mod", "zhs") != "Some other mod")
            throw new Exception("unrelated text must never be rewritten");
        if (ModNameLocalization.Replace(null, "zhs") != "")
            throw new Exception("null text must stay empty");
        if (InGameConfig.IsChinese("eng") || !InGameConfig.IsChinese("zhs"))
            throw new Exception("language detection");
        Console.WriteLine("PASS: mod name follows the game language and leaves other text alone.");
    }
}
static class MapCompatHarness
{
    public static void Check()
    {
        var act = ModelDb.Act<Overgrowth>();
        int saved = ModEntry.Settings.Act1ExtraFloors;
        try
        {
            ModEntry.Settings.Act1ExtraFloors = 0;
            var vanilla = new StandardActMap(new Rng(1), act, false, false, false);
            if (LongMapRule.ShouldSkipPruning(vanilla))
                throw new Exception("a vanilla length map must keep path pruning");

            ModEntry.Settings.Act1ExtraFloors = 30;
            var longMap = new StandardActMap(new Rng(1), act, false, false, false);
            if (longMap.GetRowCount() <= LongMapRule.MaxRowsForPruning)
                throw new Exception("the stretched map is not long enough for this test");
            if (!LongMapRule.ShouldSkipPruning(longMap))
                throw new Exception("a stretched map must skip path pruning");

            var pruneMethod = HarmonyLib.AccessTools.Method(typeof(MapPathPruning), nameof(MapPathPruning.PruneAndRepair));
            var prunePatches = HarmonyLib.Harmony.GetPatchInfo(pruneMethod);
            if (prunePatches == null || !prunePatches.Prefixes.Any(p => p.owner == "bobojo.ActFloorCustomizer"))
                throw new Exception("the pruning guard is not installed");

            var spoilsCtor = HarmonyLib.AccessTools.Constructor(typeof(SpoilsActMap),
                new[] { typeof(MegaCrit.Sts2.Core.Runs.IRunState), typeof(MapPointTypeCounts) });
            var spoilsPatches = spoilsCtor == null ? null : HarmonyLib.Harmony.GetPatchInfo(spoilsCtor);
            if (spoilsPatches == null || !spoilsPatches.Prefixes.Any(p => p.owner == "bobojo.ActFloorCustomizer"))
                throw new Exception("the spoils map prefix is not installed");
            if (!spoilsPatches.Transpilers.Any(p => p.owner == "bobojo.ActFloorCustomizer"))
                throw new Exception("the spoils map transpiler is not installed");

            var goldenCtor = HarmonyLib.AccessTools.Constructor(typeof(MegaCrit.Sts2.Core.Map.GoldenPathActMap),
                new[] { typeof(MegaCrit.Sts2.Core.Runs.IRunState) });
            var goldenPatches = goldenCtor == null ? null : HarmonyLib.Harmony.GetPatchInfo(goldenCtor);
            if (goldenPatches == null || !goldenPatches.Transpilers.Any(p => p.owner == "bobojo.ActFloorCustomizer"))
                throw new Exception("the golden path transpiler is not installed");

            var markerMethod = HarmonyLib.AccessTools.Method(
                typeof(MegaCrit.Sts2.Core.Models.Cards.SpoilsMap),
                nameof(MegaCrit.Sts2.Core.Models.Cards.SpoilsMap.ModifyGeneratedMapLate));
            var markerPatches = markerMethod == null ? null : HarmonyLib.Harmony.GetPatchInfo(markerMethod);
            if (markerPatches == null || !markerPatches.Postfixes.Any(p => p.owner == "bobojo.ActFloorCustomizer"))
                throw new Exception("the spoils marker postfix is not installed");

            var template = new List<MapPointType>
            {
                MapPointType.Monster, MapPointType.Unknown, MapPointType.Monster, MapPointType.RestSite,
                MapPointType.Monster, MapPointType.RestSite, MapPointType.Unknown, MapPointType.Treasure,
                MapPointType.Unknown, MapPointType.Treasure, MapPointType.Unknown, MapPointType.Shop,
                MapPointType.Elite, MapPointType.RestSite, MapPointType.Elite, MapPointType.RestSite
            };
            int goldenRooms = act.GetNumberOfRooms(false);
            var golden = GoldenPathPatch.Stretch(template, act, false);
            if (golden.Count != goldenRooms)
                throw new Exception($"golden path has {golden.Count} rooms, expected {goldenRooms}");
            if (golden[0] != MapPointType.Monster || golden[^1] != MapPointType.RestSite)
                throw new Exception("golden path must start with a monster and end with a rest site");
            if (golden.Count(t => t == MapPointType.Treasure) != 2) throw new Exception("golden path lost a treasure");
            if (golden.Count(t => t == MapPointType.Shop) != 1) throw new Exception("golden path lost the shop");
            if (golden.Count(t => t == MapPointType.Elite) != 2) throw new Exception("golden path lost an elite");
            if (golden.Count(t => t == MapPointType.RestSite) < 4) throw new Exception("golden path lost a rest");
            var goldenMultiplayer = GoldenPathPatch.Stretch(template, act, true);
            int goldenRoomsMultiplayer = act.GetNumberOfRooms(true);
            if (goldenMultiplayer.Count != goldenRoomsMultiplayer + 1)
                throw new Exception("the multiplayer golden path must carry one throwaway room");
            goldenMultiplayer.RemoveAt(2);
            if (goldenMultiplayer.Count != goldenRoomsMultiplayer) throw new Exception("multiplayer golden path length");

            ModEntry.Settings.Act1ExtraFloors = 0;
            if (!ReferenceEquals(GoldenPathPatch.Stretch(template, act, false), template))
                throw new Exception("a vanilla length act must keep the vanilla golden path");
            ModEntry.Settings.Act1ExtraFloors = 30;

            // Feed the golden path transpiler a stand-in for "this._defaultPointTypes.ToList()".
            var toList = typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.ToList) && m.GetParameters().Length == 1)
                .MakeGenericMethod(typeof(MapPointType));
            var synthetic = new List<CodeInstruction>
            {
                new(OpCodes.Ldarg_0),
                new(OpCodes.Ldfld, typeof(GoldenPathActMap).GetField("_defaultPointTypes", BindingFlags.Instance | BindingFlags.NonPublic)!),
                new(OpCodes.Call, toList),
                new(OpCodes.Stloc_0),
                new(OpCodes.Ret),
            };
            var rewritten = GoldenPathPatch.Transpiler(synthetic).ToList();            int stretchCalls = rewritten.Count(i => i.opcode == OpCodes.Call && (i.operand as MethodInfo)?.Name == nameof(GoldenPathPatch.Types));
            if (stretchCalls != 1) throw new Exception("the golden path transpiler did not insert its call");
            if (!rewritten.Any(i => i.opcode == OpCodes.Ldarg_1))
                throw new Exception("the golden path transpiler must pass the run state");

            // The spoils constructor's own call to GetNumberOfRooms must be rewritten too.
            var getRooms = HarmonyLib.AccessTools.Method(typeof(ActModel), nameof(ActModel.GetNumberOfRooms), new[] { typeof(bool) });
            if (getRooms == null) throw new Exception("ActModel.GetNumberOfRooms(bool) is missing");
            var spoilsIl = new List<CodeInstruction>
            {
                new(OpCodes.Ldarg_1),
                new(OpCodes.Callvirt, getRooms),
                new(OpCodes.Ret),
            };
            var spoilsRewritten = RoomCountPatch.ReplaceCalls(spoilsIl).ToList();
            if (!spoilsRewritten.Any(i => (i.operand as MethodInfo)?.Name == nameof(RoomCountPatch.EffectiveRooms)))
                throw new Exception("the spoils map transpiler does not rewrite GetNumberOfRooms");

            // The guarded call must return at once; without the guard this enumerates every route.
            var gridProperty = typeof(ActMap).GetProperty("Grid", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var grid = (MapPoint?[,])gridProperty.GetValue(longMap)!;
            var counts = (MapPointTypeCounts)HarmonyLib.AccessTools.Field(typeof(StandardActMap), "_pointTypeCounts").GetValue(longMap)!;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            MapPathPruning.PruneAndRepair(grid, new HashSet<MapPoint>(), longMap, counts, new Rng(1), (_, _) => true);
            watch.Stop();
            if (watch.ElapsedMilliseconds > 500)
                throw new Exception("pruning a long map took " + watch.ElapsedMilliseconds + "ms");

            int rooms = act.GetNumberOfRooms(true);
            var baseline = act.GetMapPointTypes(new Rng(3));
            var scaled = LongMapRule.Scaled(baseline, rooms, 30);
            double ratio = (double)rooms / (rooms - 30);
            int Scale(int n) => (int)Math.Round(n * ratio, MidpointRounding.AwayFromZero);
            if (scaled.NumOfUnknowns != Scale(baseline.NumOfUnknowns)) throw new Exception("unknown scaling");
            if (scaled.NumOfRests != Scale(baseline.NumOfRests)) throw new Exception("rest scaling");
            if (scaled.NumOfShops != Scale(baseline.NumOfShops)) throw new Exception("shop scaling");
            if (scaled.NumOfElites != Math.Min(14, Scale(baseline.NumOfElites))) throw new Exception("elite scaling");
            if (!ReferenceEquals(LongMapRule.Scaled(baseline, rooms, 0), baseline)) throw new Exception("vanilla budget must be returned unchanged");
            if (LongMapRule.Scaled(baseline, 10, 30) != baseline) throw new Exception("impossible budget must be returned unchanged");

            // Bonus treasure rows: the vanilla row seven above the boss counts as the first of the
            // series, then one more row every N rows going up.
            var treasureRows = new HashSet<int>();
            foreach (var point in longMap.GetAllMapPoints())
                if (point.PointType == MapPointType.Treasure) treasureRows.Add(point.coord.row);
            int vanillaTreasureRow = longMap.GetRowCount() - 7;
            int step = TreasureRows.Every;
            if (!treasureRows.Contains(vanillaTreasureRow))
                throw new Exception("the vanilla treasure row must stay a treasure row");
            for (int row = vanillaTreasureRow - step; row >= 3; row -= step)
            {
                if (!treasureRows.Contains(row))
                    throw new Exception($"row {row} should be a bonus treasure row on a stretched map");
            }
            int savedEvery = ModEntry.Settings.TreasureEveryRows;
            ModEntry.Settings.TreasureEveryRows = 0;
            var noBonus = new StandardActMap(new Rng(1), act, false, false, false);
            if (noBonus.GetAllMapPoints().Any(p => p.PointType == MapPointType.Treasure && p.coord.row != noBonus.GetRowCount() - 7))
                throw new Exception("treasure rows must disappear when the option is 0");
            ModEntry.Settings.TreasureEveryRows = savedEvery;

            Console.WriteLine("PASS: long maps skip the exponential path pruning and keep the scaled point budget.");
        }
        finally
        {
            ModEntry.Settings.Act1ExtraFloors = saved;
        }
    }
}

static class RoomMixHarness
{
    public static void Check()
    {
        var weights = RoomMix.Normalise(25, 10, 10, 10);
        Require(weights.Unknown == 25 && weights.Shop == 10 && weights.Rest == 10 && weights.Elite == 10, "normalise keeps the sliders");
        Require(weights.Monster == 45 && weights.Total == 100, $"monster is the remainder, got {weights.Monster}/{weights.Total}");
        Require(!RoomMix.Normalise(0, 0, 0, 0).Custom, "all zero must mean the vanilla mix");
        var over = RoomMix.Normalise(60, 60, 60, 60);
        Require(over.Total == 100 && over.Special == 100, $"a total above 100 must be rescaled, got {over.Special}");

        var act = ModelDb.Act<Overgrowth>();
        var plainMix = RoomMix.Normalise(0, 0, 0, 0);
        try
        {
            RunSettings.Bind(act, 30, plainMix);
            var plain = new StandardActMap(new Rng(5), act, false, false, false);
            RunSettings.Bind(act, 30, plainMix);
            var plainAgain = new StandardActMap(new Rng(5), act, false, false, false);
            Require(Signature(plain) == Signature(plainAgain), "a vanilla mix must stay deterministic");
            Require(RoomMix.Apply(plain, act, 7UL) == 0, "a vanilla mix must change nothing");

            RunSettings.Bind(act, 30, weights);
            var mixed = new StandardActMap(new Rng(5), act, false, false, false);
            RunSettings.Bind(act, 30, weights);
            var mixedAgain = new StandardActMap(new Rng(5), act, false, false, false);
            Require(Signature(mixed) == Signature(mixedAgain), "the room mix must be identical on both peers");
            Require(Signature(mixed) != Signature(plain), "the room mix changed nothing");

            var points = mixed.GetAllMapPoints().ToList();
            int Count(MapPointType type) => points.Count(p => p.PointType == type);
            Require(Count(MapPointType.Shop) >= 1, "the mix must keep a shop");
            Require(Count(MapPointType.RestSite) >= 1, "the mix must keep a rest site");
            Require(Count(MapPointType.Elite) >= 1 && Count(MapPointType.Elite) <= RoomMix.EliteCap, $"elite budget, got {Count(MapPointType.Elite)}");
            Require(Count(MapPointType.Unknown) >= 1, "the mix must produce unknown rooms");
            Require(Count(MapPointType.Monster) > 0, "the mix must keep monsters");

            int rows = mixed.GetRowCount();
            var changeable = points.Where(p => p.CanBeModified).ToList();
            Require(points.Where(p => p.coord.row == 1).All(p => p.PointType == MapPointType.Monster), "the first row must stay monsters");
            Require(points.Where(p => p.coord.row == rows - 7).All(p => p.PointType == MapPointType.Treasure), "the treasure row must stay treasures");
            Require(points.Where(p => p.coord.row == rows).All(p => p.PointType is MapPointType.RestSite or MapPointType.Boss), "the boss row must stay a rest site");
            Require(changeable.Where(p => p.PointType == MapPointType.RestSite).All(p => p.coord.row is >= 5 && p.coord.row < rows - 3), "rest sites keep the vanilla row limits");
            Require(changeable.Where(p => p.PointType == MapPointType.Elite).All(p => p.coord.row >= 6), "elites keep the vanilla row limit");

            var restricted = new HashSet<MapPointType> { MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop };
            foreach (var point in changeable)
                if (restricted.Contains(point.PointType))
                {
                    Require(!point.Children.Any(child => child.PointType == point.PointType), "two special rooms of the same type may not be adjacent");
                    Require(!point.parents.Any(parent => parent.PointType == point.PointType), "two special rooms of the same type may not be adjacent");
                }
            for (int row = 1; row <= rows; row++)
            {
                var inRow = changeable.Where(p => p.coord.row == row).ToList();
                Require(inRow.Count(p => p.PointType == MapPointType.Shop) <= 1, "at most one shop per row");
                Require(inRow.Count(p => p.PointType == MapPointType.Elite) <= 1, "at most one elite per row");
                Require(inRow.Count(p => p.PointType == MapPointType.RestSite) <= 1, "at most one rest site per row");
            }

            RunSettings.Bind(act, 0, weights);
            var vanillaLength = new StandardActMap(new Rng(5), act, false, false, false);
            Require(RoomMix.Apply(vanillaLength, act, 7UL) == 0, "the room mix must not touch a vanilla length map");

            Console.WriteLine("PASS: room mix percentages are normalised, deterministic, and keep the vanilla pacing rules.");
        }
        finally
        {
            RunSettings.Forget(act);
        }
    }

    static string Signature(ActMap map)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var point in map.GetAllMapPoints().OrderBy(p => p.coord.row).ThenBy(p => p.coord.col))
        {
            builder.Append(point.coord.row).Append(',').Append(point.coord.col).Append(':').Append(point.PointType).Append('[');
            foreach (var child in point.Children.OrderBy(c => c.coord.row).ThenBy(c => c.coord.col))
                builder.Append(child.coord.row).Append(',').Append(child.coord.col).Append(';');
            builder.Append("] ");
        }
        return builder.ToString();
    }

    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
