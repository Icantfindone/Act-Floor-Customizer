# ActFloorCustomizer v0.6.0

See 联机安装与使用说明.md or INSTALL_AND_COOP_EN.md for installation and co-op.

**Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3843992717 — subscribe here and enable BaseLib; no manual file copying needed. Workshop metadata for updates lives in `workshop/`.

Adds extra floors to Acts 1, 2 and 3, configured in-game through BaseLib's own ModConfig UI. Uses `BaseLib.Config.SimpleModConfig` and `ModConfigRegistry` with three static integer properties and native `ConfigSlider` attributes. Scoped Harmony patches provide English/Chinese labels for this mod only. No BonModConfig assembly reference or dependency remains; the legacy JSON is read only for a one-time value migration.

Target: game v0.113.1, BaseLib v3.4.8.

## What v0.6.0 changes

A longer map used to be drawn on the same fixed-height canvas, so the per-row distance shrank and, past roughly 23 rows, went negative: rooms sat on top of each other. The map screen now keeps the vanilla row spacing and grows the canvas with the row count instead, moves the boss marker up by the same amount, and extends the top scroll limit so the added rows remain reachable. The parchment background is a three-part scroll whose bottom edge stays put; its height grows by exactly the amount the map grew and the parchment is stretched to fill. Column spacing and room size are unchanged. With every floor setting at 0 the replacements return the exact vanilla constants, so drawing is unchanged.

## Verification status

- Automated tests cover config metadata, setters, clamping and localization, 112 offline map/encounter/determinism/save-load cases, and the layout stretch maths. The layout transpilers are checked against the shipping assembly: they replace the map height constant once, both boss offsets, and all four scroll-limit constants (`height=1, boss=2, scroll=4`), and the tests assert that stretch 1 reproduces every vanilla constant exactly and that the boss-to-last-row gap is unchanged at stretch 1, 2 and 3.
- Real-game log from 2026-11-22 confirms BaseLib registered the mod, built the config page and saved `%APPDATA%\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg` on quit. See `evidence/verification-2026-11-22-ingame.md`.
- The author of this change could not run the game, so the stretched map has **not** been seen on screen yet: the visual result, the act-start animation and scrolling to the top of a very long map still need an in-game check.
- Co-op was tested by the user on 2026-11-22 with matching settings. Still unverified in game: how the sliders render, and restoring the saved `.cfg` after a restart.
- The 112 figure counts offline cases, not in-game or online playthroughs.

## Layout

- `src/ModEntry.cs` — settings, Harmony setup, patches on `ActModel` room counts.
- `src/InGameConfig.cs` — BaseLib config, labels, per-run setting snapshots.
- `src/MapLayout.cs` — keeps the vanilla map row spacing when extra floors are added.
- `tests/Program.cs` — the offline suite.
- `package/` — installable DLL, manifest, config and docs.
- `evidence/` — original test log and the real-game config-page log evidence.

Build with .NET 9: `dotnet build src/ActFloorCustomizer.csproj -c Release -p:UseSharedCompilation=false`. Update the game and BaseLib reference paths in the project file on another computer. Do not distribute the referenced game or third-party assemblies.
