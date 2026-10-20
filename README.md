# OrbusGoblinTimer (v0.1.0 experimental)

A lightweight client-side BepInEx IL2CPP plugin for OrbusVR **Community Edition** and **Spades/Renewed**. It shows a small **Trickster Goblin countdown above the centre of your VR view**. No coordinates, tracking or gameplay modifications.

## What works in this first build

- Headset-following local **TextMesh** HUD, with a live HH:MM:SS countdown.
- Uses the historical **four-hour** spawn interval. Source: [official 18 June 2019 patch notes](https://store.steampowered.com/news/posts/?appids=746930&enddate=1561494246).
- Remembers the last confirmed spawn UTC time in the instance's BepInEx config. On future logins it extrapolates the repeating four-hour cycle.
- Read-only PowerUI `set_innerHTML` observation on the original Community client and modern Spades client to detect English Trickster Goblin spawn announcements if they pass through those UI elements.
- **F9 on the PC keyboard** manually records a goblin spawn *at the moment you press it*, if the automatic announcement hook cannot observe that client's notification path. This is a manual calibration fallback, **not** a magic lookup of the actual server time.
- If no reliable first spawn has been observed, HUD displays `--:--:--` and `Waiting for spawn sync`, **not an invented timer**.
- If last sync was more than 24 hours ago, the HUD explicitly labels the countdown as an **estimate** (server resets can change the schedule).

## Important limitations

The current community servers have not been verified to use the original 2019 four-hour interval or an identical phase. Spawn announcements may be delivered outside PowerUI; the read-only hook needs in-game validation **for both editions**. The time saved is **per modded game instance**, not automatically separated by different servers if you switch servers within the same instance. Verify/re-sync if the server restarts or you change servers.

A UI hook observing chat/history might also replay an old notification. There is a 30-minute deduplication safeguard, but a live test is required before relying on auto-sync. No information about goblin positions is collected.

## Build

Install BepInEx IL2CPP in a separate MODTEST Orbus instance, launch it once to generate `BepInEx/interop`, close the game. In PowerShell, from the repo root:

```powershell
dotnet build src/OrbusGoblinTimer/OrbusGoblinTimer.csproj -c Release "-p:GameDir=$env:LOCALAPPDATA\OrbusRebornModdingInstances\Modded Orbus"
```

Adjust the GameDir to the correct instance. Install `src/OrbusGoblinTimer/bin/Release/net6.0/OrbusGoblinTimer.dll` into the **MODTEST** instance's `BepInEx/plugins` folder. Look for `[GoblinTimer]` in `BepInEx/LogOutput.log`.

## Testing

1. Launch each edition and confirm the HUD says `Waiting for spawn sync`.
2. At a **confirmed** spawn, see whether `[GoblinTimer] Spawn clock synced from announcement` appears. If not, press **F9** on the desktop keyboard while Orbus is focused.
3. Confirm a countdown appears near the top of the headset view and advances each second.
4. Exit and relaunch the **same instance**: the saved countdown should resume. Confirm any mismatch after a live spawn, or server restart, rather than assuming the interval.
5. Share any relevant `[GoblinTimer]` log lines and whether the announcement was detected on Community or Spades.

Config (only if troubleshooting): `BepInEx/config/com.horizon.orbusgoblintimer.cfg`. `[General] ShowTimer = false` hides the display. Changes to the display's placement are intentionally deferred until the future common RebornMenu system.

## CI / RebornManager releases

`clock-tests.yml` runs pure timing and announcement-match tests without game files. `Publish GoblinTimer` builds the real DLL using existing private IL2CPP build references, initially publishes a **preview** (not shown in RM), and can promote that same commit/tag to **stable** after live testing.

One-time Actions settings in this new repository:

- **Variable** `GOBLINTIMER_BUILD_REFS_REPO` = the same private build-reference repository used by RebornCam/CastMetrics (owner/name).
- **Secret** `GOBLINTIMER_BUILD_REFS_TOKEN` = read-only access token for that private repository.
- Private release `refs-v1` should contain `RebornCamBuildRefs.zip` with BepInEx and interop references.

Never commit generated game binaries, private refs or access tokens to this public repo. RM catalogue registration does not make an unreleased mod downloadable: after test builds pass, publish a verified **stable** DLL to enable install via RebornManager.
