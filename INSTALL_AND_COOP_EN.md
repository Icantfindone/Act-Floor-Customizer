# Act Floor Customizer v0.5.5 — installation and co-op

Targets **Slay the Spire 2 v0.113.1**. Uses **BaseLib's built-in ModConfig**, compiled against the installed BaseLib v3.4.8 API.

## Install on every player's computer

1. Close the game. Subscribe to and enable [BaseLib by Alchyr](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127), including its supplied compatibility helper components.
2. Extract `ActFloorCustomizer-v0.5.5-sts2-v0.113.1.zip`.
3. In Steam, right-click the game → Manage → Browse local files. Copy the extracted `ActFloorCustomizer` folder into the game's `mods` folder. Create `mods` if necessary.
4. Launch the game, enable the mod, and restart if prompted. When updating, replace existing files rather than keeping multiple versions.

Expected path: `Slay the Spire 2/mods/ActFloorCustomizer/ActFloorCustomizer.dll`. The manifest `ActFloorCustomizer.json` must be beside it.

**Boninall's Mod Config is no longer a dependency.** Do not uninstall it if your other mods need it. The ZIP does not bundle BaseLib or game DLLs; install the dependency separately.

## Configure in the game

Open **BaseLib's Mod Config menu → Act Floor Customizer**.

The three sliders set extra regular floors for Acts 1, 2 and 3. Each accepts 0–30; default: 5. Zero keeps that act's normal length. For example, `10 / 10 / 10` adds ten floors per act; it does not set the total to ten.

BaseLib saves settings. Leave the configuration screen after editing. Changes apply to your **next run**, without restarting the game. A run already started keeps its original settings and maps.

Chinese game languages show Chinese labels; English and other languages show English. Reopen the configuration page after changing game language. Co-op players may use different interface languages.

On first upgrade, the mod migrates its old BonModConfig values if present; otherwise it uses `settings.cfg`. An existing BaseLib config takes priority. On Windows, the new config is normally `%APPDATA%/SlayTheSpire2/mod_configs/ActFloorCustomizer.cfg`.

## Co-op checklist

- Everyone installs version 0.5.2 and compatible BaseLib, not just the host.
- Everyone uses the same game version and exactly the same three slider values. Compare screenshots before starting.
- **No automatic host settings sync or mismatch protection is provided.** Joining a lobby does not prove that settings match.
- Set values before creating a lobby. If values change afterward, leave and create a new lobby.
- Other gameplay mods must also satisfy the game's multiplayer compatibility requirements.

## How a longer map is drawn

Vanilla packs every room into one fixed-height map canvas, with row spacing = (canvas height − sum of row heights) ÷ (rows − 1). Extra floors add rows without adding canvas, so the spacing is thinned out; past roughly 23 rows it turns negative and rooms start overlapping — that is why a long map looked cramped and two rooms sometimes sat on top of each other.

v0.5.5 keeps the vanilla spacing and grows the canvas instead: the vertical distance between rows matches the same act's vanilla map, the canvas height grows with the row count, the boss marker moves up by the same amount, and the top scroll limit grows with it so the added rows stay reachable. Column spacing and room icon size are unchanged; only the map gets longer.

- With all three sliders at 0 every replacement returns the original constant, so drawing is identical to vanilla.
- The parchment background grows with the map: it is a three-part scroll whose bottom edge stays put and whose height grows by exactly the amount the map grew, with the parchment stretched to fill. No gaps, and the three pieces keep a consistent width.
- Blitz does not use this path and keeps its vanilla layout.
- Very large values (for example +30 per act) make the map extremely long and need more scrolling to reach the boss.

## Map behavior and limits

Unknown rooms, shops, rest sites and elite budgets scale approximately with length. Normal encounter pools grow too. Native placement restrictions, the fixed treasure row, and boss rules remain. Rounding and randomness affect individual routes; elite budgets are capped, and extra treasure rows are not added proportionally.

Long maps skip the native duplicate-path pruning pass to avoid excessive generation times, so branches may be denser. Co-op retains the native one-floor reduction before applying your extras.

Existing maps are not regenerated. Standard v0.2+ saves recover their settings from saved encounter pool lengths. Start a new run when upgrading from v0.1.0. Other mods that modify maps or encounter pools need separate compatibility testing. Blitz is unsupported.

112 offline map/encounter regression cases pass, along with checks of the BaseLib config type, slider ranges, clamping and translations. v0.5.5 also verifies the drawing patches against the shipping assembly: the map height constant is replaced once, both boss offsets, and all four scroll-limit constants (`height=1, boss=2, scroll=4`), with tests asserting that stretch 1 reproduces every vanilla constant and that the boss-to-last-row gap is preserved at 1x, 2x and 3x.

Real-game evidence (2026-11-22, this machine's log): the game loaded the mod and logged `[BaseLib] Registered config for mod ActFloorCustomizer`; opening BaseLib's mod config page ran `Setting up SimpleModConfig ActFloorCustomizer.FloorConfig` and built the slider controls; quitting ran `[BaseLib] NGame.Quit(): saving all ModConfigs` and produced `%APPDATA%\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg` (10 / 10 / 10 at the time). See `evidence/verification-2026-11-22-ingame.md`.

Co-op was tested by the user on 2026-11-22 with matching settings and versions.

Still not verified in game: how the stretched map actually looks (row spacing, boss placement, scrolling to the very top), the act-start animation, how the sliders render, and restoring the saved `.cfg` after a restart. The 112 figure counts offline cases, not in-game or online runs.

To disable, finish your current modded run, disable Act Floor Customizer, and restart. Keep BaseLib if other mods use it.

## Diagnostic log

The mod writes `ActFloorCustomizer-diag.log` next to its DLL (the game does not keep standard output). Each map records generation time, map-screen layout time, path node count and the background scale/position. Attach that file when reporting a stutter or a visual glitch.
## Compatibility with route-analysis mods

Tools such as Map Enhance enumerate **every** route from the start to the boss to annotate win rates, and the number of routes grows roughly exponentially with map height. On one measured map (act 1, 15 + 30 = 46 rows, 183 nodes, 234 edges) there are **50,055,744** distinct routes; the tool collects all of them into a list and sorts it, which stalls the game for a very long time at high floor counts. The game logs no error, and this mod lays the map out in about 49 ms, so the stall is not here.

Rough scale for the same map structure: ~25 rows ≈ 60k routes, ~30 rows ≈ 450k, ~35 rows ≈ 3.3M, ~46 rows ≈ 50M. Therefore:

- To keep the win-rate / pick-rate annotations: keep **each act at 15 floors or below**.
- To use higher values: turn off **route analysis** in that mod's settings (Map Config tab, or `RouteAnalysisEnabled` in its config file); its other features keep working.
- This mod's config page now shows a hint line so the limit is visible where the sliders are.