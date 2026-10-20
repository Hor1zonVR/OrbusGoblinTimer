using System;
using System.Reflection;
using UnityEngine;

namespace OrbusGoblinTimer;

/// <summary>
/// Small native-layer readout. Parents once to Orbus's own stereo UI-on-top
/// camera, so Unity's VR rig does all head following at render rate.
/// Never modifies the game's existing level, health, mission or menu UI.
/// </summary>
public sealed class GoblinHud : MonoBehaviour
{
    public GoblinHud(IntPtr pointer) : base(pointer) { }

    // The actual player UI camera renders ONLY layer 23 (0x00800000).
    // Confirmed in Spades user's 2026-10-20 BepInEx/OrbusRebornCamera log.
    private const int TopUiLayer = 23;
    private static readonly Vector3 HudOffset = new(0.35f, 0.34f, 0.85f);

    private GameObject? _root;
    private TextMesh? _time;
    private TextMesh? _status;
    private Camera? _uiCamera;
    private float _nextCameraSearch;
    private float _nextTextUpdate;
    private bool _warnedNoCamera;
    private bool _manualInputWarned;

    private static bool _inputResolved;
    private static MethodInfo? _getKey;
    private static MethodInfo? _getKeyDown;

    private void Start()
    {
        _root = new GameObject("OrbusGoblinTimer.TopUi");
        _root.layer = TopUiLayer;
        UnityEngine.Object.DontDestroyOnLoad(_root);

        _time = MakeLabel("Countdown", 40, 0.0017f,
            new Color(0.98f, 0.89f, 0.65f, 0.98f),
            new Vector3(0f, 0f, 0f));

        _status = MakeLabel("SyncStatus", 27, 0.00110f,
            new Color(0.91f, 0.85f, 0.69f, 0.90f),
            new Vector3(0f, -0.047f, 0f));

        _root.SetActive(false);
        Plugin.Logger.LogInfo("[GoblinTimer] Native-layer top HUD created; awaiting player's 'Camera (UI on Top)'.");
    }

    private TextMesh MakeLabel(string name, int fontSize, float characterSize,
        Color color, Vector3 localPosition)
    {
        var child = new GameObject(name);
        child.layer = TopUiLayer;
        child.transform.SetParent(_root!.transform, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;

        var mesh = child.AddComponent<TextMesh>();
        mesh.anchor = TextAnchor.MiddleRight;
        mesh.alignment = TextAlignment.Right;
        mesh.fontSize = fontSize;
        mesh.characterSize = characterSize;
        mesh.fontStyle = FontStyle.Bold;
        mesh.color = color;
        mesh.text = "";

        try
        {
            Font? font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                mesh.font = font;
                MeshRenderer? renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.material = font.material;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] Built-in font fallback: " + ex.Message);
        }
        return mesh;
    }

    private void Update()
    {
        // F9 belongs to RebornCam! The old GoblinTimer F9 shortcut
        // caused conflicts. Require the more deliberate Ctrl+Shift+F6.
        if (ManualSyncPressed())
        {
            Plugin.OnObservedSpawn("manual-CtrlShiftF6");
            Plugin.Logger.LogInfo("[GoblinTimer] Ctrl+Shift+F6 manual sync requested.");
        }
    }

    private void LateUpdate()
    {
        if (_root == null || _time == null || _status == null) return;

        if (!Plugin.Enabled.Value)
        {
            if (_root.activeSelf) _root.SetActive(false);
            return;
        }

        float now = Time.realtimeSinceStartup;

        if (_uiCamera == null || !_uiCamera.isActiveAndEnabled ||
            _root.transform.parent != _uiCamera.transform)
        {
            if (now >= _nextCameraSearch)
            {
                _nextCameraSearch = now + 2f;
                FindAndAttachTopUiCamera();
            }
        }

        if (_uiCamera == null || !_uiCamera.isActiveAndEnabled ||
            _root.transform.parent != _uiCamera.transform)
        {
            if (_root.activeSelf) _root.SetActive(false);
            return;
        }

        // Only change the string at ~1 Hz. The camera is a permanent parent;
        // we NEVER compute a new headset-relative world pose in Update/LateUpdate.
        if (now >= _nextTextUpdate)
        {
            _nextTextUpdate = now + 0.5f;
            RefreshText();
        }

        if (!_root.activeSelf) _root.SetActive(true);
    }

    private void FindAndAttachTopUiCamera()
    {
        try
        {
            foreach (Camera candidate in Camera.allCameras)
            {
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    !candidate.stereoEnabled) continue;

                // The main eye camera and RC desktop recording cameras
                // are deliberately NOT allowed as fallback anchors.
                if (!string.Equals(candidate.name, "Camera (UI on Top)",
                    StringComparison.Ordinal)) continue;

                _uiCamera = candidate;
                _root!.transform.SetParent(candidate.transform, false);
                _root.transform.localPosition = HudOffset;
                // Unity TextMesh glyph faces camera at local identity (-Z normal).
                // No LookRotation/180-degree flip = no backwards text.
                _root.transform.localRotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;

                _nextTextUpdate = 0f;
                _warnedNoCamera = false;
                Plugin.Logger.LogInfo("[GoblinTimer] Attached to " +
                    candidate.transform.name + " (stereo UI camera, layer 23). " +
                    "Fixed local pose; no per-frame positioning.");
                return;
            }

            _uiCamera = null;
            if (!_warnedNoCamera)
            {
                _warnedNoCamera = true;
                Plugin.Logger.LogWarning("[GoblinTimer] 'Camera (UI on Top)' not available yet. " +
                    "HUD stays hidden rather than attaching to a wrong camera.");
            }
        }
        catch (Exception ex)
        {
            _uiCamera = null;
            Plugin.Logger.LogWarning("[GoblinTimer] UI camera attach failed: " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void RefreshText()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (SpawnClock.TryParseUtc(Plugin.LastObservedSpawnUtc.Value,
            out DateTimeOffset last))
        {
            DateTimeOffset next = SpawnClock.NextSpawn(last, now,
                Plugin.SpawnIntervalHours.Value);
            _time!.text = "GOBLIN  " + SpawnClock.FormatRemaining(next - now);
            _status!.text = (now - last >= TimeSpan.FromHours(24))
                ? "Estimated - resync after server restart"
                : Plugin.HasSessionSync ? "Synced this session - estimated next spawn"
                                        : "Saved sync - estimated next spawn";
        }
        else
        {
            _time!.text = "GOBLIN  --:--:--";
            _status!.text = "Waiting for sync - Ctrl+Shift+F6 at spawn";
        }
    }

    private static bool ManualSyncPressed()
    {
        if (!_inputResolved)
        {
            _inputResolved = true;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type? type = assembly.GetType("UnityEngine.Input", false);
                    if (type == null) continue;
                    _getKey = type.GetMethod("GetKey", BindingFlags.Public |
                        BindingFlags.Static, null, new[] { typeof(string) }, null);
                    _getKeyDown = type.GetMethod("GetKeyDown", BindingFlags.Public |
                        BindingFlags.Static, null, new[] { typeof(string) }, null);
                    if (_getKey != null && _getKeyDown != null) break;
                }
                catch { }
            }
        }
        if (_getKey == null || _getKeyDown == null) return false;
        try
        {
            bool ctrl = IsHeld("left ctrl") || IsHeld("right ctrl");
            bool shift = IsHeld("left shift") || IsHeld("right shift");
            return ctrl && shift && _getKeyDown.Invoke(null, new object[] { "f6" }) is bool hit && hit;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHeld(string key) =>
        _getKey?.Invoke(null, new object[] { key }) is bool down && down;

    private void OnDestroy()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }
}
