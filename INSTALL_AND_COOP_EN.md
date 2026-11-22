# Act Floor Customizer v0.4.0 — installation and co-op

Targets **Slay the Spire 2 v0.113.1**. Uses **BaseLib's built-in ModConfig**, compiled against the installed BaseLib v3.4.8 API.

## Install on every player's computer

1. Close the game. Subscribe to and enable [BaseLib by Alchyr](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127), including its supplied compatibility helper components.
2. Extract `ActFloorCustomizer-v0.4.0-sts2-v0.113.1.zip`.
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

- Everyone installs version 0.4.0 and compatible BaseLib, not just the host.
- Everyone uses the same game version and exactly the same three slider values. Compare screenshots before starting.
- **No automatic host settings sync or mismatch protection is provided.** Joining a lobby does not prove that settings match.
- Set values before creating a lobby. If values change afterward, leave and create a new lobby.
- Other gameplay mods must also satisfy the game's multiplayer compatibility requirements.

## Map behavior and limits

Unknown rooms, shops, rest sites and elite budgets scale approximately with length. Normal encounter pools grow too. Native placement restrictions, the fixed treasure row, and boss rules remain. Rounding and randomness affect individual routes; elite budgets are capped, and extra treasure rows are not added proportionally.

Long maps skip the native duplicate-path pruning pass to avoid excessive generation times, so branches may be denser. Co-op retains the native one-floor reduction before applying your extras.

Existing maps are not regenerated. Standard v0.2+ saves recover their settings from saved encounter pool lengths. Start a new run when upgrading from v0.1.0. Other mods that modify maps or encounter pools need separate compatibility testing. Blitz is unsupported.

112 offline map/encounter regression cases pass, along with checks of the BaseLib config type, slider ranges, clamping and translations.

Real-game evidence (2026-11-22, this machine's log): the game loaded the mod and logged `[BaseLib] Registered config for mod ActFloorCustomizer`; opening BaseLib's mod config page ran `Setting up SimpleModConfig ActFloorCustomizer.FloorConfig` and built the slider controls; quitting ran `[BaseLib] NGame.Quit(): saving all ModConfigs` and produced `%APPDATA%\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg` (10 / 10 / 10 at the time). See `evidence/verification-2026-11-22-ingame.md`.

Still not verified in game: how the sliders actually look and read on screen, restoring the saved `.cfg` after a restart, and a complete real two-computer co-op run. The 112 figure counts offline cases, not in-game or online runs.

To disable, finish your current modded run, disable Act Floor Customizer, and restart. Keep BaseLib if other mods use it.
