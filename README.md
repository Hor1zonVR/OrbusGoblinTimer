# OrbusGoblinTimer (v0.1.5 diagnostic)

**v0.1.5: read-only Goblin event diagnostics.** The confirmed native TopHUD overlay remains in place, nudged slightly right to avoid the Level label. The game-code scan runs **only** when you press **Ctrl+Shift+G** with the game focused; it scans loaded `Assembly-CSharp` type names once per session, caps the result list at 45, and records matching method **names**, not player data, positions or network packets. Startup also probes known PowerUI/Dom text setters and logs only text containing `goblin` or `trickster`. Automatic timer sync is still experimental: **do not assume a correct spawn countdown before a real announcement has been detected**.

**Test via RebornManager:** from the existing OrbusGoblinTimer clone, run `git pull` then `.\release.ps1`; refresh RM and update the mod. In-game, press **Ctrl+Shift+G once** and send `BepInEx/LogOutput.log` with lines containing `[GoblinTimer] DIAG`. Keep the mod running when an actual goblin spawn announcement happens and send that session's log too. **Ctrl+Shift+F6** still manually syncs a confirmed actual spawn; do not use it just to test the UI. If a message may contain private chat, review the log before sharing.

**Safety:** no polling/periodic game-object searches were added to the diagnostic code, no hooks alter content, and the one-shot type survey is deliberately opt-in. This is an untested source change until the DLL is built and launched in both Community and Spades editions.

---

# OrbusGoblinTimer (v0.1.4 experimental)

Client-side BepInEx IL2CPP Trickster Goblin countdown for OrbusVR Community and Spades / Renewed. **v0.1.4 replaces the old headset-following TextMesh with a noninteractive Unity UI.Text child of the game's own `PlayerRigv4/TopHUD/Canvas`.** Nothing is parented to a camera. The label should occupy the same world-space HUD that shows level/health/weather, and leave the view naturally when looking down.

## What to expect from v0.1.4

- The RebornCam read-only UI report `OrbusUI_20261020_132901_862.txt` verified an active world-space `PlayerRigv4/TopHUD/Canvas` (layer 5: UI; RectTransform 1x1). This is the actual native TopHUD container, although the old inspector's highest-score cutoff omitted most children.
- The mod adds a compact `Goblin: no sync` display *inside that native Canvas*, using its actual RectTransform size, and does not manipulate its existing children, player rig, cameras, or gameplay state.
- If no native TopHUD is available, the label remains hidden: **the oversized floating fallback is removed entirely**.
- `Ctrl+Shift+F6` on PC still records an actual observed spawn time. The four-hour interval is a historical estimate; without a confirmed spawn, the mod intentionally shows no invented countdown. The PowerUI announcement listener remains experimental.
- The BepInEx log now prints `ATTACHED to native PlayerRigv4/TopHUD/Canvas` and a brief read-only inventory of the native HUD's children. If the alignment is slightly off, send this log and a screenshot. We can then use the real child names and scale instead of guessing from camera offsets.
- The build requires UnityEngine.UI interop for native HUD rendering; the existing local release script enables this automatically when the selected Orbus RM instance contains it.

## Why this was changed

The prior versions were anchored to `Camera (UI on Top)`, which rotates with the headset and is **not** the user-requested display. Increasing its text size to 0.065 world units made it enormous. The user instead requested the top in-game status HUD that stays at its game position as they turn or look down. The 13:29 RebornCam report discovered the native `TopHUD/Canvas` and enables direct parent placement.

## v0.1.3 invisible HUD diagnosis and visibility fix

The third live Spades log proves the DLL loaded, the game UI camera existed,
and the UnityEngine.UI.Text level-search failed; the fallback was installed at
an extreme upper-left headset angle and was still almost microscopic
(`characterSize=0.0048`, only 4.8 mm per nominal character at 1.08 m).
The fallback is now positioned safely inside the stereo view, with
`characterSize=0.065` (over **13 times larger**) and the source code
verifies/logs viewport visibility rather than assuming the text is visible.
The mod additionally searches for an actual Unity `TextMesh` "Level NN"
within the local rig's UI layer and attaches a same-parent label if found.
If the level display uses PowerUI or other components, the independent
fallback remains. The mod never modifies game-native HUD elements.

Look for the `Visible HUD diagnostic: viewport=...` line in the BepInEx
log, which records actual camera position, active state, renderer status
and text. Do not manually set a spawn time until a real spawn occurs.

## v0.1.2 readability and positioning

The second headset test confirmed v0.1.1 loaded and attached correctly, but the TextMesh's font size/scale was too small to read. The desired placement is alongside the game's **Level XX** and weather HUD, not merely in the same UI rendering layer. v0.1.2 now searches for the real, visible Level label in Unity UI, creates a separate text element alongside it using a similar font and scale, and preserves the original game objects untouched. If no level label can be found, it uses a large, single-line TextMesh high on the left of the headset. Fallback font scale is 0.0048, up from 0.0017, and positioning is set once on parenting, never recomputed each update.

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
