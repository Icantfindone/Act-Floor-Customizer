# ActFloorCustomizer v0.7.0

See 联机安装与使用说明.md or INSTALL_AND_COOP_EN.md for installation and co-op.

**Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3843992717 — subscribe here and enable BaseLib; no manual file copying needed. Workshop metadata for updates lives in `workshop/`.

Adds extra floors to Acts 1, 2 and 3, configured in-game through BaseLib's own ModConfig UI. Uses `BaseLib.Config.SimpleModConfig` and `ModConfigRegistry` with three static integer properties and native `ConfigSlider` attributes. Scoped Harmony patches provide English/Chinese labels for this mod only. No BonModConfig assembly reference or dependency remains; the legacy JSON is read only for a one-time value migration.

Target: game v0.113.1, BaseLib v3.4.8.

## Co-op: the host decides (v0.7.0)

In co-op every peer builds its own `RunState` from the shared seed and then generates its own encounter pools and map, so two players with different floor counts used to end up with different rooms and different maps. The host now broadcasts its three values through a mod-defined `INetMessage` while the lobby is still open, just before the begin-run message; the reliable channel delivers it first, and the receiving peers bind those values instead of their own sliders. Other players' sliders therefore do nothing in a run they did not host, while singleplayer and hosting still use the local settings. The values are only broadcast, never written to another player's config file.

## Map stretch (v0.6.0)

A longer map used to be drawn on the same fixed-height canvas, so the per-row distance shrank and, past roughly 23 rows, went negative: rooms sat on top of each other. The map screen now keeps the vanilla row spacing and grows the canvas with the row count instead, moves the boss marker up by the same amount, and extends the top scroll limit so the added rows remain reachable. The parchment background is a three-part scroll whose bottom edge stays put; its height grows by exactly the amount the map grew and the parchment is stretched to fill. Column spacing and room size are unchanged. With every floor setting at 0 the replacements return the exact vanilla constants, so drawing is unchanged.

## Verification status

- Automated tests cover config metadata, setters, clamping and localization, 112 offline map/encounter/determinism/save-load cases, the layout stretch maths, and the co-op rule: host values win in a multiplayer run, local sliders still win in singleplayer, clamping, the message contract (broadcast, reliable, buffered) and a three-byte serialize/deserialize roundtrip. The layout transpilers are checked against the shipping assembly: they replace the map height constant once, both boss offsets, and all four scroll-limit constants (`height=1, boss=2, scroll=4`), and the tests assert that stretch 1 reproduces every vanilla constant exactly and that the boss-to-last-row gap is unchanged at stretch 1, 2 and 3.
- Real-game log from 2026-11-22 confirms BaseLib registered the mod, built the config page and saved `%APPDATA%\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg` on quit. See `evidence/verification-2026-11-22-ingame.md`.
- The stretched map and the parchment were confirmed on screen by the user; scrolling to the very top of a long map was not separately checked.
- **The v0.7.0 co-op sync has not been run in a real lobby yet.** It has to be confirmed by a host and a client whose sliders differ: both peers should end up with the host's floor counts in the same act. Diagnostics for that check are written to `%APPDATA%\SlayTheSpire2\ActFloorCustomizer-diag.log` (`lobby: sent host floors`, `lobby: host floors ... from`, `bindNewRun`).
- The 112 figure counts offline cases, not in-game or online playthroughs.

## Layout

- `src/ModEntry.cs` — settings, Harmony setup, patches on `ActModel` room counts.
- `src/InGameConfig.cs` — BaseLib config, labels, per-run setting snapshots, the co-op rule.
- `src/HostSync.cs` — host floor broadcast, lobby hooks, the net message.
- `src/MapLayout.cs` — keeps the vanilla map row spacing when extra floors are added.
- `tests/Program.cs` — the offline suite.
- `package/` — installable DLL, manifest, config and docs.
- `evidence/` — original test log and the real-game config-page log evidence.

Build with .NET 9: `dotnet build src/ActFloorCustomizer.csproj -c Release -p:UseSharedCompilation=false`. Update the game and BaseLib reference paths in the project file on another computer. Do not distribute the referenced game or third-party assemblies.
