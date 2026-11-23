using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace ActFloorCustomizer;

/// <summary>
/// Temporary diagnostics for the freeze when the game enters a new act.
/// The last line written before a freeze tells which stage of map generation never returned,
/// and the model type that was running at that moment.
/// </summary>
public static class GenProbe
{
    private static readonly string[] HookNames =
    {
        "ModifyGeneratedMap",
        "ModifyGeneratedMapLate",
        "AfterMapGenerated",
    };

    private static readonly Dictionary<string, long> Marks = new();

    public static void Apply(Harmony harmony)
    {
        PatchStage(harmony, "gen generateMap", () => AccessTools.Method(typeof(RunManager), "GenerateMap"));
        PatchStage(harmony, "gen hook modify", () => AccessTools.Method(typeof(Hook), "ModifyGeneratedMap", new[] { typeof(IRunState), typeof(ActMap), typeof(int) }));
        PatchStage(harmony, "gen hook modifyLate", () => AccessTools.Method(typeof(Hook), "ModifyGeneratedMapLate", new[] { typeof(IRunState), typeof(ActMap), typeof(int) }));
        PatchStage(harmony, "gen hook after", () => AccessTools.Method(typeof(Hook), "AfterMapGenerated", new[] { typeof(IRunState), typeof(ActMap), typeof(int) }));
        PatchStage(harmony, "gen map changed", () => AccessTools.Method(typeof(NMapScreen), "OnMapChanged"));

        int patched = 0;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }
            catch
            {
                continue;
            }

            foreach (Type type in types)
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (Array.IndexOf(HookNames, method.Name) < 0) continue;
                    try
                    {
                        harmony.Patch(method, new HarmonyMethod(typeof(GenProbe), nameof(TypePrefix)));
                        patched++;
                    }
                    catch
                    {
                        // Diagnostics must never break the game.
                    }
                }
            }
        }

        MapLayout.Diag($"gen: model hook overrides watched={patched}");
    }

    /// <summary>Prefix that reports the owning type of any map-generation hook override.</summary>
    public static void TypePrefix(MethodBase __originalMethod)
    {
        string owner = __originalMethod.DeclaringType?.FullName ?? "?";
        MapLayout.Diag($"gen model {owner}.{__originalMethod.Name} enter");
        Marks[owner + "." + __originalMethod.Name] = Stopwatch.GetTimestamp();
    }

    private static void PatchStage(Harmony harmony, string label, Func<MethodInfo?> resolve)
    {
        MethodInfo? method;
        try
        {
            method = resolve();
        }
        catch (Exception ex)
        {
            MapLayout.Diag($"{label}: resolve failed {ex.GetType().Name}");
            return;
        }
        if (method == null)
        {
            MapLayout.Diag($"{label}: method not found");
            return;
        }
        try
        {
            harmony.Patch(method, new HarmonyMethod(typeof(GenProbe), nameof(Enter)), new HarmonyMethod(typeof(GenProbe), nameof(Exit)));
        }
        catch (Exception ex)
        {
            MapLayout.Diag($"{label}: patch failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Enter(MethodBase __originalMethod)
    {
        string key = (__originalMethod.DeclaringType?.FullName ?? "?") + "." + __originalMethod.Name;
        Marks[key] = Stopwatch.GetTimestamp();
        MapLayout.Diag($"gen {__originalMethod.Name} enter");
    }

    public static void Exit(MethodBase __originalMethod)
    {
        string key = (__originalMethod.DeclaringType?.FullName ?? "?") + "." + __originalMethod.Name;
        double ms = Marks.TryGetValue(key, out long start)
            ? (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency
            : 0;
        MapLayout.Diag($"gen {__originalMethod.Name} done ms={ms:0}");
    }
}
