# OrbusGoblinTimer (v0.1.2 experimental)

A lightweight client-side BepInEx IL2CPP plugin for OrbusVR **Community Edition** and **Spades/Renewed**. It displays a **Trickster Goblin countdown next to the in-game Level number**, when that Unity UI label is available, or a significantly larger upper-left fallback when it is not. No coordinates, tracking or gameplay modifications.

## What works in this first build

- Tries to discover the actual Unity UI **Level NN** label under the local player rig, then adds a matching-sized noninteractive sibling beside it. If the label is not discoverable, uses a **3.4x larger**, head-stable upper-left fallback attached to the game's stereo UI camera.
- Uses the historical **four-hour** spawn interval. Source: [official 18 June 2019 patch notes](https://store.steampowered.com/news/posts/?appids=746930&enddate=1561494246).
- Remembers the last confirmed spawn UTC time in the instance's BepInEx config. On future logins it extrapolates the repeating four-hour cycle.
- Read-only PowerUI `set_innerHTML` observation on the original Community client and modern Spades client to detect English Trickster Goblin spawn announcements if they pass through those UI elements.
- **Ctrl+Shift+F6 on the PC keyboard** manually records a goblin spawn *at the moment you press it*, if the automatic announcement hook cannot observe that client's notification path. This is a manual calibration fallback, **not** a magic lookup of the actual server time.
- If no reliable first spawn has been observed, HUD displays **GOBLIN: SYNC NEEDED** in large readable text, **not an invented timer**.
- If last sync was more than 24 hours ago, the readout appends **~** to indicate an estimated time (server restarts can change the schedule).

## v0.1.2 readability and positioning

The second headset test confirmed v0.1.1 loaded and attached correctly, but the TextMesh's font size/scale was too small to read. The desired placement is alongside the game's **Level XX** and weather HUD, not merely in the same UI rendering layer. v0.1.2 now searches for the real, visible Level label in Unity UI, creates a separate text element alongside it using a similar font and scale, and preserves the original game objects untouched. If no level label can be found, it uses a large, single-line TextMesh high on the left of the headset. Fallback font scale is 0.0058, up from 0.0017, and positioning is set once on parenting, never recomputed each update.

Build against an RM instance with both UnityEngine.UI.dll and UnityEngine.UIModule.dll generated to enable native HUD integration. The compile remains compatible without those optional assemblies, but native-level matching is disabled and the camera fallback is used. Look for **"Found native Level HUD"**, **"Native 'Level NN' Unity UI label not found"**, or **"Native-level matching disabled"** in LogOutput.log to confirm what path was used.

**Important:** The native level label may use a rendering system other than UnityEngine.UI.Text; this heuristic must be tested. If it doesn't match, upload a UI inspector report from RebornCam while in-game to identify the actual object and we can directly anchor the next iteration to it.

## v0.1.1 HUD fix

The user's first live test on Spades confirmed the old headset-following TextMesh was backwards and jittery, and its unsynced state had no numeric countdown. The updated HUD uses a small one-line upper-HUD label with a smaller clear sync status. It is parented **once** to the native stereo `Camera (UI on Top)` whose layer-23 mask was verified in the user's RebornCam log. It does not change any of Orbus's original UI objects and does not touch the main VR rig. F9 was already assigned to RebornCam and is no longer a GoblinTimer shortcut.

If no confirmed spawn timestamp is available, the mod shows `GOBLIN: SYNC NEEDED`. This is intentional. The original four-hour interval alone cannot determine the phase of the community server's first spawn. Press **Ctrl+Shift+F6 only when the goblin has actually spawned** if announcement auto-sync isn't working. An unverified timer would be misleading.

## Important limitations

The current community servers have not been verified to use the original 2019 four-hour interval or an identical phase. Spawn announcements may be delivered outside PowerUI; the read-only hook needs in-game validation **for both editions**. The time saved is **per modded game instance**, not automatically separated by different servers if you switch servers within the same instance. Verify/re-sync if the server restarts or you change servers.

A UI hook observing chat/history might also replay an old notification. There is a 30-minute deduplication safeguard, but a live test is required before relying on auto-sync. No information about goblin positions is collected.

## The easy workflow: local build -> GitHub Release -> RebornManager

**No GitHub Actions secrets are required.** The source is on GitHub; `release.ps1` uses the generated BepInEx IL2CPP references already installed on your Windows PC to compile the DLL, publishes it through the GitHub CLI you already use, and RM then reads the public stable release. This works exactly like CastMetrics and QuestHelper.

Clone from the parent of your existing QuestHelper directory:

```powershell
cd ..
git clone https://github.com/Hor1zonVR/OrbusGoblinTimer.git
cd .\OrbusGoblinTimer
```

Then publish directly to **RebornManager**:

```powershell
.\release.ps1
```

The script checks countdown tests, selects a BepInEx-prepared RM instance for local build references, compiles the plugin, commits/pushes any source changes, and publishes **one** `OrbusGoblinTimer.dll` on a **stable** GitHub release. The publisher remembers its build-reference path in the Git-ignored `.build-refs-path.txt`, so normal updates do not ask you to choose again. On the first run without a saved path it automatically selects the most recently used compatible Orbus instance. If needed, `-GameDir 'C:\\path\\to\\instance'` saves a different choice for all future releases. Build references are read-only; no game folder is directly changed by the script.

When you specifically want a **GitHub preview that RM intentionally ignores**, use `.\release.ps1 -Preview`. Because the user prefers testing directly through RM, the default is stable, though **v0.1.1 is still experimental** and needs in-game validation. After running the stable release, close Orbus and use RM's Add mods / Check updates to install the mod in the selected game instance.

Subsequent updates from GitHub:

```powershell
git pull
.\release.ps1
```

To choose a specific installed game manually, use `.\release.ps1 -GameDir 'C:\path\to\game-instance'`. You need `dotnet`, `git`, `gh`, and to be authenticated with `gh auth login` as with other local publishers.

## Build

Install BepInEx IL2CPP in a separate MODTEST Orbus instance, launch it once to generate `BepInEx/interop`, close the game. In PowerShell, from the repo root:

```powershell
dotnet build src/OrbusGoblinTimer/OrbusGoblinTimer.csproj -c Release "-p:GameDir=$env:LOCALAPPDATA\OrbusRebornModdingInstances\Modded Orbus"
```

Adjust the GameDir to the correct instance. Install `src/OrbusGoblinTimer/bin/Release/net6.0/OrbusGoblinTimer.dll` into the **MODTEST** instance's `BepInEx/plugins` folder. Look for `[GoblinTimer]` in `BepInEx/LogOutput.log`.

## Testing

1. Launch each edition and confirm the HUD says `Waiting for spawn sync`.
2. At a **confirmed** spawn, see whether `[GoblinTimer] Spawn clock synced from announcement` appears. If not, press ****Ctrl+Shift+F6**** on the desktop keyboard while Orbus is focused.
3. Confirm the timer appears near the top HUD/level region, faces the player, and doesn't jitter when your head moves. The number should change each second only after a real sync.
4. Exit and relaunch the **same instance**: the saved countdown should resume. Confirm any mismatch after a live spawn, or server restart, rather than assuming the interval.
5. Share any relevant `[GoblinTimer]` log lines and whether the announcement was detected on Community or Spades.

Config (only if troubleshooting): `BepInEx/config/com.horizon.orbusgoblintimer.cfg`. `[General] ShowTimer = false` hides the display. Changes to the display's placement are intentionally deferred until the future common RebornMenu system.

## CI / RebornManager releases

The lightweight `clock-tests.yml` GitHub Action validates clock math and announcement matching automatically without any secrets or private game files. The **actual IL2CPP plugin** is built on your own Windows PC with `release.ps1` and published using your existing GitHub CLI login. The RM catalogue already contains this repository; it offers the mod once its first non-prerelease `OrbusGoblinTimer.dll` is published.

Never commit your game's generated interop DLLs or authentication tokens to this public repository.
