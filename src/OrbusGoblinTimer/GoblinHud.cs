using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OrbusGoblinTimer;

/// <summary>Local TextMesh only: follows the user's headset and never alters the world or other players.</summary>
public sealed class GoblinHud : MonoBehaviour
{
    public GoblinHud(IntPtr pointer) : base(pointer) { }

    private GameObject? _root;
    private TextMesh? _text;
    private Camera? _headsetCamera;
    private float _nextRefresh;
    private float _nextCameraScan;

    private static MethodInfo? _getKeyDown;
    private static bool _searchedInput;
    private static bool _loggedHotkey;

    private void Start()
    {
        _root = new GameObject("OrbusGoblinTimer.LocalHeadsetHud");
        UnityEngine.Object.DontDestroyOnLoad(_root);

        _text = _root.AddComponent<TextMesh>();
        _text.anchor = TextAnchor.MiddleCenter;
        _text.alignment = TextAlignment.Center;
        _text.fontSize = 44;
        _text.characterSize = 0.0032f;
        _text.fontStyle = FontStyle.Bold;
        _text.color = new Color(0.97f, 0.90f, 0.62f, 0.97f);
        try
        {
            Font? font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                _text.font = font;
                MeshRenderer? renderer = _root.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.material = font.material;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] HUD font fallback: " + ex.Message);
        }
        _root.SetActive(false);
        Plugin.Logger.LogInfo("[GoblinTimer] Local headset HUD created.");
    }

    private void Update()
    {
        if (_root == null || _text == null) return;

        if (WasF9Pressed())
        {
            Plugin.OnObservedSpawn("manual-F9");
            if (!_loggedHotkey)
            {
                Plugin.Logger.LogInfo("[GoblinTimer] F9 recorded confirmed spawn; future launches reuse this UTC observation.");
                _loggedHotkey = true;
            }
        }

        if (!Plugin.Enabled.Value)
        {
            _root.SetActive(false);
            return;
        }

        float realtime = Time.realtimeSinceStartup;
        if (realtime < _nextRefresh) return;
        _nextRefresh = realtime + 0.2f;

        if (_headsetCamera == null || !_headsetCamera.isActiveAndEnabled)
        {
            if (realtime >= _nextCameraScan)
            {
                _nextCameraScan = realtime + 2f;
                _headsetCamera = ResolveCamera();
            }
        }
        if (_headsetCamera == null)
        {
            _root.SetActive(false);
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string details = "Waiting for spawn sync";
        string timeText = "--:--:--";
        if (SpawnClock.TryParseUtc(Plugin.LastObservedSpawnUtc.Value, out DateTimeOffset last))
        {
            DateTimeOffset next = SpawnClock.NextSpawn(last, now, Plugin.SpawnIntervalHours.Value);
            timeText = SpawnClock.FormatRemaining(next - now);
            double elapsedHours = (now - last).TotalHours;
            details = elapsedHours >= 24 ? "ESTIMATE - verify after server restart"
                    : Plugin.HasSessionSync ? "Synced this session" : "From saved spawn time";
        }
        else
            details = "F9 on PC at confirmed spawn";

        _text.text = "TRICKSTER GOBLIN\n" + timeText + "\n" + details;

        Transform cam = _headsetCamera.transform;
        Vector3 pos = cam.position + cam.forward * 0.78f + cam.up * 0.18f;
        _root.transform.position = pos;
        _root.transform.rotation = Quaternion.LookRotation(pos - cam.position, cam.up);
        if (!_root.activeSelf) _root.SetActive(true);
    }

    private static Camera? ResolveCamera()
    {
        try
        {
            // Prioritise stereo/HMD over RC's separate non-stereo recording camera.
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam != null && cam.isActiveAndEnabled && cam.stereoEnabled)
                    return cam;
            }
            Camera? main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug("[GoblinTimer] Camera lookup: " + ex.Message);
        }
        return null;
    }

    private static bool WasF9Pressed()
    {
        if (!_searchedInput)
        {
            _searchedInput = true;
            // Use reflection so the plugin does not require an additional
            // InputLegacyModule reference in the shared Community/Spades build.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type? input = assembly.GetType("UnityEngine.Input", false);
                    if (input == null) continue;
                    _getKeyDown = input.GetMethod("GetKeyDown",
                        BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(string) }, null);
                    if (_getKeyDown != null) break;
                }
                catch { }
            }
        }
        if (_getKeyDown == null) return false;
        try { return _getKeyDown.Invoke(null, new object[] { "f9" }) is bool hit && hit; }
        catch { return false; }
    }

    private void OnDestroy()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }
}
