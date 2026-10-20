using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace OrbusGoblinTimer;

/// <summary>
/// Read-only PowerUI event probe. Both supported PowerUI variants are handled
/// by the same BepInEx plugin. Does not inspect coordinates, modify HTML or gameplay.
/// </summary>
internal static class AnnouncementObserver
{
    private static Harmony? _harmony;

    internal static void Install()
    {
        Type? legacy = FindType("PowerUI.Element");
        Type? modern = FindType("Dom.Element");
        Type? element = legacy ?? modern;

        if (element == null)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] No known PowerUI element type; " +
                "automatic announcement detection is unavailable. Use F9 at a confirmed spawn.");
            return;
        }

        MethodInfo? setter = element.GetMethods(BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "set_innerHTML" &&
                m.ReturnType == typeof(void) && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(string));

        MethodInfo? before = typeof(AnnouncementObserver).GetMethod(
            nameof(BeforeHtml), BindingFlags.NonPublic | BindingFlags.Static);

        if (setter == null || before == null)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] PowerUI HTML setter signature not found. " +
                "Use F9 to synchronise from an observed spawn.");
            return;
        }

        try
        {
            _harmony = new Harmony("com.horizon.orbusgoblintimer.readonly");
            _harmony.Patch(setter, prefix: new HarmonyMethod(before));
            Plugin.Logger.LogInfo("[GoblinTimer] Read-only announcement probe attached: " +
                setter.DeclaringType?.FullName + ".set_innerHTML(string).");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] Announcement probe unavailable: " +
                ex.GetType().Name + " " + ex.Message + ". F9 sync remains available.");
        }
    }

    private static void BeforeHtml(object __instance, ref string __0)
    {
        try
        {
            if (SpawnClock.IsSpawnAnnouncement(__0))
                Plugin.OnObservedSpawn("announcement");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug("[GoblinTimer] Announcement observation failed: " + ex.Message);
        }
        // Deliberately NEVER assign to __0 or interact with __instance.
    }

    private static Type? FindType(string name)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = asm.GetType(name, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }
}
