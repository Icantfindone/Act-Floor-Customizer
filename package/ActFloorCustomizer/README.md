# ActFloorCustomizer v0.8.2

Add extra floors to Acts 1, 2 and 3 of Slay the Spire 2 — up to 50 per act — with the map, the
parchment background, the treasure rows and the room mix all adjusting to the new length.

- **Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3843992717
- **中文说明:** [README.zhs.md](README.zhs.md)
- **完整更新日志:** [CHANGELOG.md](CHANGELOG.md) / [CHANGELOG.zhs.md](CHANGELOG.zhs.md)

Target: game v0.113.1, BaseLib v3.4.8.

## What it does

| Feature | What it changes |
|---|---|
| Extra floors | 0–50 extra floors per act, set on BaseLib's own config page (or in `settings.cfg`). |
| Map stretch | The map keeps the vanilla row spacing and grows instead of squeezing rooms together; the boss markers move up and the scroll limit is extended. |
| Rolled parchment | The two rolled ends stay untouched; the flat middle is stacked from strips at the vanilla width, so the sheet gets longer without looking stretched. |
| Treasure rows | One more row of treasure rooms every N rows (default 7, `0` disables), counted from the vanilla treasure row upwards, on stretched acts only. |
| Room mix | Shares for unknown rooms, shops, rest sites and elites; the rest stays a normal fight. `0/0/0/0` keeps the vanilla mix. |
| Co-op | The host's floor values and room mix are sent to everyone when the run starts; other players' sliders are ignored. |
| Special act maps | The treasure map act and the golden compass map follow the configured length instead of their hard-coded vanilla length. |
| Language | Config page, mod-list entry and hints follow the game language (English / 简体中文 / 繁體中文). |

## Install

**Workshop (easiest):** subscribe to the item above, install [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127), launch the game.

**Manual:** copy the `ActFloorCustomizer` folder from the release zip into
`Slay the Spire 2\mods\` (the folder holds `ActFloorCustomizer.dll` and `ActFloorCustomizer.json`).
`settings.cfg` is optional; the mod runs without it.

Co-op: every player needs the mod and BaseLib installed; the host's numbers decide the run.

Full installation and co-op notes: [INSTALL_AND_COOP_EN.md](INSTALL_AND_COOP_EN.md) /
[联机安装与使用说明.md](联机安装与使用说明.md).

## Configure

Open the mod config page from the main menu (BaseLib adds a "Mod Config" entry) and use the three
sections:

- **Floors per act** — the three floor sliders (0–50).
- **Treasure rows** — one row of treasure rooms every N rows (0–30, default 7).
- **Room mix** — unknown / shop / rest / elite shares in percent; the remainder is a normal fight.

Settings are saved automatically and apply to the next run; the numbers can also be edited in
`settings.cfg` (`Act1ExtraFloors`, `Act2ExtraFloors`, `Act3ExtraFloors`, `TreasureEveryRows`,
`UnknownPercent`, `ShopPercent`, `RestPercent`, `ElitePercent`).

## Known compatibility issues

- **Mods that enumerate every route** (e.g. Map Enhance's route analysis) are exponential in the row
  count: about 50 million routes on a 46-row act. Keep each act at 15 floors or below while such a
  mod is enabled.
- **Mods that draw a large minimap overview** rebuild a big map preview and can drop the map screen
  to a few FPS on very long acts; turn the overview off or lower the floor count.
- **Mods that rewrite combat pools or the map itself** may conflict — test before committing to a run.

Details are in the changelog.

## This release: 0.8.2

- The treasure map's "X marks the spot" marker now lands on the special chest row instead of the
  first chest of a bonus row. The special chest is the only chest of that row again, and the
  ordinary treasure rows keep their whole row.
- `README.md` only describes the mod and the current release; the full history moved to the
  changelog, and both files now exist in English and Chinese.

The full list of changes for every version is in [CHANGELOG.md](CHANGELOG.md).

## Verification status

- The offline suite covers 128 map/encounter/determinism/save-load cases over 0–50 extra floors,
  the layout transpilers against the shipping assembly, config metadata/translation, the co-op rule
  (host values and host room mix win in multiplayer, local sliders win in singleplayer) and the room
  mix (normalisation, determinism, vanilla pacing rules).
- The stretched map, the stacked parchment, the treasure rows, the special act maps and the co-op
  host sync were confirmed in game; the co-op sync was verified in a real two-player lobby through
  the diagnostics.
- The 0.8.0+ room mix and the 50-floor setting have not been played in a full run yet.
- Diagnostics are opt-in: create `%APPDATA%\SlayTheSpire2\ActFloorCustomizer-test.txt` to get
  `ActFloorCustomizer-diag.log`, delete it to stop. A normal install writes no log.

## Layout

- `src/ModEntry.cs` — settings, Harmony setup, patches on `ActModel` room counts.
- `src/InGameConfig.cs` — BaseLib config page, labels, per-run setting snapshots, the co-op rule.
- `src/HostSync.cs` — host broadcast of floors and room mix, lobby hooks, the net message.
- `src/ModNameLocalization.cs` — makes the mod-list name follow the game language.
- `src/MapLayout.cs` — keeps the vanilla row spacing and rebuilds the parchment.
- `src/RoomMix.cs` — the room-share re-roll and its pacing rules.
- `src/MapCompat.cs` — rules for the special act maps (treasure map, golden compass) and the bonus
  treasure rows.
- `tests/Program.cs` — the offline suite.
- `package/` — installable DLL, manifest, config and docs.
- `workshop/` — Workshop metadata, the Chinese localisation of the page and the publishing tool.

Build with .NET 9: `dotnet build src/ActFloorCustomizer.csproj -c Release -p:UseSharedCompilation=false`.
Do not redistribute the referenced game or third-party assemblies.
