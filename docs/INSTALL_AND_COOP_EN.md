# Act Floor Customizer v0.7.2 — installation and co-op

Targets **Slay the Spire 2 v0.113.1**. Uses **BaseLib's built-in ModConfig**, compiled against the installed BaseLib v3.4.8 API.

## Install on every player's computer

**Easiest: subscribe on the Steam Workshop.** Open https://steamcommunity.com/sharedfiles/filedetails/?id=3843992717 and subscribe, plus its BaseLib prerequisite, then enable the mod in game and restart. No files to copy.

Or install manually:

1. Close the game. Subscribe to and enable [BaseLib by Alchyr](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127), including its supplied compatibility helper components.
2. Extract `ActFloorCustomizer-v0.7.2-sts2-v0.113.1.zip`.
3. In Steam, right-click the game → Manage → Browse local files. Copy the extracted `ActFloorCustomizer` folder into the game's `mods` folder. Create `mods` if necessary.
4. Launch the game, enable the mod, and restart if prompted. When updating, replace existing files rather than keeping multiple versions.

Expected path: `Slay the Spire 2/mods/ActFloorCustomizer/ActFloorCustomizer.dll`. The manifest `ActFloorCustomizer.json` must be beside it.

**Boninall's Mod Config is no longer a dependency.** Do not uninstall it if your other mods need it. The ZIP does not bundle BaseLib or game DLLs; install the dependency separately.

## Configure in the game

Open **BaseLib's Mod Config menu → Act Floor Customizer**.

The three sliders set extra regular floors for Acts 1, 2 and 3. Each accepts 0–50; default: 5. Zero keeps that act's normal length. For example, `10 / 10 / 10` adds ten floors per act; it does not set the total to ten.

A fourth slider, `Treasure row every N rows`, sets the spacing of the bonus treasure rows (0–30, default 7; 0 disables them). The same value can be edited in `settings.cfg` as `TreasureEveryRows=7`.

Below those, four sliders set the share of each special room type (v0.8.0): unknown, shop, rest site and elite, in percent; everything left over becomes a normal fight. `0 / 0 / 0 / 0` keeps the vanilla mix. If the four numbers add up to more than 100 they are scaled down proportionally. Only stretched acts are rearranged; acts at their normal length stay untouched, and the vanilla pacing rules are kept (elites never above row 6, rest sites never below row 5 and never in the last three rows, no two rooms of the same special type next to each other).

The same values can be written directly into `settings.cfg`: `Act1ExtraFloors`, `Act2ExtraFloors`, `Act3ExtraFloors` (0–50), `TreasureEveryRows` (0–30), `UnknownPercent`, `ShopPercent`, `RestPercent`, `ElitePercent` (0–100).

BaseLib saves settings. Leave the configuration screen after editing. Changes apply to your **next run**, without restarting the game. A run already started keeps its original settings and maps.

Chinese game languages show Chinese labels; English and other languages show English. Reopen the configuration page after changing game language. Co-op players may use different interface languages.

On first upgrade, the mod migrates its old BonModConfig values if present; otherwise it uses `settings.cfg`. An existing BaseLib config takes priority. On Windows, the new config is normally `%APPDATA%/SlayTheSpire2/mod_configs/ActFloorCustomizer.cfg`.

## Language (v0.7.1)

- Every in-game string (config page title, slider labels, hints) follows the game language.
- The name in the mod list follows it too: 每幕自定义加层 in Chinese, `Act Floor Customizer` elsewhere. A manifest can hold only one literal name, so a small patch rewrites our own entry's labels when the screen is built; it matches only our own manifest text and leaves other mods alone.
- The Workshop page carries an English base version. The Chinese title and description have to be pasted once in the item editor (edit title and description -> language: Simplified Chinese); the text lives in `workshop/localization-zhs.md`. Steam has no API for per-language item text.
## Co-op checklist

- **From v0.7.0 the host decides.** When the host presses start, its three values are sent to everyone in the lobby; other players use those values for that run no matter what their own sliders say. Their own numbers still apply in their singleplayer runs and when they host, and nothing is written into their config file.
- Everyone installs v0.7.2 and compatible BaseLib, not just the host — game and mod versions must match anyway.
- Only the host needs to check the numbers before starting. Other players do not need to change anything.
- Loading a saved co-op run restores the floor counts from the saved encounter pools, matching what the host used at the time.
- **Still no mismatch protection and no version warning**: joining a lobby does not prove the setup is correct. The host must have this mod installed and enabled.
- Other gameplay mods must also satisfy the game's multiplayer compatibility requirements.

## How a longer map is drawn

Vanilla packs every room into one fixed-height map canvas, with row spacing = (canvas height − sum of row heights) ÷ (rows − 1). Extra floors add rows without adding canvas, so the spacing is thinned out; past roughly 23 rows it turns negative and rooms start overlapping — that is why a long map looked cramped and two rooms sometimes sat on top of each other.

v0.6.0 keeps the vanilla spacing and grows the canvas instead: the vertical distance between rows matches the same act's vanilla map, the canvas height grows with the row count, the boss marker moves up by the same amount, and the top scroll limit grows with it so the added rows stay reachable. Column spacing and room icon size are unchanged; only the map gets longer.

- With all three sliders at 0 every replacement returns the original constant, so drawing is identical to vanilla.
- The parchment background is a three-part scroll (rolled top, flat middle, rolled bottom). The two rolled ends are left completely untouched and the flat middle is filled by stacking strips of its texture at the vanilla drawn width (each strip only about 6% taller than vanilla), so the paper stays exactly as wide as the rolled ends instead of looking blown up on a long map; the sheet's bottom edge stays pinned and the paper grows upward.
- Blitz does not use this path and keeps its vanilla layout.
- Very large values (for example +30 per act) make the map extremely long and need more scrolling to reach the boss.

## Treasure rows (v0.7.2)

Vanilla adds exactly one row of treasure rooms, seven rows above the boss, so a stretched map would keep a single chest row however many floors it gained. The mod now treats that vanilla row as the first of a series and turns one more row into treasure rooms every N rows going up (default N = 7, 0 disables it). It only applies to acts that actually have extra floors, so vanilla-length acts are untouched, and the rows stay clear of the starting rooms and of the boss. Configure it with the fourth slider or with TreasureEveryRows=7 in settings.cfg.

## Special act maps (v0.7.2)

Two things replace the whole act map, and both used to ignore the extra floors:

- **Treasure map (act 2)**: it swaps in an hourglass-shaped map whose row count comes from the act's room count. That call is inlined by the game's compiler, so the room-count patch never fired there and the act stayed at 15 rows; the call site is now rewritten by a transpiler and the act gets the configured length. That map also ran the duplicate-path pruning, which enumerates every possible route — harmless at 16 rows, tens of millions of routes at 46, which is what froze a real co-op run when entering act 2. Maps longer than 20 rows now skip that pass; the generator already guarantees every point reaches the boss.
- **Golden compass**: a fixed sixteen-room straight line. It is still a straight line, but its length now follows your setting, keeping the vanilla mix of treasure, shop, elite and rest rooms, with a normal monster first and a rest site last.
## Map behavior and limits

Unknown rooms, shops, rest sites and elite budgets scale approximately with length. Normal encounter pools grow too. Native placement restrictions, the fixed treasure row, and boss rules remain. Rounding and randomness affect individual routes; elite budgets are capped, and extra treasure rows are not added proportionally.

Long maps skip the native duplicate-path pruning pass to avoid excessive generation times, so branches may be denser. Co-op retains the native one-floor reduction before applying your extras.

Existing maps are not regenerated. Standard v0.2+ saves recover their settings from saved encounter pool lengths. Start a new run when upgrading from v0.1.0. Other mods that modify maps or encounter pools need separate compatibility testing. Blitz is unsupported.

112 offline map/encounter regression cases pass, along with checks of the BaseLib config type, slider ranges, clamping and translations. v0.6.0 also verifies the drawing patches against the shipping assembly: the map height constant is replaced once, both boss offsets, and all four scroll-limit constants (`height=1, boss=2, scroll=4`), with tests asserting that stretch 1 reproduces every vanilla constant and that the boss-to-last-row gap is preserved at 1x, 2x and 3x.

Real-game evidence (2026-11-22, this machine's log): the game loaded the mod and logged `[BaseLib] Registered config for mod ActFloorCustomizer`; opening BaseLib's mod config page ran `Setting up SimpleModConfig ActFloorCustomizer.FloorConfig` and built the slider controls; quitting ran `[BaseLib] NGame.Quit(): saving all ModConfigs` and produced `%APPDATA%\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg` (10 / 10 / 10 at the time). See `evidence/verification-2026-11-22-ingame.md`.

The stretched map and parchment were confirmed on screen by the user, including the 0.7.2 width fix and the act 2 bottom edge (screenshots from the 0.5.3–0.7.2 builds). Scrolling all the way to the top of a very long map and the act-start animation were not checked separately.

The v0.7.0 co-op sync passes offline checks: host values win in a multiplayer run, singleplayer still uses the local sliders, clamping, the message's broadcast/reliable/buffered flags, and a three-byte serialize/deserialize roundtrip, on top of the 112 map/encounter cases. **It has not been run in a real two-player lobby yet.** Verify with a host and a client whose sliders differ: both should see the host's floor count in the same act. The diagnostic log should show `lobby: sent host floors` on the host, `lobby: host floors ... from` on the client, and `bindNewRun: coop=True extra=...` on both.

The 112 figure counts offline cases, not in-game or online runs.

To disable, finish your current modded run, disable Act Floor Customizer, and restart. Keep BaseLib if other mods use it.

## Diagnostic log

Diagnostics are off by default and write nothing. To turn them on, create `%APPDATA%\SlayTheSpire2\ActFloorCustomizer-test.txt` (any content; `# no switches` is enough) and restart the game: the mod then writes `%APPDATA%\SlayTheSpire2\ActFloorCustomizer-diag.log` with generation times, map-screen layout time, path node count, the background scale/position and — in co-op — the host values being sent, received and bound. Delete the file to stop logging. Other lines you can put in it: `nobg` (skip the parchment fix) and `nostretch` (vanilla map layout), both for isolating a visual problem.

## Compatibility with route-analysis mods

Tools such as Map Enhance enumerate **every** route from the start to the boss to annotate win rates, and the number of routes grows roughly exponentially with map height. On one measured map (act 1, 15 + 30 = 46 rows, 183 nodes, 234 edges) there are **50,055,744** distinct routes; the tool collects all of them into a list and sorts it, which stalls the game for a very long time at high floor counts. The game logs no error, and this mod lays the map out in about 49 ms, so the stall is not here.

Rough scale for the same map structure: ~25 rows ≈ 60k routes, ~30 rows ≈ 450k, ~35 rows ≈ 3.3M, ~46 rows ≈ 50M. Therefore:

- To keep the win-rate / pick-rate annotations: keep **each act at 15 floors or below**.
- To use higher values: turn off **route analysis** in that mod's settings (Map Config tab, or `RouteAnalysisEnabled` in its config file); its other features keep working.
- This mod's config page now shows a hint line so the limit is visible where the sliders are.

Map Enhance also costs frames even with route analysis off: its **minimap overview** rebuilds a large map preview image, and on a 46-row act it pulled the map screen down to roughly **6 FPS** with memory swinging between 0.5 GB and 3.2 GB every couple of seconds. Turning the minimap overview off (MAP MOD's Map Config tab, or `ShowMiniMapOverview` in its config file) restored the frame rate; disabling the mod entirely does the same. This mod's own drawing cost grows linearly with the row count, so whenever another map mod is installed and the map screen feels slow, lower the floor setting first.