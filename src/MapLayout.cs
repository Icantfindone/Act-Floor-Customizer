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

    /// <summary>
    /// The map background is three 1920x1080 pieces stacked in a 3240px tall box: a rolled top end,
    /// a flat middle and a rolled bottom end. Only the middle is stretched, so the rolls keep their
    /// vanilla size and do not look blown up on long maps.
    /// </summary>
    public const float VanillaPieceHeight = 1080f;

    /// <summary>Width of one parchment piece; the middle is tiled at its native width.</summary>
    public const float VanillaPieceWidth = 1920f;

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

    /// <summary>Test switch: skip the parchment scaling entirely.</summary>
    public static bool SkipBackground { get; private set; }

    /// <summary>Test switch: lay the map out exactly like vanilla (no canvas growth at all).</summary>
    public static bool NoStretch { get; private set; }

    /// <summary>
    /// Optional debug switch file. When it exists the diagnostic log and the probes are switched on,
    /// which is how the freeze and parchment investigations were done; a normal install never has it.
    /// </summary>
    public static string DebugSwitchPath
    {
        get
        {
            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrEmpty(appData)
                ? string.Empty
                : Path.Combine(appData, "SlayTheSpire2", "ActFloorCustomizer-test.txt");
        }
    }

    /// <summary>
    /// Reads optional debug switches from
    /// <c>%APPDATA%\SlayTheSpire2\ActFloorCustomizer-test.txt</c> (one per line:
    /// <c>nobg</c> disables the parchment stretch, <c>nostretch</c> disables every layout change).
    /// Used only while diagnosing a freeze report; harmless when the file is absent.
    /// </summary>
    public static void ReadSwitches()
    {
        SkipBackground = false;
        NoStretch = false;
        try
        {
            string path = DebugSwitchPath;
            if (path.Length > 0 && File.Exists(path))
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim().ToLowerInvariant();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    if (line == "nobg") SkipBackground = true;
                    else if (line == "nostretch") NoStretch = true;
                }
            }
        }
        catch
        {
            // Switches must never break the game.
        }
        Diag($"switches nobg={SkipBackground} nostretch={NoStretch}");
    }

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
        if (NoStretch) return 1f;
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

        /// <summary>Vanilla CustomMinimumSize of the middle piece, captured before we touch it.</summary>
        public Vector2 MidMin;

        public bool MidMinCaptured;

        /// <summary>The middle parchment texture, kept because the piece itself is emptied while stretched.</summary>
        public Texture2D? MidTexture;

        /// <summary>Holder of the stacked middle strips, parented to the middle piece.</summary>
        public Control? MidHolder;

        public readonly List<TextureRect> MidTiles = new();

        /// <summary>Vanilla size of the drawing layer, captured before we grow it.</summary>
        public Vector2 DrawingsSize;

        public bool DrawingsCaptured;
    }

    private static readonly ConditionalWeakTable<NMapBg, BgState> BgStates = new();

    private static bool _skipLogged;

    /// <summary>
    /// Set by <see cref="ModEntry.Initialize"/>. The offline test harness calls LoadAndPatch
    /// directly, and writing diagnostics there would both pollute the player's log and touch
    /// Godot APIs that only exist inside the running game.
    /// </summary>
    public static bool DiagnosticsEnabled { get; set; }

    // Resolved lazily: touching OS.GetUserDataDir outside the engine faults.
    private static string? _diagPath;

    private static string DiagPath => _diagPath ??= ResolveDiagPath();

    /// <summary>
    /// Diagnostics go to the game's user data folder, never next to the DLL: a Workshop
    /// install lives under steamapps\workshop and must stay untouched.
    /// </summary>
    private static string ResolveDiagPath()
    {
        try
        {
            string userDir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDir))
                return Path.Combine(userDir, "ActFloorCustomizer-diag.log");
        }
        catch
        {
            // Fall back to the roaming profile, then to the mod folder.
        }
        try
        {
            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
                return Path.Combine(appData, "SlayTheSpire2", "ActFloorCustomizer-diag.log");
        }
        catch
        {
            // Ignore.
        }
        return Path.Combine(
            Path.GetDirectoryName(typeof(MapLayout).Assembly.Location) ?? ".",
            "ActFloorCustomizer-diag.log");
    }

    private static readonly object DiagLock = new();

    /// <summary>Best-effort diagnostic log; the game does not keep standard output.</summary>
    public static void Diag(string message)
    {
        if (!DiagnosticsEnabled) return;
        try
        {
            lock (DiagLock)
                File.AppendAllText(DiagPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{System.Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break the game.
        }
    }

    private static bool _dumped;

    /// <summary>
    /// Diagnostic only: measures the parchment textures once - their real size, where the opaque
    /// paper sits inside them (alpha bounds) and what rect vanilla gives each piece - and writes the
    /// images next to the log so they can be inspected offline.
    /// </summary>
    public static void DumpPieces(NMapBg bg)
    {
        if (_dumped) return;
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(DiagPath) ?? ".", "ActFloorCustomizer-bg");
            Directory.CreateDirectory(dir);
            bool any = false;
            foreach (string field in new[] { "_mapTop", "_mapMid", "_mapBot" })
            {
                if (AccessTools.Field(typeof(NMapBg), field)?.GetValue(bg) is not TextureRect piece) continue;
                Texture2D? tex = piece.Texture;
                if (tex == null) continue;
                any = true;
                int tw = tex.GetWidth();
                int th = tex.GetHeight();
                string bounds = "no-image";
                Image img = tex.GetImage();
                if (img != null)
                {
                    int w = img.GetWidth();
                    int h = img.GetHeight();
                    bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && img.GetPixel(x, y).A > 0.03f;
                    int row = h / 2;
                    int left = -1, right = -1, top = -1, bottom = -1;
                    for (int x = 0; x < w; x++) if (Solid(x, row)) { left = x; break; }
                    for (int x = w - 1; x >= 0; x--) if (Solid(x, row)) { right = x; break; }
                    int col = w / 2;
                    for (int y = 0; y < h; y++) if (Solid(col, y)) { top = y; break; }
                    for (int y = h - 1; y >= 0; y--) if (Solid(col, y)) { bottom = y; break; }
                    int x0 = -1, x1 = -1, y0 = -1, y1 = -1;
                    for (int x = 0; x < w; x++) { for (int y = 0; y < h; y += 4) if (Solid(x, y)) { x0 = x; break; } if (x0 >= 0) break; }
                    for (int x = w - 1; x >= 0; x--) { for (int y = 0; y < h; y += 4) if (Solid(x, y)) { x1 = x; break; } if (x1 >= 0) break; }
                    for (int y = 0; y < h; y++) { for (int x = 0; x < w; x += 4) if (Solid(x, y)) { y0 = y; break; } if (y0 >= 0) break; }
                    for (int y = h - 1; y >= 0; y--) { for (int x = 0; x < w; x += 4) if (Solid(x, y)) { y1 = y; break; } if (y1 >= 0) break; }
                    var cov = new System.Text.StringBuilder();
                    int step = Math.Max(1, h / 12);
                    for (int y = 0; y < h; y += step)
                    {
                        int n = 0;
                        for (int x = 0; x < w; x += 8) if (Solid(x, y)) n++;
                        cov.Append($"{y}:{n * 8} ");
                    }
                    bounds = $"fmt={img.GetFormat()} paperRow={left}..{right} paperCol={top}..{bottom} " +
                             $"pageBox={x0},{y0}..{x1},{y1} rows={cov}";
                    string path = Path.Combine(dir, field.TrimStart('_') + ".png");
                    img.SavePng(path);
                }
                Diag($"dump {field}: tex={tw}x{th} rect={piece.Size.X:0}x{piece.Size.Y:0} " +
                     $"min={piece.CustomMinimumSize.X:0}x{piece.CustomMinimumSize.Y:0} " +
                     $"expand={piece.ExpandMode} stretch={piece.StretchMode} pos={piece.Position.X:0},{piece.Position.Y:0} {bounds}");
            }
            if (any) _dumped = true;
            Diag($"dump bg: size={bg.Size.X:0}x{bg.Size.Y:0} pos={bg.Position.X:0},{bg.Position.Y:0} children={bg.GetChildCount()}");
        }
        catch (Exception ex)
        {
            Diag("dump failed: " + ex.Message);
        }
    }
    /// <summary>
    /// Restores the vanilla look of the middle piece: the 2035px texture fitted into the 1920x1080
    /// box, which squeezes it to 1526px wide - the same width the two rolled ends are drawn at.
    /// </summary>
    private static void RestoreMiddle(TextureRect middle, BgState state)
    {
        if (state.MidHolder != null)
        {
            state.MidHolder.QueueFree();
            state.MidHolder = null;
            state.MidTiles.Clear();
        }
        if (state.MidTexture != null) middle.Texture = state.MidTexture;
        float width = state.MidMin.X > 0f ? state.MidMin.X : VanillaPieceWidth;
        middle.CustomMinimumSize = new Vector2(width, VanillaPieceHeight);
    }

    /// <summary>
    /// Fills the grown middle box with vertically stacked copies of the middle texture. Every copy is
    /// drawn at the vanilla width (so the paper edges line up with the rolled ends) and only slightly
    /// taller than vanilla, so the torn left and right edges repeat gently instead of being smeared
    /// over a five-times-taller sheet. Returns how much the sheet grew upwards.
    /// </summary>
    private static float StackMiddle(TextureRect middle, Texture2D tex, float boxWidth, float totalHeight, BgState state)
    {
        float texW = Mathf.Max(1f, tex.GetWidth());
        float texH = Mathf.Max(1f, tex.GetHeight());
        float fit = Mathf.Min(boxWidth / texW, VanillaPieceHeight / texH);
        float drawW = texW * fit;
        float drawX = (boxWidth - drawW) * 0.5f;
        int count = Mathf.Max(1, Mathf.CeilToInt(totalHeight / VanillaPieceHeight));
        float tileH = totalHeight / count;

        middle.Texture = null;
        middle.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        middle.StretchMode = TextureRect.StretchModeEnum.Scale;
        float width = state.MidMin.X > 0f ? state.MidMin.X : VanillaPieceWidth;
        middle.CustomMinimumSize = new Vector2(width, totalHeight);

        if (state.MidHolder == null)
        {
            state.MidHolder = new Control
            {
                Name = "ActFloorPaper",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            middle.AddChild(state.MidHolder);
            state.MidTiles.Clear();
        }
        while (state.MidTiles.Count < count)
        {
            var tile = new TextureRect
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            state.MidHolder.AddChild(tile);
            state.MidTiles.Add(tile);
        }
        state.MidHolder.Position = Vector2.Zero;
        state.MidHolder.Size = new Vector2(boxWidth, totalHeight);
        for (int i = 0; i < state.MidTiles.Count; i++)
        {
            TextureRect tile = state.MidTiles[i];
            if (i >= count)
            {
                tile.Visible = false;
                continue;
            }
            tile.Visible = true;
            tile.Texture = tex;
            tile.Position = new Vector2(drawX, i * tileH);
            tile.Size = new Vector2(drawW, tileH);
        }
        Diag($"bg paper tiles={count} tileH={tileH:0.#} drawW={drawW:0.#} x={drawX:0.#} box={boxWidth:0.#}x{totalHeight:0.#}");
        return totalHeight - VanillaPieceHeight;
    }

    // ---------------------------------------------------------------------
    // Map drawings (the quill layer).
    //
    // Vanilla sizes this Control to the parchment once and converts every stroke through
    // ToNetPosition, which divides by base.Size.Y. On a stretched map the sheet is several
    // times taller but the drawing layer keeps its vanilla height, so only the band under
    // the rolled top accepts ink. Growing the layer (and the half-resolution sub-viewport
    // that renders the strokes, which vanilla sizes at half of the layer) makes the whole
    // map drawable again; every stroke stays in normalised net space, so peers with the same
    // floor count still agree on where a line is.
    // ---------------------------------------------------------------------

    private static readonly FieldInfo? DrawingStatesField = AccessTools.Field(typeof(NMapDrawings), "_drawingStates");

    private static readonly Dictionary<Type, FieldInfo?> ViewportFieldCache = new();

    private static readonly Dictionary<Type, FieldInfo?> DrawingTextureFieldCache = new();

    private static void FitDrawings(NMapDrawings drawings, NMapBg bg, BgState state)
    {
        if (!state.DrawingsCaptured)
        {
            state.DrawingsSize = drawings.Size;
            state.DrawingsCaptured = true;
        }
        bool restore = Stretch <= 1.001f;
        float width = state.DrawingsSize.X > 0f ? state.DrawingsSize.X : VanillaPieceWidth;
        float vanillaHeight = state.DrawingsSize.Y > 0f ? state.DrawingsSize.Y : VanillaHeight;
        float height = restore ? vanillaHeight : Mathf.Max(vanillaHeight, bg.Size.Y);
        if (Mathf.Abs(drawings.Size.X - width) > 0.5f || Mathf.Abs(drawings.Size.Y - height) > 0.5f)
        {
            drawings.CustomMinimumSize = new Vector2(width, height);
            drawings.Size = new Vector2(width, height);
            Diag($"drawings size={drawings.Size.X:0.#}x{drawings.Size.Y:0.#} vanilla={state.DrawingsSize.X:0.#}x{state.DrawingsSize.Y:0.#}");
        }
        ResizeDrawingStates(drawings, width, height);
    }

    private static void ResizeDrawingStates(NMapDrawings drawings, float width, float height)
    {
        if (DrawingStatesField?.GetValue(drawings) is not System.Collections.IEnumerable states) return;
        foreach (object? entry in states)
        {
            if (entry == null) continue;
            Type type = entry.GetType();
            if (!ViewportFieldCache.TryGetValue(type, out FieldInfo? viewportField))
            {
                viewportField = AccessTools.Field(type, "drawViewport");
                ViewportFieldCache[type] = viewportField;
            }
            if (!DrawingTextureFieldCache.TryGetValue(type, out FieldInfo? textureField))
            {
                textureField = AccessTools.Field(type, "drawingTexture");
                DrawingTextureFieldCache[type] = textureField;
            }
            // Vanilla renders the strokes into a half-resolution sub-viewport and stretches that
            // texture back over the layer, so both have to follow the new layer size.
            if (viewportField?.GetValue(entry) is SubViewport viewport)
            {
                Vector2I wanted = new(Mathf.Max(1, (int)(width * 0.5f)), Mathf.Max(1, (int)(height * 0.5f)));
                if (viewport.Size != wanted)
                {
                    viewport.Size = wanted;
                    Diag($"drawings viewport={wanted.X}x{wanted.Y}");
                }
            }
            if (textureField?.GetValue(entry) is TextureRect texture)
            {
                if (texture.Position != Vector2.Zero) texture.Position = Vector2.Zero;
                if (Mathf.Abs(texture.Size.X - width) > 0.5f || Mathf.Abs(texture.Size.Y - height) > 0.5f)
                {
                    texture.Size = new Vector2(width, height);
                    Diag($"drawings texture={texture.Size.X:0.#}x{texture.Size.Y:0.#}");
                }
            }
        }
    }

    public static void ApplyBackground(NMapBg bg, bool positionWasReset)
    {
        try
        {
            if (SkipBackground)
            {
                if (!_skipLogged)
                {
                    _skipLogged = true;
                    Diag("bg: skipped by test switch");
                }
                return;
            }
            if (!BgStates.TryGetValue(bg, out BgState? state))
            {
                state = new BgState();
                BgStates.Add(bg, state);
            }
            if (positionWasReset) state.PositionDelta = 0f;

            if (DiagnosticsEnabled) 
            {
                NMapBg probe = bg;
                Callable.From(() => DumpPieces(probe)).CallDeferred();
            }
            float stretch = Stretch;
            float growth = VanillaHeight * (stretch - 1f);
            float vanillaY = bg.Position.Y + state.PositionDelta;

            // Stretch the flat middle piece only, and only vertically: the two rolled ends keep their
            // vanilla 1080px height and are simply pushed apart, so a long map looks like a longer
            // sheet instead of one with oversized rolls, and the paper edges keep their vanilla
            // distance to the boss room and to the starting room.
            // The width must stay exactly what vanilla gives this node. The middle texture is 2035px
            // wide inside a 1920px box, so vanilla squeezes it horizontally; forcing the box to the
            // texture width made the sheet 115px wider than the rolled ends. Keep the vanilla
            // CustomMinimumSize.X and only override the height.
            float added = 0f;
            if (AccessTools.Field(typeof(NMapBg), "_mapMid")?.GetValue(bg) is TextureRect middle)
            {
                if (!state.MidMinCaptured)
                {
                    state.MidMin = middle.CustomMinimumSize;
                    state.MidMinCaptured = true;
                }
                if (middle.Texture != null) state.MidTexture = middle.Texture;
                float pieceWidth = state.MidMin.X > 0f ? state.MidMin.X : VanillaPieceWidth;
                float boxWidth = bg.Size.X > 0f ? bg.Size.X : pieceWidth;
                if (stretch <= 1.001f || state.MidTexture == null)
                {
                    RestoreMiddle(middle, state);
                    added = 0f;
                }
                else
                {
                    added = StackMiddle(middle, state.MidTexture, boxWidth, VanillaPieceHeight + growth, state);
                }
            }
            bg.PivotOffset = Vector2.Zero;
            bg.Scale = Vector2.One;
            bg.Position = new Vector2(bg.Position.X, vanillaY - added);
            state.PositionDelta = added;

            try
            {
                if (AccessTools.Field(typeof(NMapBg), "_drawings")?.GetValue(bg) is NMapDrawings drawings)
                {
                    FitDrawings(drawings, bg, state);
                    drawings.RepositionBasedOnBackground(bg);
                }
            }
            catch (Exception ex)
            {
                Diag("bg: drawings reposition failed: " + ex.Message);
            }

            NMapBg reference = bg;
            Callable.From(() =>
            {
                Diag($"bg stretch={stretch:0.###} growth={growth:0.#} " +
                     $"bgW={reference.Size.X:0.#} bgH={reference.Size.Y:0.#} posY={reference.Position.Y:0.#} " +
                     $"bottom={reference.Position.Y + reference.Size.Y:0.#}");
            }).CallDeferred();
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

[HarmonyPatch(typeof(NMapBg), "OnVisibilityChanged")]
internal static class MapBgVisibilityPatch
{
    [HarmonyPostfix]
    static void Postfix(NMapBg __instance)
    {
        // Vanilla re-assigns the three textures here, which would undo the stack; re-apply next frame.
        NMapBg probe = __instance;
        Callable.From(() =>
        {
            if (!MapLayout.DiagnosticsEnabled) return;
            MapLayout.DumpPieces(probe);
        }).CallDeferred();
        Callable.From(() => MapLayout.ApplyBackground(probe, false)).CallDeferred();
    }
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