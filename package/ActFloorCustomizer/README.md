# ActFloorCustomizer v0.4.0

See 联机安装与使用说明.md or INSTALL_AND_COOP_EN.md for installation and co-op.

Uses BaseLib.Config.SimpleModConfig and ModConfigRegistry, with three static integer properties and native ConfigSlider attributes. Scoped Harmony patches provide English/Chinese labels only for this mod. No BonModConfig assembly reference or dependency remains; legacy JSON is read only for one-time value migration.

Target: game v0.113.1, BaseLib v3.4.8. Native UI creation and BaseLib persistence still require in-game verification. Automated tests cover config metadata/setters/localization plus 112 map, encounter, determinism and save/load cases.

Source: src/ModEntry.cs and src/InGameConfig.cs. Tests: tests/Program.cs. Inspection files are local reference material, not packaged.

Build with .NET 9: `dotnet build src/ActFloorCustomizer.csproj -c Release -p:UseSharedCompilation=false`. Update the game and BaseLib reference paths in the project file on another computer. Do not distribute referenced game or third-party assemblies.
