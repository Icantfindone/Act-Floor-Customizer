using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using System.Reflection;

namespace ActFloorCustomizer;

// The mod manifest only carries one literal name/description, so the mod list shows English
// even for Chinese players. Rewrite the visible labels of our own mod entry to the language
// the game is running in. Only exact matches are touched, so nothing else on the screen moves.
public static class ModNameLocalization
{
    public const string EnglishName = "Act Floor Customizer";
    // Name written by older builds; still handled so an old install looks right before it updates.
    public const string LegacyName = "Act Floor Customizer / 每幕自定义加层";

    public static string Replace(string? text, string? language)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        string trimmed = text.Trim();
        if (trimmed == EnglishName || trimmed == LegacyName)
            return InGameConfig.Translate("Name", language);
        return text;
    }

    private static bool NeedsRewrite(string? text, string language)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string trimmed = text.Trim();
        if (trimmed != EnglishName && trimmed != LegacyName) return false;
        return Replace(text, language) != text;
    }

    // Rewrites our mod's label text when the game runs in Chinese. Returns how many nodes changed.
    public static int Rewrite(Node root, string? name, string? description, string? language)
    {
        if (root == null || string.IsNullOrEmpty(language)) return 0;
        int changed = 0;
        foreach (Node child in root.GetChildren())
        {
            if (child is Node node)
            {
                changed += RewriteNode(node, name, description, language);
                changed += Rewrite(node, name, description, language);
            }
        }
        return changed;
    }

    private static int RewriteNode(Node node, string? name, string? description, string? language)
    {
        int changed = 0;
        foreach (PropertyInfo property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string) || property.Name != "Text" || !property.CanWrite) continue;
            string? current = property.GetValue(node) as string;
            if (string.IsNullOrEmpty(current)) continue;
            string? replacement = null;
            if (NeedsRewrite(current, language!)) replacement = Replace(current, language);
            else if (description != null && current.Trim() == description.Trim())
                replacement = InGameConfig.Translate("Desc", language);
            if (replacement == null || replacement == current) continue;
            try
            {
                property.SetValue(node, replacement);
                changed++;
            }
            catch (Exception ex)
            {
                MapLayout.Diag("modname: cannot rewrite label: " + ex.Message);
            }
        }
        return changed;
    }

    private static void FillPostfix(object __instance, Mod __0)
    {
        try
        {
            if (__instance is not Node root) return;
            string? language = LocManager.Instance?.Language;
            if (!InGameConfig.IsChinese(language)) return;
            string? name = ManifestField(__0, "name");
            string? description = ManifestField(__0, "description");
            if (name == null && description == null) return;
            int changed = Rewrite(root, name, description, language);
            if (changed > 0) MapLayout.Diag($"modname: rewrote {changed} label(s) to {language}");
            // Some screens fill their labels one frame later.
            Callable.From(() =>
            {
                int later = Rewrite(root, name, description, language);
                if (later > 0) MapLayout.Diag($"modname: rewrote {later} label(s) on the next frame");
            }).CallDeferred();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ActFloorCustomizer] Mod name localisation: " + ex.Message);
        }
    }

    private static string? ManifestField(Mod? mod, string field)
    {
        if (mod == null) return null;
        try
        {
            object? manifest = AccessTools.Field(typeof(Mod), "manifest")?.GetValue(mod);
            if (manifest == null) return null;
            MemberInfo? member = (MemberInfo?)manifest.GetType().GetField(field) ?? manifest.GetType().GetProperty(field);
            return member switch
            {
                FieldInfo f => f.GetValue(manifest) as string,
                PropertyInfo p => p.GetValue(manifest) as string,
                _ => null
            };
        }
        catch (Exception ex)
        {
            MapLayout.Diag("modname: cannot read manifest field: " + ex.Message);
            return null;
        }
    }

    private static void NamePostfix(ref string __result)
    {
        try
        {
            string? language = LocManager.Instance?.Language;
            if (InGameConfig.IsChinese(language)) __result = Replace(__result, language);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ActFloorCustomizer] Mod name localisation: " + ex.Message);
        }
    }

    // Called from ModEntry.LoadAndPatch. Kept out of PatchAll so a missing UI type cannot
    // take the whole mod down.
    public static void Apply(Harmony harmony)
    {
        try
        {
            MethodInfo? fill = AccessTools.Method(typeof(NModInfoContainer), "Fill");
            if (fill != null)
                harmony.Patch(fill, postfix: new HarmonyMethod(typeof(ModNameLocalization), nameof(FillPostfix)));
            else Console.WriteLine("[ActFloorCustomizer] Mod list screen changed; name localisation disabled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ActFloorCustomizer] Mod list name patch: " + ex.Message);
        }
        try
        {
            Type? button = AccessTools.TypeByName("BaseLib.Config.UI.NModListButton");
            MethodInfo? getter = button == null ? null : AccessTools.PropertyGetter(button, "ModName");
            if (getter != null)
                harmony.Patch(getter, postfix: new HarmonyMethod(typeof(ModNameLocalization), nameof(NamePostfix)));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ActFloorCustomizer] Config list name patch: " + ex.Message);
        }
    }
}
