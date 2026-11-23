using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ActFloorCustomizer;

/// <summary>
/// Rules shared by every act map that can end up longer than vanilla.
///
/// Two vanilla maps replace the standard act map: <see cref="SpoilsActMap"/> (the treasure map
/// card, act 2 only) and <see cref="GoldenPathActMap"/> (the golden compass relic). The spoils map
/// is generated from the act's room count, so it grows with the extra floors and then runs
/// <see cref="MapPathPruning"/>, which enumerates every possible route. That enumeration is
/// exponential in the number of rows: it is harmless for a vanilla 16-row map and impossible for a
/// 45-row one, where it allocates until the game stops responding.
/// </summary>
public static class LongMapRule
{
    /// <summary>Vanilla maps never exceed 16 rows, so anything longer is a stretched map.</summary>
    public const int MaxRowsForPruning = 20;

    private static readonly FieldInfo ShopsField =
        AccessTools.Field(typeof(MapPointTypeCounts), "<NumOfShops>k__BackingField")!;

    public static bool ShouldSkipPruning(ActMap? map) =>
        map != null && map.GetRowCount() > MaxRowsForPruning;

    /// <summary>Scales the point type budget with the map length so shops, elites and rests keep their density.</summary>
    public static MapPointTypeCounts Scaled(MapPointTypeCounts original, int rooms, int extra)
    {
        if (extra <= 0 || rooms <= extra) return original;
        double ratio = (double)rooms / (rooms - extra);
        int Scale(int value) => (int)Math.Round(value * ratio, MidpointRounding.AwayFromZero);
        var scaled = new MapPointTypeCounts(Scale(original.NumOfUnknowns), Scale(original.NumOfRests))
        {
            NumOfElites = Math.Min(14, Scale(original.NumOfElites)),
            PointTypesThatIgnoreRules = new(original.PointTypesThatIgnoreRules)
        };
        ShopsField.SetValue(scaled, Scale(original.NumOfShops));
        return scaled;
    }

    /// <summary>Point type budget for a map that is about to be generated for <paramref name="act"/>.</summary>
    public static MapPointTypeCounts CountsFor(ActModel act, bool multiplayer, Rng rng, MapPointTypeCounts? given)
    {
        int extra = RunSettings.ForAct(act);
        if (extra <= 0) return given ?? act.GetMapPointTypes(rng);
        int rooms = RoomCountPatch.EffectiveRooms(act, multiplayer);
        return Scaled(given ?? act.GetMapPointTypes(rng), rooms, extra);
    }
}

[HarmonyPatch(typeof(MapPathPruning), nameof(MapPathPruning.PruneAndRepair))]
internal static class LongMapPruningPatch
{
    static bool Prefix(ActMap map)
    {
        if (!LongMapRule.ShouldSkipPruning(map)) return true;
        MapLayout.Diag($"prune: skipped rows={map.GetRowCount()}");
        return false;
    }
}

// The treasure map card swaps act 2 for its own hourglass map. It takes the act's room count,
// so it grows with the extra floors; give it the same scaled budget the standard map gets.
[HarmonyPatch(typeof(SpoilsActMap), MethodType.Constructor,
    new Type[] { typeof(IRunState), typeof(MapPointTypeCounts) })]
internal static class SpoilsMapPatch
{
    static void Prefix(IRunState runState, ref MapPointTypeCounts? mapPointTypeCountsOverride)
    {
        try
        {
            ActModel act = runState.Act;
            if (RunSettings.ForAct(act) <= 0) return;
            bool multiplayer = runState.Players.Count > 1;
            var rng = new Rng(runState.Rng.Seed, "spoils_map");
            mapPointTypeCountsOverride = LongMapRule.CountsFor(act, multiplayer, rng, mapPointTypeCountsOverride);
            MapLayout.Diag($"spoils: act {act.Index + 1} rooms={RoomCountPatch.EffectiveRooms(act, multiplayer)} extra={RunSettings.ForAct(act)}");
        }
        catch (Exception ex)
        {
            MapLayout.Diag("spoils: prefix failed " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // The constructor inlines ActModel.GetNumberOfRooms, so the postfix on that method never
    // fires here; rewrite the call site instead.
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        RoomCountPatch.ReplaceCalls(instructions);
}

/// <summary>
/// Vanilla puts a whole row of treasure rooms seven rows before the boss. On a stretched map that
/// is the only treasure row, so the extra floors bring no extra chests. Add one more treasure row
/// every N rows (configurable, 0 disables it), matching the vanilla row style.
/// </summary>
public static class TreasureRows
{
    public static int Every
    {
        get
        {
            int value = Math.Clamp(ModEntry.Settings.TreasureEveryRows, 0, 30);
            return value == 0 ? 0 : Math.Max(2, value);
        }
    }

    public static int Apply(ActMap? map, ActModel? act)
    {
        try
        {
            if (map == null || act == null) return 0;
            int every = Every;
            if (every <= 0) return 0;
            // Only stretched maps get the bonus rows; a vanilla act keeps the vanilla chest count.
            if (RunSettings.ForAct(act) <= 0) return 0;
            int rows = map.GetRowCount();
            if (rows < every + 4) return 0;

            var changed = new List<int>();
            // The vanilla treasure row sits seven rows above the boss. Count it as part of the
            // series, then add one more every N rows going up, so the spacing stays even.
            for (int row = rows - 7; row >= 3; row -= every)
            {
                bool touched = false;
                foreach (MapPoint point in AllInRow(map, row))
                {
                    if (point.PointType is MapPointType.Boss or MapPointType.Ancient) continue;
                    if (point.PointType == MapPointType.Treasure) continue;
                    point.PointType = MapPointType.Treasure;
                    point.CanBeModified = false;
                    touched = true;
                }
                if (touched) changed.Add(row);
            }
            if (changed.Count > 0)
                MapLayout.Diag($"treasure: rows={string.Join(",", changed)} every={every}");
            return changed.Count;
        }
        catch (Exception ex)
        {
            MapLayout.Diag("treasure: failed " + ex.GetType().Name + ": " + ex.Message);
            return 0;
        }
    }

    private static IEnumerable<MapPoint> AllInRow(ActMap map, int row)
    {
        foreach (MapPoint point in map.GetAllMapPoints())
            if (point.coord.row == row)
                yield return point;
    }
}

[HarmonyPatch(typeof(StandardActMap), MethodType.Constructor,
    new Type[] { typeof(Rng), typeof(ActModel), typeof(bool), typeof(bool), typeof(bool), typeof(MapPointTypeCounts), typeof(bool) })]
internal static class TreasureRowsStandardPatch
{
    static void Postfix(StandardActMap __instance, ActModel actModel, Rng mapRng)
    {
        TreasureRows.Apply(__instance, actModel);
        // Clone the act's own stream so the mix is identical on every peer and never steals
        // numbers from the generator itself.
        RoomMix.Apply(__instance, actModel, new Rng(mapRng.ToSerializable()));
    }
}

[HarmonyPatch(typeof(SpoilsActMap), MethodType.Constructor,
    new Type[] { typeof(IRunState), typeof(MapPointTypeCounts) })]
internal static class TreasureRowsSpoilsPatch
{
    static void Postfix(SpoilsActMap __instance, IRunState runState)
    {
        TreasureRows.Apply(__instance, runState.Act);
        RoomMix.Apply(__instance, runState.Act, runState.Rng.Seed);
    }
}

// The golden compass relic swaps the act for a straight seven-column line with a fixed sixteen
// room pattern, so it ignores the extra floors entirely. Keep the line, but stretch it to the
// configured room count, keeping the vanilla spacing of the special rooms.
[HarmonyPatch(typeof(GoldenPathActMap), MethodType.Constructor, new Type[] { typeof(IRunState) })]
public static class GoldenPathPatch
{    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo? stretch = AccessTools.Method(typeof(GoldenPathPatch), nameof(Types));
        var list = instructions.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            CodeInstruction instruction = list[i];
            yield return instruction;
            if (stretch == null) continue;
            if (instruction.opcode != OpCodes.Call || instruction.operand is not MethodInfo callee) continue;
            if (callee.Name != nameof(Enumerable.ToList) || callee.DeclaringType != typeof(Enumerable)) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call, stretch);
        }
    }

    /// <summary>Entry point used by the rewritten constructor.</summary>
    public static List<MapPointType> Types(List<MapPointType> vanilla, IRunState runState) =>
        Stretch(vanilla, runState.Act, runState.Players.Count > 1);

    // The straight line is a normal map once built, so the room mix applies to it as well.
    static void Postfix(GoldenPathActMap __instance, IRunState runState) =>
        RoomMix.Apply(__instance, runState.Act, runState.Rng.Seed);

    /// <summary>Replaces the fixed sixteen room pattern with one that fills the configured length.</summary>
    public static List<MapPointType> Stretch(List<MapPointType> vanilla, ActModel act, bool multiplayer)
    {
        try
        {
            if (RunSettings.ForAct(act) <= 0) return vanilla;
            int rooms = RoomCountPatch.EffectiveRooms(act, multiplayer);
            if (rooms <= vanilla.Count) return vanilla;

            var template = new List<MapPointType>(vanilla);
            if (multiplayer && template.Count > 2) template.RemoveAt(2);

            var result = new List<MapPointType>(rooms);
            for (int i = 0; i < rooms; i++) result.Add(i % 3 == 1 ? MapPointType.Unknown : MapPointType.Monster);

            int last = 0;
            for (int i = 0; i < template.Count; i++)
            {
                MapPointType type = template[i];
                if (type is MapPointType.Monster or MapPointType.Unknown) continue;
                if (last + 1 > rooms - 2) break;
                double scaled = (double)i * (rooms - 1) / (template.Count - 1);
                int position = Math.Clamp((int)Math.Round(scaled, MidpointRounding.AwayFromZero), last + 1, rooms - 2);
                if (position <= last) continue;
                result[position] = type;
                last = position;
            }

            result[0] = MapPointType.Monster;
            result[rooms - 1] = MapPointType.RestSite;
            // The constructor removes index 2 on multiplayer, so add a throwaway monster there.
            if (multiplayer && rooms > 3) result.Insert(2, MapPointType.Monster);

            MapLayout.Diag($"golden: act {act.Index + 1} rooms={rooms} mp={multiplayer}");
            return result;
        }
        catch (Exception ex)
        {
            MapLayout.Diag("golden: stretch failed " + ex.GetType().Name + ": " + ex.Message);
            return vanilla;
        }
    }
}
