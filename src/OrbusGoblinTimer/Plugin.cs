using System;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace OrbusGoblinTimer;

[BepInPlugin("com.horizon.orbusgoblintimer", "OrbusGoblinTimer", "0.1.2")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<int> SpawnIntervalHours = null!;
    internal static ConfigEntry<string> LastObservedSpawnUtc = null!;
    internal static ConfigEntry<string> SyncSource = null!;
    internal static ConfigEntry<bool> ObservePowerUiAnnouncements = null!;

    internal static bool HasSessionSync { get; private set; }
    private static DateTimeOffset _lastEventHandled = DateTimeOffset.MinValue;

    public override void Load()
    {
        Logger = Log;
        Enabled = Config.Bind("General", "ShowTimer", true,
            "Show a small Trickster Goblin countdown next to the game's upper HUD.");
        SpawnIntervalHours = Config.Bind("Timing", "IntervalHours", 4,
            new ConfigDescription("Server goblin spawn interval; 4 hours in the original game.",
                new AcceptableValueRange<int>(1, 24)));
        LastObservedSpawnUtc = Config.Bind("Timing", "LastObservedSpawnUtc", "",
            "Automatically saved time of the last observed spawn. Separate Orbus instances have separate configs.");
        SyncSource = Config.Bind("Timing", "LastSyncSource", "",
            "Read-only note: announcement or manual-CtrlShiftF6. The spawn interval is not itself the actual server schedule.");
        ObservePowerUiAnnouncements = Config.Bind("Diagnostics", "WatchPowerUiAnnouncements", true,
            "Observe original PowerUI HTML writes for Trickster Goblin spawn broadcasts (never changes game UI).");

        if (SpawnClock.TryParseUtc(LastObservedSpawnUtc.Value, out var last))
            Log.LogInfo("[GoblinTimer] Loaded last observation " + last.ToString("O") +
                        "; future cycles are estimates until confirmed on this server.");
        else
            Log.LogInfo("[GoblinTimer] Waiting for first spawn announcement or manual Ctrl+Shift+F6 sync.");

        if (ObservePowerUiAnnouncements.Value) AnnouncementObserver.Install();
        AddComponent<GoblinHud>();
        Log.LogInfo("[GoblinTimer] v0.1.2 loaded; top UI readout enabled=" + Enabled.Value +
                    "; interval=" + SpawnIntervalHours.Value + " hours.");
    }

    internal static void OnObservedSpawn(string source)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        // A chat/history UI might redraw the same announcement many times. Never
        // interpret those redraws as multiple separate four-hour spawns.
        if (now - _lastEventHandled < TimeSpan.FromMinutes(30)) return;
        if (SpawnClock.TryParseUtc(LastObservedSpawnUtc.Value, out var previous) &&
            now - previous < TimeSpan.FromMinutes(30)) return;
        _lastEventHandled = now;

        LastObservedSpawnUtc.Value = now.ToString("O", CultureInfo.InvariantCulture);
        SyncSource.Value = source;
        HasSessionSync = true;
        Logger.LogInfo("[GoblinTimer] Spawn clock synced from " + source + " at " +
                       LastObservedSpawnUtc.Value + " UTC. No goblin position recorded.");
    }
}
