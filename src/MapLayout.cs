using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ActFloorCustomizer;

/// <summary>
/// The map screen lays every room out on a fixed 2325px tall canvas:
/// <c>rowSpacing = (2325 - sumOfRowHeights) / (rowCount - 1)</c>.
/// Extra floors raise <c>rowCount</c> but leave the canvas alone, so the spacing is
/// squeezed and, past roughly 23 rows, turns negative: rows are placed on top of each
/// other and rooms overlap. Blitz and every other act use the same formula.
///
/// Fix: keep the vanilla per-row spacing and grow the canvas in proportion, then move
/// the boss marker up by the same amount and extend the top scroll limit so the added
/// rows stay reachable. At <see cref="Stretch"/> = 1 every replacement returns the
/// original constant, so unmodified runs behave exactly like vanilla.
/// </summary>
public static class MapLayout
{
    public const float VanillaHeight = 2325f;
    public const float VanillaBossY = -1980f;
    public const float VanillaSecondBossY = -2280f;
    public const float VanillaScrollTop = 1800f;

    /// <summary>Vertical scale factor of the current map. 1 means vanilla length.</summary>
    public static float Stretch { get; private set; } = 1f;

    /// <summary>Total canvas height used by <c>SetMap</c> (before the second-boss 0.9 factor).</summary>
    public static float BaseHeight => VanillaHeight * Stretch;

    /// <summary>Highest scroll position (was a hardcoded 1800).</summary>
    public static float ScrollTop => BaseHeight - (VanillaHeight - VanillaScrollTop);

    public static int HeightReplacements { get; private set; }
    public static int BossReplacements { get; private set; }
    public static int ScrollReplacements { get; private set; }

    public static void SetStretch(float value) => Stretch = Math.Max(1f, value);

    public static long Mark() => System.Diagnostics.Stopwatch.GetTimestamp();

    public static double Since(long mark)
        => (System.Diagnostics.Stopwatch.GetTimestamp() - mark) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Canvas scale for a map with <paramref name="rows"/> rows where this mod added
    /// <paramref name="extra"/> of them. <c>rows - extra</c> is the row count the act would
    /// have without extra floors, so (rows-1)/(vanillaRows-1) keeps the vanilla row pitch.
    /// </summary>
    public static float ComputeStretch(int rows, int extra)
    {
        if (extra <= 0) return 1f;
        int vanillaRows = rows - extra;
        if (vanillaRows < 2 || rows <= vanillaRows) return 1f;
        return (float)(rows - 1) / (vanillaRows - 1);
    }

    /// <summary>
    /// Works out the canvas scale for the map that is about to be laid out. The number of
    /// rows the map would have without this mod's extra floors is the target, so the
    /// per-row distance stays what vanilla uses for that act.
    /// </summary>
    public static void Begin(NMapScreen screen, ActMap map)
    {
        Stretch = 1f;
        try
        {
            if (map is BlitzActMap) return;
            if (AccessTools.Field(typeof(NMapScreen), "_runState")?.GetValue(screen) is not RunState runState) return;
            ActModel? act = runState.Act;
            if (act is null) return;
            Stretch = ComputeStretch(map.GetRowCount(), RunSettings.ForAct(act));
        }
        catch (Exception ex)
        {
            Stretch = 1f;
            Console.WriteLine("[ActFloorCustomizer] Map stretch skipped: " + ex.Message);
        }
    }

    /// <summary>
    /// The original code computes <c>-1980 * num</c> (or <c>-2280 * num</c> for the second
    /// boss) where num is 1, or 0.9 when an act has two bosses. The canvas grew by
    /// <c>2325 * (Stretch - 1) * num</c>, so the marker has to move up by the same amount
    /// to keep the exact gap it had below the last row.
    /// </summary>
    public static float ScaleBossOffset(float legacy, float vanillaY)
        => legacy * (1f + VanillaHeight / -vanillaY * (Stretch - 1f));

    public static float FixBossY(float legacy) => ScaleBossOffset(legacy, VanillaBossY);

    public static float FixSecondBossY(float legacy) => ScaleBossOffset(legacy, VanillaSecondBossY);

    /// <summary>Rewrites the layout constants inside <c>NMapScreen.SetMap</c>.</summary>
    public static IEnumerable<CodeInstruction> StretchSetMap(IEnumerable<CodeInstruction> instructions)
    {
        var baseHeight = AccessTools.PropertyGetter(typeof(MapLayout), nameof(BaseHeight));
        var fixBoss = AccessTools.Method(typeof(MapLayout), nameof(FixBossY));
        var fixSecondBoss = AccessTools.Method(typeof(MapLayout), nameof(FixSecondBossY));
        bool pendingBoss = false;
        bool pendingSecondBoss = false;
        bool heightReplaced = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_R4)
            {
                float value = (float)instruction.operand;
                if (!heightReplaced && Math.Abs(value - VanillaHeight) < 0.5f)
                {
                    // ldc.r4 2325 followed by "* num": pushes 2325 * Stretch instead.
                    heightReplaced = true;
                    HeightReplacements++;
                    yield return new CodeInstruction(OpCodes.Call, baseHeight)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks,
                    };
                    continue;
                }
                if (Math.Abs(value - VanillaBossY) < 0.5f) pendingBoss = true;
                else if (Math.Abs(value - VanillaSecondBossY) < 0.5f) pendingSecondBoss = true;
            }
            yield return instruction;
            if (instruction.opcode != OpCodes.Mul) continue;
            if (pendingBoss)
            {
                // "-1980 * num" is already on the stack; rescale it.
                BossReplacements++;
                yield return new CodeInstruction(OpCodes.Call, fixBoss);
                pendingBoss = false;
            }
            if (pendingSecondBoss)
            {
                BossReplacements++;
                yield return new CodeInstruction(OpCodes.Call, fixSecondBoss);
                pendingSecondBoss = false;
            }
        }
    }

    /// <summary>Replaces the hardcoded top scroll limit (1800) with <see cref="ScrollTop"/>.</summary>
    public static IEnumerable<CodeInstruction> StretchScrollTop(IEnumerable<CodeInstruction> instructions)
    {
        var scrollTop = AccessTools.PropertyGetter(typeof(MapLayout), nameof(ScrollTop));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_R4 && Math.Abs((float)instruction.operand - VanillaScrollTop) < 0.5f)
            {
                ScrollReplacements++;
                yield return new CodeInstruction(OpCodes.Call, scrollTop)
                {
                    labels = instruction.labels,
                    blocks = instruction.blocks,
                };
                continue;
            }
            yield return instruction;
        }
    }

    /// <summary>
    /// The act-start animation is an async state machine: it snaps the map to y = 1800
    /// and tweens down to the start point. Patch its MoveNext so it begins at the real
    /// top of a stretched map.
    /// </summary>
    public static void ApplyExtraPatches(Harmony harmony)
    {
        try
        {
            Type? stateMachine = typeof(NMapScreen).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(t => t.Name.StartsWith("<StartOfActAnim>", StringComparison.Ordinal));
            if (stateMachine is null)
            {
                Console.WriteLine("[ActFloorCustomizer] Act-start animation type not found; its scroll start stays at vanilla height.");
                return;
            }
            MethodInfo? moveNext = stateMachine.GetMethod("MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (moveNext is null)
            {
                Console.WriteLine("[ActFloorCustomizer] Act-start animation MoveNext not found.");
                return;
            }
            harmony.Patch(moveNext, transpiler: new HarmonyMethod(typeof(MapLayout), nameof(StretchScrollTop)));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ActFloorCustomizer] Act-start animation patch skipped: " + ex.Message);
        }
    }

    public static void Report()
    {
        Console.WriteLine($"[ActFloorCustomizer] Map layout patches: height={HeightReplacements}, boss={BossReplacements}, scroll={ScrollReplacements}");
        if (HeightReplacements == 0)
            Console.WriteLine("[ActFloorCustomizer] WARNING: map height constant not found; long maps may still be cramped.");
        if (BossReplacements != 2)
            Console.WriteLine("[ActFloorCustomizer] WARNING: boss marker constants not found as expected; the boss may sit over the last rows.");
        if (ScrollReplacements < 3)
            Console.WriteLine("[ActFloorCustomizer] WARNING: scroll limit constants not found as expected; the top of long maps may be unreachable.");
    }

    // ---------------------------------------------------------------------
    // Map background: a three-part parchment (MapTop / MapMid / MapBot), three
    // natively 1920x1080 pieces stacked by a VBoxContainer. Growing the canvas
    // leaves the upper rows without parchment.
    //
    // Resizing only the middle piece does not work: its texture is drawn at its
    // native size, so a taller box only adds empty space above and below the art
    // (and the three pieces drift apart). Scale the whole scroll vertically and
    // lift it so its bottom edge stays put; the pieces stay contiguous, keep
    // their width, and stretch by the same factor as the map.
    // ---------------------------------------------------------------------

    private sealed class BgState
    {
        public float PositionDelta;
    }

    private static readonly ConditionalWeakTable<NMapBg, BgState> BgStates = new();

    private static readonly string DiagPath = Path.Combine(
        Path.GetDirectoryName(typeof(MapLayout).Assembly.Location) ?? ".",
        "ActFloorCustomizer-diag.log");

    /// <summary>Best-effort diagnostic log; the game does not keep standard output.</summary>
    public static void Diag(string message)
    {
        try
        {
            File.AppendAllText(DiagPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{System.Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break the game.
        }
    }

    public static void ApplyBackground(NMapBg bg, bool positionWasReset)
    {
        try
        {
            if (!BgStates.TryGetValue(bg, out BgState? state))
            {
                state = new BgState();
                BgStates.Add(bg, state);
            }
            if (positionWasReset) state.PositionDelta = 0f;

            float stretch = Stretch;
            float baseHeight = bg.Size.Y;
            // Grow the sheet by exactly the amount the map canvas grew, so the sheet keeps the
            // vanilla relationship to the rooms (its top stays a fixed distance below the boss,
            // its bottom stays below the first room), instead of scaling its own height.
            float growth = VanillaHeight * (stretch - 1f);
            float scale = baseHeight <= 1f ? 1f : (baseHeight + growth) / baseHeight;

            float vanillaY = bg.Position.Y + state.PositionDelta;
            // Control.Scale pivots around PivotOffset. With a non-zero pivot the sheet grows in
            // both directions, which lifts its bottom edge and pushes its top past the screen.
            bg.PivotOffset = Vector2.Zero;
            bg.Scale = new Vector2(1f, scale);
            bg.Position = new Vector2(bg.Position.X, vanillaY - growth);
            state.PositionDelta = growth;

            try
            {
                if (AccessTools.Field(typeof(NMapBg), "_drawings")?.GetValue(bg) is NMapDrawings drawings)
                    drawings.RepositionBasedOnBackground(bg);
            }
            catch (Exception ex)
            {
                Diag("bg: drawings reposition failed: " + ex.Message);
            }

            NMapBg reference = bg;
            Callable.From(() => Diag(
                $"bg stretch={stretch:0.###} baseH={baseHeight:0.#} growth={growth:0.#} " +
                $"scaleY={reference.Scale.Y:0.###} posY={reference.Position.Y:0.#} " +
                $"bottom={reference.Position.Y + baseHeight * reference.Scale.Y:0.#}")).CallDeferred();
        }
        catch (Exception ex)
        {
            Diag("bg: stretch failed: " + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class MapScreenSetMapPatch
{
    private static long _start;
    private static int _points;

    [HarmonyPrefix]
    static void Prefix(NMapScreen __instance, ActMap map)
    {
        _start = MapLayout.Mark();
        _points = map.GetAllMapPoints().Count();
        MapLayout.Diag($"setMap begin rows={map.GetRowCount()} points={_points} stretch={MapLayout.Stretch:0.###}");
        MapLayout.Begin(__instance, map);
    }

    [HarmonyPostfix]
    static void Postfix(NMapScreen __instance)
    {
        int pathNodes = 0;
        try
        {
            if (AccessTools.Field(typeof(NMapScreen), "_pathsContainer")?.GetValue(__instance) is Node paths)
                pathNodes = paths.GetChildCount();
        }
        catch
        {
            // Diagnostics only.
        }
        MapLayout.Diag($"setMap done ms={MapLayout.Since(_start):0} points={_points} pathNodes={pathNodes}");
        if (AccessTools.Field(typeof(NMapScreen), "_mapBgContainer")?.GetValue(__instance) is NMapBg background)
            MapLayout.ApplyBackground(background, false);
    }

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        => MapLayout.StretchSetMap(instructions);
}

[HarmonyPatch(typeof(NMapBg), "OnViewportChange")]
internal static class MapBgViewportPatch
{
    [HarmonyPostfix]
    static void Postfix(NMapBg __instance) => MapLayout.ApplyBackground(__instance, true);
}

[HarmonyPatch(typeof(NMapScreen), "UpdateScrollPosition")]
internal static class MapScreenScrollPatch
{
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        => MapLayout.StretchScrollTop(instructions);
}

[HarmonyPatch(typeof(NMapScreen), "GetRowScrollPos")]
internal static class MapScreenRowScrollPatch
{
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        => MapLayout.StretchScrollTop(instructions);
}
