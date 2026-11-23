using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Map;
using System.Reflection.Emit;

namespace ActFloorCustomizer;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static FloorSettings Settings { get; private set; } = new();
    public static void Initialize()
    {
        // Diagnostics only run when the debug switch file exists, so a normal install writes nothing.
        string switches = MapLayout.DebugSwitchPath;
        MapLayout.DiagnosticsEnabled = switches.Length > 0 && File.Exists(switches);
        LoadAndPatch();
        InGameConfig.Register();
    }
    public static void LoadAndPatch()
    {
        string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        Settings = FloorSettings.Read(Path.Combine(directory, "settings.cfg"));
        var harmony = new Harmony("bobojo.ActFloorCustomizer");
        harmony.PatchAll();
        MapLayout.ApplyExtraPatches(harmony);
        ModNameLocalization.Apply(harmony);
        if (MapLayout.DiagnosticsEnabled)
        {
            MapLayout.ReadSwitches();
            MapLayout.Diag("build 0.7.2-test probe");
            MapProbe.Start();
            GenProbe.Apply(harmony);
        }
        MapLayout.Report();
        Console.WriteLine($"[ActFloorCustomizer] Extra floors: {Settings.Act1ExtraFloors}, {Settings.Act2ExtraFloors}, {Settings.Act3ExtraFloors}");
    }
}

public sealed class FloorSettings
{
    /// <summary>Highest number of extra floors a single act accepts.</summary>
    public const int MaxFloors = 50;

    public int Act1ExtraFloors { get; set; } = 5;
    public int Act2ExtraFloors { get; set; } = 5;
    public int Act3ExtraFloors { get; set; } = 5;

    /// <summary>Add a treasure row every N rows on stretched maps. 0 disables the bonus rows.</summary>
    public int TreasureEveryRows { get; set; } = 7;

    // Room mix: percentages of the still-changeable rooms on a stretched map. All zero keeps the
    // vanilla mix. Whatever the four values leave over becomes a normal fight.
    public int UnknownPercent { get; set; }
    public int ShopPercent { get; set; }
    public int RestPercent { get; set; }
    public int ElitePercent { get; set; }

    public int ForAct(int index) => Math.Clamp(index switch
    {
        0 => Act1ExtraFloors, 1 => Act2ExtraFloors, 2 => Act3ExtraFloors, _ => 0
    }, 0, MaxFloors);

    public static FloorSettings Read(string path)
    {
        var result = new FloorSettings();
        try
        {
            if (!File.Exists(path)) return result;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith('#') || line.Length == 0) continue;
                string[] pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (pair.Length != 2 || !int.TryParse(pair[1], out int value))
                    throw new FormatException("Invalid setting: " + line);
                switch (pair[0])
                {
                    case "Act1ExtraFloors": result.Act1ExtraFloors = Math.Clamp(value, 0, MaxFloors); break;
                    case "Act2ExtraFloors": result.Act2ExtraFloors = Math.Clamp(value, 0, MaxFloors); break;
                    case "Act3ExtraFloors": result.Act3ExtraFloors = Math.Clamp(value, 0, MaxFloors); break;
                    case "TreasureEveryRows": result.TreasureEveryRows = Math.Clamp(value, 0, 30); break;
                    case "UnknownPercent": result.UnknownPercent = Math.Clamp(value, 0, RoomMix.MaxPercent); break;
                    case "ShopPercent": result.ShopPercent = Math.Clamp(value, 0, RoomMix.MaxPercent); break;
                    case "RestPercent": result.RestPercent = Math.Clamp(value, 0, RoomMix.MaxPercent); break;
                    case "ElitePercent": result.ElitePercent = Math.Clamp(value, 0, RoomMix.MaxPercent); break;
                    default: throw new FormatException("Unknown setting: " + pair[0]);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ActFloorCustomizer] Configuration error; disabled extra floors: " + ex.Message);
            return new FloorSettings { Act1ExtraFloors = 0, Act2ExtraFloors = 0, Act3ExtraFloors = 0 };
        }
        return result;
    }
}

// A Blitz act uses a different map. Remember this on the act instance before room generation.
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms),
    new Type[] { typeof(Rng), typeof(UnlockState), typeof(bool), typeof(bool) })]
internal static class ModePatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => RoomCountPatch.ReplaceCalls(instructions);
    internal static readonly ConditionalWeakTable<ActModel, object> BlitzActs = new();
    private static long _start;
    static void Prefix(ActModel __instance, bool isBlitz)
    {
        _start = MapLayout.Mark();
        BlitzActs.Remove(__instance);
        if (isBlitz) BlitzActs.Add(__instance, new object());
    }
    static void Postfix(ActModel __instance, bool isMultiplayer)
    {
        MapLayout.Diag($"generateRooms ms={MapLayout.Since(_start):0} act={__instance.Index} mp={isMultiplayer} rooms={__instance.GetNumberOfRooms(isMultiplayer)}");
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GetNumberOfRooms))]
public static class RoomCountPatch
{
    // EffectiveRooms is called from several hot paths (map layout, travelability, serialization),
    // so resolve the base-count property once per act type instead of searching every call.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo> BaseCountCache = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int EffectiveRooms(ActModel act, bool multiplayer)
    {
        PropertyInfo property = BaseCountCache.GetOrAdd(act.GetType(),
            static type => AccessTools.Property(type, "BaseNumberOfRooms"));
        int original = (int)property.GetValue(act)!;
        return original - (multiplayer ? 1 : 0) + (ModePatch.BlitzActs.TryGetValue(act, out _) ? 0 : RunSettings.ForAct(act));
    }
    public static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(ActModel), nameof(ActModel.GetNumberOfRooms));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(target))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(RoomCountPatch), nameof(EffectiveRooms));
            }
            yield return instruction;
        }
    }
    static void Postfix(ActModel __instance, bool isMultiplayer, ref int __result)
    {
        __result = EffectiveRooms(__instance, isMultiplayer);
    }
}

// Vanilla pruning materializes every possible path; branching makes this exponential
// on long maps. The native generator already connects every route to the boss.
// Preserve those routes rather than running the optional duplicate-path pruning pass.
[HarmonyPatch(typeof(StandardActMap), MethodType.Constructor,
    new Type[] { typeof(Rng), typeof(ActModel), typeof(bool), typeof(bool),
        typeof(bool), typeof(MapPointTypeCounts), typeof(bool) })]
internal static class LongMapPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => RoomCountPatch.ReplaceCalls(instructions);
    private static long _start;
    static void Prefix(Rng mapRng, ActModel actModel, bool isMultiplayer, ref bool enablePruning, ref MapPointTypeCounts? mapPointTypeCountsOverride)
    {
        _start = MapLayout.Mark();
        int extra = RunSettings.ForAct(actModel);
        if (extra == 0) return;
        enablePruning = false;
        int rooms = RoomCountPatch.EffectiveRooms(actModel, isMultiplayer);
        mapPointTypeCountsOverride = LongMapRule.Scaled(
            mapPointTypeCountsOverride ?? actModel.GetMapPointTypes(mapRng), rooms, extra);
    }
    static void Postfix(StandardActMap __instance, ActModel actModel, bool isMultiplayer)
    {
        MapLayout.Diag($"actMap ms={MapLayout.Since(_start):0} act={actModel.Index} mp={isMultiplayer} rows={__instance.GetRowCount()} points={__instance.GetAllMapPoints().Count()} extra={RunSettings.ForAct(actModel)}");
        Console.WriteLine($"[ActFloorCustomizer] Act {actModel.Index + 1}, multiplayer={isMultiplayer}, rooms={__instance.GetRowCount()-1}, bossRow={__instance.BossMapPoint.coord.row}");
    }
}

