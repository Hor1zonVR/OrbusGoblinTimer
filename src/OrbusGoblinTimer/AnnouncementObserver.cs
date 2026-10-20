using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace OrbusGoblinTimer;

/// <summary>
/// Passive, read-only announcement diagnostics. Never polls the scene, reads
/// player positions, changes game HTML, or scans objects on every frame.
/// </summary>
internal static class AnnouncementObserver
{
    private static Harmony? _harmony;
    private static string _lastCandidate = "";
    private static DateTimeOffset _lastCandidateAt = DateTimeOffset.MinValue;
    private static bool _surveyCompleted;
    private static int _patchCount;

    private static readonly string[] SetterNames =
    {
        "set_innerHTML", "set_textContent", "set_innerText"
    };

    internal static void Install()
    {
        try
        {
            _harmony = new Harmony("com.horizon.orbusgoblintimer.readonly");

            // Both variants may exist in a single client; the old build
            // stopped after finding the first one.
            foreach (string typeName in new[] { "PowerUI.Element", "Dom.Element" })
            {
                Type? element = FindType(typeName);
                if (element == null)
                {
                    Plugin.Logger.LogInfo("[GoblinTimer] DIAG UI type absent: " + typeName);
                    continue;
                }

                Plugin.Logger.LogInfo("[GoblinTimer] DIAG UI type found: " +
                                      element.Assembly.GetName().Name + "/" + typeName);

                MethodInfo[] methods = element.GetMethods(BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (string setterName in SetterNames)
                {
                    MethodInfo? setter = methods.FirstOrDefault(m =>
                        m.Name == setterName &&
                        m.ReturnType == typeof(void) &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(string));

                    if (setter == null)
                    {
                        Plugin.Logger.LogInfo("[GoblinTimer] DIAG setter unavailable: " +
                                              typeName + "." + setterName + "(string)");
                        continue;
                    }

                    try
                    {
                        MethodInfo before = typeof(AnnouncementObserver).GetMethod(
                            nameof(BeforeTextWrite),
                            BindingFlags.NonPublic | BindingFlags.Static)!;
                        _harmony.Patch(setter, prefix: new HarmonyMethod(before));
                        _patchCount++;
                        Plugin.Logger.LogInfo("[GoblinTimer] DIAG watching " +
                                              typeName + "." + setterName + "(string)");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning("[GoblinTimer] DIAG hook failed for " +
                            typeName + "." + setterName + ": " +
                            ex.GetType().Name + " " + ex.Message);
                    }
                }

                // Record possible alternative text paths, but DO NOT patch them.
                string[] alternatives = methods.Select(m => m.Name)
                    .Where(n => n.IndexOf("append", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("insert", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                n.IndexOf("message", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct().Take(12).ToArray();
                if (alternatives.Length > 0)
                    Plugin.Logger.LogInfo("[GoblinTimer] DIAG unhooked UI methods on " +
                        typeName + ": " + string.Join(", ", alternatives));
            }

            if (_patchCount == 0)
                Plugin.Logger.LogWarning("[GoblinTimer] DIAG no compatible PowerUI setters. " +
                    "This does NOT mean the server has no goblin event. Press Ctrl+Shift+G " +
                    "in-game for a one-time game-code metadata survey.");
            else
                Plugin.Logger.LogInfo("[GoblinTimer] DIAG installed " + _patchCount +
                    " passive UI text hooks. Only goblin-related text is logged.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] DIAG UI probe error: " +
                ex.GetType().Name + " " + ex.Message);
        }
    }

    private static void BeforeTextWrite(ref string __0)
    {
        try
        {
            string? raw = __0;
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 32768) return;
            if (raw.IndexOf("goblin", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("trickster", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            // Keep the diagnostic narrow: no general chat logging or scene data.
            string text = Regex.Replace(raw, "<[^>]*>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length > 240) text = text.Substring(0, 240) + "...";

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (text != _lastCandidate || now - _lastCandidateAt > TimeSpan.FromSeconds(20))
            {
                _lastCandidate = text;
                _lastCandidateAt = now;
                Plugin.Logger.LogInfo("[GoblinTimer] DIAG goblin-related UI write: " + text);
            }

            if (SpawnClock.IsSpawnAnnouncement(raw))
                Plugin.OnObservedSpawn("PowerUI announcement");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug("[GoblinTimer] DIAG UI observation error: " + ex.Message);
        }

        // No edits to __0: Harmony prefix is observation-only.
    }

    /// <summary>
    /// Manually triggered metadata-only scan. It inspects the Assembly-CSharp
    /// type catalogue once, NOT game objects, network packets or Unity scenes.
    /// The hotkey is deliberately required to avoid an unwanted startup stall.
    /// </summary>
    internal static void RunManualMetadataSurvey()
    {
        if (_surveyCompleted)
        {
            Plugin.Logger.LogInfo("[GoblinTimer] DIAG metadata survey already captured " +
                                  "this session; no repeat scan.");
            return;
        }
        _surveyCompleted = true;

        Plugin.Logger.LogInfo("[GoblinTimer] DIAG metadata survey BEGIN " +
            "(manual Ctrl+Shift+G; one-time, names only).");
        try
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => string.Equals(a.GetName().Name, "Assembly-CSharp",
                              StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a.GetName().Name, "Assembly-CSharp-firstpass",
                              StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (assemblies.Length == 0)
            {
                Plugin.Logger.LogWarning("[GoblinTimer] DIAG no Assembly-CSharp " +
                    "interop assembly currently loaded; share BepInEx/LogOutput.log.");
                return;
            }

            int emitted = 0;
            foreach (Assembly asm in assemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).Select(t => t!).ToArray();
                }

                var candidates = types
                    .Where(t => t.FullName != null && RelevantTypeName(t.FullName))
                    .OrderByDescending(t => TypePriority(t.FullName!))
                    .ThenBy(t => t.FullName, StringComparer.Ordinal)
                    .Take(45).ToArray();

                Plugin.Logger.LogInfo("[GoblinTimer] DIAG " + asm.GetName().Name +
                    ": " + types.Length + " types, " + candidates.Length +
                    " bounded event/chat/goblin candidates.");

                foreach (Type type in candidates)
                {
                    if (emitted >= 45) break;
                    emitted++;
                    try
                    {
                        string[] methods = type.GetMethods(BindingFlags.Instance |
                                BindingFlags.Static | BindingFlags.Public |
                                BindingFlags.NonPublic)
                            .Select(m => m.Name)
                            .Where(RelevantMethodName).Distinct().Take(8).ToArray();
                        Plugin.Logger.LogInfo("[GoblinTimer] DIAG game type: " +
                            type.FullName + " | methods: " +
                            (methods.Length == 0 ? "(no matching names)" :
                             string.Join(", ", methods)));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogInfo("[GoblinTimer] DIAG game type: " +
                            type.FullName + " | method listing: " +
                            ex.GetType().Name);
                    }
                }
                if (emitted >= 45) break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] DIAG metadata survey error: " +
                ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            Plugin.Logger.LogInfo("[GoblinTimer] DIAG metadata survey END. " +
                "Share LogOutput.log; do not assume a spawn schedule from these names.");
        }
    }

    private static bool RelevantTypeName(string name) =>
        Contains(name, "goblin") || Contains(name, "trickster") ||
        Contains(name, "announcement") || Contains(name, "broadcast") ||
        Contains(name, "chat") || Contains(name, "notification") ||
        Contains(name, "worldevent") || Contains(name, "gameevent") ||
        Contains(name, "systemmessage") || Contains(name, "spawnmanager");

    private static int TypePriority(string name)
    {
        if (Contains(name, "goblin") || Contains(name, "trickster")) return 100;
        if (Contains(name, "announcement") || Contains(name, "broadcast")) return 80;
        if (Contains(name, "worldevent") || Contains(name, "systemmessage")) return 75;
        if (Contains(name, "chat") || Contains(name, "notification")) return 60;
        return 40;
    }

    private static bool RelevantMethodName(string name) =>
        Contains(name, "goblin") || Contains(name, "trickster") ||
        Contains(name, "spawn") || Contains(name, "message") ||
        Contains(name, "chat") || Contains(name, "notify") ||
        Contains(name, "announcement") || Contains(name, "event") ||
        Contains(name, "receive") || Contains(name, "broadcast");

    private static bool Contains(string value, string part) =>
        value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

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
