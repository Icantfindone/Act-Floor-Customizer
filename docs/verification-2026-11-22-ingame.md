# In-game verification evidence — 2026-11-22

Source: `%APPDATA%\SlayTheSpire2\logs\godot.log`, game release `v0.113.1`, BaseLib `v3.4.8`, installed mod `ActFloorCustomizer` v0.4.0.
Line numbers refer to that log file as read on 2026-11-22. Only this mod's lines and the surrounding lifecycle lines are quoted.

## Mod loaded and registered with BaseLib's config backend

```text
8       [INFO] [Sentry.NET] Initialized: env=unknown, release=v0.113.1
899     [INFO] [BaseLib] Registered config for mod ActFloorCustomizer
939       * Act Floor Customizer / 每幕自定义加层 [ActFloorCustomizer] (0.4.0)
942     [INFO]  --- RUNNING MODDED! --- Loaded 26 mods (26 total)
```

`ModConfigRegistry.Register` reached BaseLib's real registry during mod initialization, not only in a test harness. The bilingual manifest name is what the in-game mod list shows.

## BaseLib config page was opened and built

```text
149168  [INFO] [BaseLib] Setting up SimpleModConfig ActFloorCustomizer.FloorConfig
149169  [WARN] Asset not cached: res://scenes/screens/settings_slider.tscn
149170  [WARN] Asset not cached: res://BaseLib/images/config/configbutton.png
149171  [WARN] Asset not cached: res://themes/kreon_bold_glyph_space_two.tres
```

`SetupConfigUI` ran inside a live game: BaseLib asked the game for its slider scene, config button image and font theme, which are the widgets this config needs. The three `Asset not cached` warnings are ordinary first-load cache misses, not errors.

## BaseLib saved the values on quit

```text
149176  [INFO] Quit button pressed
149177  [INFO] Quit confirmed
149178  [INFO] [BaseLib] NGame.Quit(): saving all ModConfigs
```

Resulting file:

```text
Path     : C:\Users\Bobojo\AppData\Roaming\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg
Length   : 87
Modified : 2026-11-22 01:13:02
Content  : {"Act1ExtraFloors":"10","Act2ExtraFloors":"10","Act3ExtraFloors":"10"}
```

Before this run the file did not exist; `settings.cfg` and the legacy BonModConfig JSON both held 10 / 10 / 10, so the saved values are consistent with both the read path and the migration path.

## What this does NOT prove

- Whether the sliders render with correct spacing and readable numbers, or whether the Chinese/English labels appear as intended. The log shows the widgets were requested, not how they looked.
- Whether a restart reads `mod_configs\ActFloorCustomizer.cfg` back and restores 10 / 10 / 10.
- Whether the one-time legacy JSON migration alone (without an existing BaseLib `.cfg`) produces the right values.
- A real two-computer co-op run with matching settings.

Automated coverage remains 112 offline cases (`evidence/test-results-v0.4.0.txt`); those are offline map, encounter, determinism and save/load checks, not in-game or online runs.
