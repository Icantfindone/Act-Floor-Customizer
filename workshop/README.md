# ActFloorCustomizer v0.7.2

See 联机安装与使用说明.md or INSTALL_AND_COOP_EN.md for installation and co-op.

**Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3843992717 — subscribe here and enable BaseLib; no manual file copying needed. Workshop metadata for updates lives in `workshop/`.

Adds extra floors to Acts 1, 2 and 3, configured in-game through BaseLib's own ModConfig UI. Uses `BaseLib.Config.SimpleModConfig` and `ModConfigRegistry` with three static integer properties and native `ConfigSlider` attributes. Scoped Harmony patches provide English/Chinese labels for this mod only. No BonModConfig assembly reference or dependency remains; the legacy JSON is read only for a one-time value migration.

Target: game v0.113.1, BaseLib v3.4.8.

## Language (v0.7.1)

A mod manifest holds one literal name and description, so the game cannot localise them by itself. The manifest now carries plain English, and a small patch rewrites the labels of this mod's own entry in the mod list to 每幕自定义加层 / the Chinese description when the game runs in `zhs`, `zht` or any other `zh` language. Only text equal to our own manifest strings is touched; nothing else on the screen is modified, and the patch is optional (`ModNameLocalization.Apply` is called in its own try/catch, so a changed mod-list screen in a future game build disables it instead of breaking the mod). Every other string — config page title, slider labels and hints — was already bilingual and still follows the game language.

The Workshop page is split the same way: `workshop/workshop.json` holds the English title and description as the base language, and `workshop/localization-zhs.md` holds the 简体中文 title and description. Both variants are now published on item 3843992717 — the English strings are the base language, the Chinese ones were written as the item's `schinese` variant through `ISteamUGC::SetItemUpdateLanguage` by the small tool in `workshop/localizer/`, because the official uploader has no language field. Chinese users see the Chinese title and description, everyone else sees the English base. The same text can still be pasted by hand in the item editor (语言 → 简体中文); `workshop/localization-zhs.md` documents both routes.

## Co-op: the host decides (v0.7.0)

In co-op every peer builds its own `RunState` from the shared seed and then generates its own encounter pools and map, so two players with different floor counts used to end up with different rooms and different maps. The host now broadcasts its three values through a mod-defined `INetMessage` while the lobby is still open, just before the begin-run message; the reliable channel delivers it first, and the receiving peers bind those values instead of their own sliders. Other players' sliders therefore do nothing in a run they did not host, while singleplayer and hosting still use the local settings. The values are only broadcast, never written to another player's config file.

## Treasure rows (v0.7.2)

Vanilla adds exactly one row of treasure rooms, seven rows above the boss, so a stretched map would keep the same single chest row no matter how many floors it gained. The mod now counts that vanilla row as the first of a series and turns one more row into treasure rooms every N rows going up (`TreasureEveryRows`, default 7, `0` disables it). It only applies to acts that actually have extra floors, so vanilla-length acts are untouched, and the rows are placed clear of the starting rooms and of the boss. The value is editable in-game (fourth slider on the config page) and in `settings.cfg` as `TreasureEveryRows=7`.

## Special act maps (v0.7.2)

Two relics/cards replace the standard act map, and both ignored the extra floors. The treasure map card swaps act 2 for an hourglass map whose constructor calls `ActModel.GetNumberOfRooms` — a call the JIT inlines, so the room-count patch never fired there and the act stayed at 15 rows. The call site is now rewritten by a transpiler, and the map gets the same scaled point budget as a standard act. That map also ran `MapPathPruning`, which enumerates every possible route and is exponential in the number of rows: harmless at 16 rows, ~50 million routes at 46 rows, which is what froze the game in a real co-op run. Maps longer than 20 rows now skip that pass (the generator already guarantees every point reaches the boss). The golden compass map was a fixed 16-room straight line; it is now stretched to the configured room count while keeping its vanilla mix and its first/last rooms.

## Map stretch (v0.6.0)

A longer map used to be drawn on the same fixed-height canvas, so the per-row distance shrank and, past roughly 23 rows, went negative: rooms sat on top of each other. The map screen now keeps the vanilla row spacing and grows the canvas with the row count instead, moves the boss marker up by the same amount, and extends the top scroll limit so the added rows remain reachable. The parchment background is a three-part scroll: the two rolled ends are left completely untouched and the flat middle is filled by stacking strips of the middle texture at the vanilla drawn width (each strip only about 6% taller than vanilla), so the sheet grows upward with its bottom edge pinned in place and the paper stays exactly as wide as the rolled ends. Column spacing and room size are unchanged. With every floor setting at 0 the replacements return the exact vanilla constants, so drawing is unchanged.

## Long maps and other map mods

Extra floors multiply the work every map-drawing mod does per frame, so a mod that is cheap on a 16-row act can become very expensive on a 46-row one. Measured on a 30-floor setup (Ryzen, 60 FPS baseline): with the Map Enhance workshop mod active, the map screen dropped to roughly 6 FPS and its memory swung between 0.5 GB and 3.2 GB every couple of seconds — that mod rebuilds a large map overview image; switching its minimap overview off (or disabling the mod) restored the frame rate. Its route analysis is worse than expensive: it enumerates every route, which is ~50 million on a 46-row map, so keep the floor setting at or below 15 while route analysis is enabled. Turning the extra floors down is the reliable fix for any such mod; this mod's own drawing cost is linear in the row count.

## Verification status

- Automated tests cover config metadata, setters, clamping and localization, 112 offline map/encounter/determinism/save-load cases, the layout stretch maths, and the co-op rule: host values win in a multiplayer run, local sliders still win in singleplayer, clamping, the message contract (broadcast, reliable, buffered) and a three-byte serialize/deserialize roundtrip. The layout transpilers are checked against the shipping assembly: they replace the map height constant once, both boss offsets, and all four scroll-limit constants (`height=1, boss=2, scroll=4`), and the tests assert that stretch 1 reproduces every vanilla constant exactly and that the boss-to-last-row gap is unchanged at stretch 1, 2 and 3.
- Real-game log from 2026-11-22 confirms BaseLib registered the mod, built the config page and saved `%APPDATA%\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg` on quit. See `evidence/verification-2026-11-22-ingame.md`.
- The stretched map and the parchment were confirmed on screen by the user; scrolling to the very top of a long map was not separately checked.
- **The v0.7.0 co-op sync has been confirmed in a real two-player lobby** (2026-11-23): with the host on 30/30/30 and the client on 0/0/0, the client's diagnostics recorded `lobby: host floors 30/30/30 from …`, `bindNewRun: coop=True extra=30/30/30`, and both peers built the same 45/44/43-room acts. Diagnostics are opt-in and off by default: drop a `%APPDATA%\SlayTheSpire2\ActFloorCustomizer-test.txt` to enable `ActFloorCustomizer-diag.log`, delete it to stop.
- The v0.7.1 in-game language split was confirmed by the user for both languages (config page, labels and mod-list name follow the game language); the patch itself is also covered by the offline harness.
- The 112 figure counts offline cases, not in-game or online playthroughs.
- v0.7.2: the treasure-row series, the special-map fixes (spoils rows, pruning guard, golden path stretch) and the parchment change are covered by the offline suite; the in-game look of the longer parchment (width matching the rolled ends, no overhang at the bottom of act 2) and the 45-row spoils map and the bonus treasure rows were then confirmed on screen by the user. The v0.7.0 co-op sync has since been confirmed in a real lobby through the diagnostics (`lobby: host floors 30/30/30 from …`, `bindNewRun: coop=True`).

## Layout

- `src/ModEntry.cs` — settings, Harmony setup, patches on `ActModel` room counts.
- `src/InGameConfig.cs` — BaseLib config, labels, per-run setting snapshots, the co-op rule.
- `src/HostSync.cs` — host floor broadcast, lobby hooks, the net message.
- `src/ModNameLocalization.cs` — makes the mod-list name follow the game language.
- `src/MapLayout.cs` — keeps the vanilla map row spacing when extra floors are added.
- `src/MapCompat.cs` — rules for the special act maps (treasure map, golden compass) and the bonus treasure rows.
- `tests/Program.cs` — the offline suite.
- `package/` — installable DLL, manifest, config and docs.
- `evidence/` — original test log and the real-game config-page log evidence.

Build with .NET 9: `dotnet build src/ActFloorCustomizer.csproj -c Release -p:UseSharedCompilation=false`. Update the game and BaseLib reference paths in the project file on another computer. Do not distribute the referenced game or third-party assemblies.
