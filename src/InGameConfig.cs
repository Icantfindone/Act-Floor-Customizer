using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Localization;

namespace ActFloorCustomizer;

public static class InGameConfig
{
    public const string Id = "ActFloorCustomizer";
    public static bool Registered { get; private set; }
    public static bool IsChinese(string? language) =>
        language is "zhs" or "zht" || (language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ?? false);
    public static string Translate(string key, string? language)
    {
        bool chinese = IsChinese(language);
        return key switch
        {
            "Name" => chinese ? "每幕自定义加层" : "Act Floor Customizer",
            "Desc" => chinese
                ? "每幕增加 0～30 层；地图按原版间距纵向拉伸；联机以房主设置为准。"
                : "Add 0-30 extra floors per act. The map stretches to keep vanilla spacing; in co-op the host's values apply.",
            "NextRun" => chinese ? "自动保存，下一局生效，无需重启" : "Auto-saved. Applies to your next run. No restart needed.",
            "Act1" => chinese ? "第一幕额外层数" : "Act 1: extra floors",
            "Act2" => chinese ? "第二幕额外层数" : "Act 2: extra floors",
            "Act3" => chinese ? "第三幕额外层数" : "Act 3: extra floors",
            "ExtraHint" => chinese ? "在原版层数上增加；0 表示不增加。" : "Added to the normal floor count. Set to 0 for no extra floors.",
            "Coop" => chinese
                ? "联机：以房主的三个数字为准，其他人改了也不生效。"
                : "Co-op: the host's three values are used by everyone. Other players' own numbers are ignored.",
            "PerfHint" => chinese
                ? "注意：地图增强的路线分析会穷举所有路线，层级太高会卡死。建议每幕不超过 15 层。"
                : "Note: the map mod's route analysis enumerates every route and stalls on very long maps. Keep each act at 15 floors or below.",
            "CurrentRun" => chinese ? "当前对局保留原设置，地图不会重画" : "Your current run keeps its settings and existing maps.",
            "Treasure" => chinese ? "每隔多少行加一排宝箱" : "Treasure row every N rows",
            "TreasureHint" => chinese
                ? "仅对加层后的地图生效：每 N 行把整排房间变成宝箱房（和原版宝箱房一样），0 表示不加。"
                : "Only for stretched maps: every N rows the whole row becomes treasure rooms, like the vanilla treasure row. 0 disables it.",
            _ => key
        };
    }
    public static string Localize(string key) => Translate(key, LocManager.Instance?.Language);
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register()
    {
        if (Registered) return;
        // BaseLib loads its persisted .cfg in the constructor; legacy values are only defaults.
        string dataDir = OS.GetUserDataDir();
        if (!File.Exists(Path.Combine(dataDir, "mod_configs", "ActFloorCustomizer.cfg")))
        {
            string legacy = Path.Combine(dataDir, "mod_config", "ActFloorCustomizer.json");
            if (File.Exists(legacy))
            {
                try
                {
                    using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(legacy));
                    var values = json.RootElement.GetProperty("values");
                    FloorConfig.Act1ExtraFloors = values.GetProperty("Act1ExtraFloors").GetInt32();
                    FloorConfig.Act2ExtraFloors = values.GetProperty("Act2ExtraFloors").GetInt32();
                    FloorConfig.Act3ExtraFloors = values.GetProperty("Act3ExtraFloors").GetInt32();
                }
                catch (Exception ex) { Console.WriteLine("[ActFloorCustomizer] Legacy config migration: " + ex.Message); }
            }
        }
        ModConfigRegistry.Register(Id, new FloorConfig());
        Registered = true;
    }
}

public sealed class FloorConfig : SimpleModConfig
{
    [ConfigSlider(0, 30, 1, Format = "{0:0}")]
    public static int Act1ExtraFloors { get => ModEntry.Settings.Act1ExtraFloors; set => ModEntry.Settings.Act1ExtraFloors = Math.Clamp(value, 0, 30); }
    [ConfigSlider(0, 30, 1, Format = "{0:0}")]
    public static int Act2ExtraFloors { get => ModEntry.Settings.Act2ExtraFloors; set => ModEntry.Settings.Act2ExtraFloors = Math.Clamp(value, 0, 30); }
    [ConfigSlider(0, 30, 1, Format = "{0:0}")]
    public static int Act3ExtraFloors { get => ModEntry.Settings.Act3ExtraFloors; set => ModEntry.Settings.Act3ExtraFloors = Math.Clamp(value, 0, 30); }
    [ConfigSlider(0, 30, 1, Format = "{0:0}")]
    public static int TreasureEveryRows { get => ModEntry.Settings.TreasureEveryRows; set => ModEntry.Settings.TreasureEveryRows = Math.Clamp(value, 0, 30); }

    public override void SetupConfigUI(Control optionContainer)
    {
        optionContainer.AddChild(CreateRawLabelControl(InGameConfig.Localize("NextRun"), 24));
        optionContainer.AddChild(CreateRawLabelControl(InGameConfig.Localize("Coop"), 24));
        optionContainer.AddChild(CreateRawLabelControl(InGameConfig.Localize("TreasureHint"), 24));
        optionContainer.AddChild(CreateRawLabelControl(InGameConfig.Localize("PerfHint"), 24));
        base.SetupConfigUI(optionContainer);
    }
}

// Scope localization to this config only; other BaseLib settings are unaffected.
[HarmonyPatch(typeof(ModConfig), "GetLabelText", new Type[] { typeof(string), typeof(bool) })]
internal static class BaseLibLabelPatch
{
    static bool Prefix(ModConfig __instance, string labelName, ref string __result)
    {
        if (__instance is not FloorConfig) return true;
        string? key = labelName switch { "Act1ExtraFloors" => "Act1", "Act2ExtraFloors" => "Act2", "Act3ExtraFloors" => "Act3", "TreasureEveryRows" => "Treasure", _ => null };
        if (key == null) return true;
        __result = InGameConfig.Localize(key);
        return false;
    }
}

[HarmonyPatch(typeof(BaseLib.Config.UI.NModConfigSubmenu), "GetModTitle")]
internal static class BaseLibTitlePatch
{
    static bool Prefix(ModConfig config, ref string __result)
    {
        if (config is not FloorConfig) return true;
        __result = InGameConfig.Localize("Name");
        return false;
    }
}
public static class RunSettings
{
    private sealed record Snapshot(int Extra);
    private static readonly ConditionalWeakTable<ActModel, Snapshot> Snapshots = new();
    public static int ForAct(ActModel act) => Snapshots.TryGetValue(act, out var value) ? value.Extra : ModEntry.Settings.ForAct(act.Index);
    public static void Bind(ActModel act, int extra)
    {
        Snapshots.Remove(act);
        Snapshots.Add(act, new Snapshot(Math.Clamp(extra, 0, 30)));
    }

    // All acts are bound before the native encounter pools are generated. In co-op every peer builds
    // its own RunState from the same seed, so the host's numbers must win here or the peers would
    // generate different rooms and different maps.
    public static void BindNewRun(RunState state)
    {
        bool coop = state.Players.Count > 1;
        foreach (var act in state.Acts) Bind(act, ExtraForNewRun(act.Index, coop));
        if (coop) MapLayout.Diag($"bindNewRun: coop={coop} extra={ExtraForNewRun(0, coop)}/{ExtraForNewRun(1, coop)}/{ExtraForNewRun(2, coop)}");
    }

    // Single rule for the whole feature: in co-op the host's three numbers apply to every peer,
    // everywhere else the local sliders apply.
    public static int ExtraForNewRun(int actIndex, bool coop)
    {
        if (coop && HostSync.TryGet(out int act1, out int act2, out int act3))
            return Math.Clamp(actIndex switch { 0 => act1, 1 => act2, 2 => act3, _ => 0 }, 0, 30);
        return ModEntry.Settings.ForAct(actIndex);
    }

    // v0.2+ saves already store the full encounter pools for every act. Recover the
    // run's original length from those pools, not the newly edited menu defaults.
    public static void BindLoadedRun(RunState state)
    {
        foreach (var act in state.Acts)
            BindLoadedAct(act, state.Players.Count > 1);
    }
    public static void BindLoadedAct(ActModel act, bool multiplayer)
    {
        int vanilla = (int)AccessTools.Property(act.GetType(), "BaseNumberOfRooms").GetValue(act)! - (multiplayer ? 1 : 0);
        Bind(act, act.GetRoomsDebug().normalEncounters.Count - vanilla);
    }
}

[HarmonyPatch(typeof(RunState), nameof(RunState.CreateForNewRun))]
internal static class NewRunSettingsPatch
{
    static void Postfix(RunState __result) => RunSettings.BindNewRun(__result);
}

[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
internal static class LoadedRunSettingsPatch
{
    static void Postfix(RunState __result) => RunSettings.BindLoadedRun(__result);
}


