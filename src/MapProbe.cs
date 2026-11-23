using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace ActFloorCustomizer;

/// <summary>
/// Temporary diagnostics for the "game freezes when the map opens" report.
/// A background thread writes one line every two seconds with the frame counter and the
/// map container position, so the last line before a freeze says whether the main loop
/// stopped (a stall inside the frame) and where the map was when it did.
/// </summary>
public static class MapProbe
{
    private static readonly FieldInfo MapContainer = AccessTools.Field(typeof(NMapScreen), "_mapContainer")!;
    private static readonly FieldInfo TargetDragPos = AccessTools.Field(typeof(NMapScreen), "_targetDragPos")!;
    private static readonly FieldInfo ActAnimTween = AccessTools.Field(typeof(NMapScreen), "_actAnimTween")!;
    private static readonly FieldInfo BossNode = AccessTools.Field(typeof(NMapScreen), "_bossPointNode")!;
    private static readonly FieldInfo SecondBossNode = AccessTools.Field(typeof(NMapScreen), "_secondBossPointNode")!;
    private static readonly FieldInfo StartNode = AccessTools.Field(typeof(NMapScreen), "_startingPointNode")!;
    private static readonly FieldInfo MapBg = AccessTools.Field(typeof(NMapScreen), "_mapBgContainer")!;
    private static readonly FieldInfo PointsRoot = AccessTools.Field(typeof(NMapScreen), "_points")!;

    private static int _frames;
    private static int _beat;
    private static float _mapY;
    private static float _targetY;
    private static int _tween;
    private static int _visible;
    private static int _wasVisible;
    private static Thread? _thread;

    public static void Start()
    {
        if (_thread != null) return;
        MapLayout.Diag("probe: watchdog started");
        _thread = new Thread(Loop) { IsBackground = true, Name = "ActFloorCustomizer-watch" };
        _thread.Start();
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree)
            {
                tree.Root.AddChild(new FrameBeat { Name = "ActFloorCustomizerBeat" });
                MapLayout.Diag("probe: frame beat added");
            }
            else
            {
                MapLayout.Diag("probe: frame beat unavailable (no SceneTree)");
            }
        }
        catch (Exception ex)
        {
            MapLayout.Diag("probe: frame beat failed " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static void Beat() => Interlocked.Increment(ref _beat);

    /// <summary>Called from an <c>NMapScreen._Process</c> prefix, i.e. once per rendered frame.</summary>
    public static void Frame(NMapScreen screen)
    {
        if (!MapLayout.DiagnosticsEnabled) return;
        Interlocked.Increment(ref _frames);
        try
        {
            if (MapContainer.GetValue(screen) is Control container) _mapY = container.Position.Y;
            if (TargetDragPos.GetValue(screen) is Vector2 target) _targetY = target.Y;
            _tween = ActAnimTween.GetValue(screen) is Tween tween && tween.IsValid() ? 1 : 0;
            bool visible = screen.IsVisibleInTree();
            _visible = visible ? 1 : 0;
            if (visible && Volatile.Read(ref _wasVisible) == 0)
            {
                Volatile.Write(ref _wasVisible, 1);
                MapLayout.Diag("probe: map screen shown");
            }
            else if (!visible && Volatile.Read(ref _wasVisible) == 1)
            {
                Volatile.Write(ref _wasVisible, 0);
                MapLayout.Diag("probe: map screen hidden");
            }

            if (visible && _frames % 120 == 0)
                Geometry(screen);
        }
        catch
        {
            // Diagnostics must never break the game.
        }
    }

    /// <summary>
    /// Dumps the screen-space extent of the parchment, the boss rooms and the starting room,
    /// so the visible gaps can be measured instead of guessed.
    /// </summary>
    private static void Geometry(NMapScreen screen)
    {
        try
        {
            var container = MapContainer.GetValue(screen) as Control;
            var bg = MapBg.GetValue(screen) as Control;
            var points = PointsRoot.GetValue(screen) as Control;
            var boss = BossNode.GetValue(screen) as Control;
            var boss2 = SecondBossNode.GetValue(screen) as Control;
            var start = StartNode.GetValue(screen) as Control;

            static string Span(Control? control)
            {
                if (control == null) return "none";
                Rect2 rect = control.GetGlobalRect();
                return $"{rect.Position.Y:0}..{rect.End.Y:0}";
            }

            MapLayout.Diag(
                $"geo S={container?.Position.Y:0.#} target={_targetY:0.#} bg={Span(bg)} boss={Span(boss)} " +
                $"boss2={Span(boss2)} start={Span(start)} pts={Span(points)} " +
                $"bgParent={bg?.GetParent()?.Name} ptsParent={points?.GetParent()?.Name}");
        }
        catch
        {
            // Diagnostics must never break the game.
        }
    }

    private static void Loop()
    {
        int last = -1;
        int lastBeat = -1;
        double lastCpu = -1;
        while (true)
        {
            try
            {
                Thread.Sleep(2000);
                int frames = Volatile.Read(ref _frames);
                int beat = Volatile.Read(ref _beat);
                double cpu = -1;
                long memory = 0;
                int threads = 0;
                try
                {
                    using var process = System.Diagnostics.Process.GetCurrentProcess();
                    cpu = process.TotalProcessorTime.TotalSeconds;
                    memory = process.WorkingSet64;
                    threads = process.Threads.Count;
                }
                catch
                {
                    // Process counters are only a hint.
                }
                MapLayout.Diag(
                    $"probe beat={beat} dbeat={beat - lastBeat} frames={frames} delta={frames - last} " +
                    $"cpu={cpu:0.00} dcpu={(lastCpu < 0 ? 0 : cpu - lastCpu):0.00} " +
                    $"mem={memory / 1048576}MB threads={threads} visible={_visible} tween={_tween} " +
                    $"mapY={_mapY:0.#} targetY={_targetY:0.#}");
                last = frames;
                lastBeat = beat;
                lastCpu = cpu;
            }
            catch
            {
                // Diagnostics must never break the game.
            }
        }
    }
}

/// <summary>An empty node added to the scene root: its <c>_Process</c> proves the main loop still runs.</summary>
public partial class FrameBeat : Node
{
    public override void _Process(double delta) => MapProbe.Beat();
}

[HarmonyPatch(typeof(NMapScreen), "_Process")]
internal static class MapProbeProcessPatch
{
    [HarmonyPrefix]
    static void Prefix(NMapScreen __instance) => MapProbe.Frame(__instance);
}

[HarmonyPatch(typeof(NMapScreen), "PlayStartOfActAnimation")]
internal static class MapProbeAnimPatch
{
    [HarmonyPrefix]
    static void Prefix() => MapLayout.Diag("probe: start-of-act animation");
}
