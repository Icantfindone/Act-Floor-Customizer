# ActFloorCustomizer v0.4.0

See 联机安装与使用说明.md or INSTALL_AND_COOP_EN.md for installation and co-op.

Uses BaseLib.Config.SimpleModConfig and ModConfigRegistry, with three static integer properties and native ConfigSlider attributes. Scoped Harmony patches provide English/Chinese labels only for this mod. No BonModConfig assembly reference or dependency remains; legacy JSON is read only for one-time value migration.

Target: game v0.113.1, BaseLib v3.4.8.

Verification status:

- Automated tests cover config metadata, setters, clamping, localization, plus 112 offline map, encounter, determinism and save/load cases.
- Real-game log from 2026-11-22 on this machine confirms the game loaded the mod and logged `[BaseLib] Registered config for mod ActFloorCustomizer`; opening BaseLib's mod config page ran `Setting up SimpleModConfig ActFloorCustomizer.FloorConfig` and created the slider controls; quitting ran `[BaseLib] NGame.Quit(): saving all ModConfigs` and wrote `%APPDATA%\SlayTheSpire2\mod_configs\ActFloorCustomizer.cfg`. See `evidence/verification-2026-11-22-ingame.md`.
- Still unverified: how the sliders actually look and read on screen, restoring the saved `.cfg` after a game restart, and a complete real two-computer co-op run.
- The 112 figure is the number of offline cases, not 112 in-game or online playthroughs.

Source: src/ModEntry.cs and src/InGameConfig.cs. Tests: tests/Program.cs. Inspection files are local reference material, not packaged.

Build with .NET 9: `dotnet build src/ActFloorCustomizer.csproj -c Release -p:UseSharedCompilation=false`. Update the game and BaseLib reference paths in the project file on another computer. Do not distribute referenced game or third-party assemblies.
