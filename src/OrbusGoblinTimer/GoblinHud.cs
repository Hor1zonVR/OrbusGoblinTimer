using System;
using UnityEngine;
#if ORBUS_UI_AVAILABLE
using UnityEngine.UI;
#endif

namespace OrbusGoblinTimer;

/// <summary>
/// Read-only discovery of Orbus's local level HUD, and an independent goblin
/// readout placed beside it. Never reparents or edits Orbus's original UI.
/// Falls back to a larger static overlay on the stereo "UI on Top" camera.
/// </summary>
public sealed class GoblinHud : MonoBehaviour
{
    public GoblinHud(IntPtr pointer) : base(pointer) { }

    private const int TopUiLayer = 23;
    private static readonly Vector3 FallbackOffset = new(-0.86f, 0.87f, 1.08f);

    private GameObject? _fallbackRoot;
    private TextMesh? _fallbackText;
    private Camera? _uiCamera;
    private float _nextCameraSearch;
    private float _nextLevelSearch;
    private float _nextTextUpdate;
    private bool _loggedMissingCamera;
    private bool _loggedMissingLevel;
#if ORBUS_UI_AVAILABLE
    private GameObject? _nativeRoot;
    private Text? _nativeText;
    private Text? _levelAnchor;
#endif

    private void Start()
    {
        _fallbackRoot = new GameObject("OrbusGoblinTimer.UpperLeftFallback");
        _fallbackRoot.layer = TopUiLayer;
        UnityEngine.Object.DontDestroyOnLoad(_fallbackRoot);

        _fallbackText = _fallbackRoot.AddComponent<TextMesh>();
        _fallbackText.anchor = TextAnchor.MiddleLeft;
        _fallbackText.alignment = TextAlignment.Left;
        _fallbackText.fontSize = 44;
        // v0.1.1 used 0.0017. This text is about 3x its former visual scale,
        // and uses one concise line with no miniature subheading.
        _fallbackText.characterSize = 0.0048f;
        _fallbackText.fontStyle = FontStyle.Bold;
        _fallbackText.color = new Color(1.0f, 0.92f, 0.72f, 1f);
        _fallbackText.text = "GOBLIN: SYNC NEEDED";
        try
        {
            Font? font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                _fallbackText.font = font;
                MeshRenderer? renderer = _fallbackRoot.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.material = font.material;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] Fallback font: " + ex.Message);
        }
        _fallbackRoot.SetActive(false);
        Plugin.Logger.LogInfo("[GoblinTimer] v0.1.2 HUD ready: targeting real Level HUD text; large upper-left fallback.");
#if !ORBUS_UI_AVAILABLE
        Plugin.Logger.LogWarning("[GoblinTimer] Unity UI interop not available in build references. " +
            "Native Level-label matching disabled; readable camera fallback will be used.");
#endif
    }

    private void Update()
    {
        if (ManualSyncPressed())
            Plugin.OnObservedSpawn("manual-CtrlShiftF6");
    }

    private void LateUpdate()
    {
        if (_fallbackRoot == null || _fallbackText == null) return;
        if (!Plugin.Enabled.Value)
        {
            if (_fallbackRoot.activeSelf) _fallbackRoot.SetActive(false);
#if ORBUS_UI_AVAILABLE
            if (_nativeRoot != null && _nativeRoot.activeSelf) _nativeRoot.SetActive(false);
#endif
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (_uiCamera == null || !_uiCamera.isActiveAndEnabled ||
            _fallbackRoot.transform.parent != _uiCamera.transform)
        {
            if (now >= _nextCameraSearch)
            {
                _nextCameraSearch = now + 2f;
                AttachToTopCamera();
            }
        }

#if ORBUS_UI_AVAILABLE
        if (now >= _nextLevelSearch)
        {
            _nextLevelSearch = now + 4f;
            if (_levelAnchor == null || !_levelAnchor.gameObject.activeInHierarchy ||
                _nativeText == null || _nativeRoot == null)
                FindNativeLevelLabel();
        }
#endif

        if (now >= _nextTextUpdate)
        {
            _nextTextUpdate = now + 0.65f;
            string status = BuildDisplayText();
            _fallbackText.text = status;
#if ORBUS_UI_AVAILABLE
            if (_nativeText != null) _nativeText.text = status;
#endif
        }

#if ORBUS_UI_AVAILABLE
        // A native sibling inherits the same Canvas, anchoring and scaling as
        // "Level 25", and takes priority whenever it is available.
        bool nativeVisible = _nativeText != null && _nativeRoot != null &&
            _levelAnchor != null && _levelAnchor.gameObject.activeInHierarchy &&
            _nativeRoot.transform.parent == _levelAnchor.transform.parent;
        if (_nativeRoot != null && _nativeRoot.activeSelf != nativeVisible)
            _nativeRoot.SetActive(nativeVisible);
        if (nativeVisible)
        {
            if (_fallbackRoot.activeSelf) _fallbackRoot.SetActive(false);
            return;
        }
#endif
        bool fallbackVisible = _uiCamera != null && _uiCamera.isActiveAndEnabled &&
            _fallbackRoot.transform.parent == _uiCamera.transform;
        if (_fallbackRoot.activeSelf != fallbackVisible)
            _fallbackRoot.SetActive(fallbackVisible);
    }

    private static string BuildDisplayText()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!SpawnClock.TryParseUtc(Plugin.LastObservedSpawnUtc.Value,
            out DateTimeOffset last))
            return "GOBLIN: SYNC NEEDED";

        DateTimeOffset next = SpawnClock.NextSpawn(last, now,
            Plugin.SpawnIntervalHours.Value);
        string stale = (now - last >= TimeSpan.FromHours(24)) ? " ~" : "";
        return "GOBLIN: " + SpawnClock.FormatRemaining(next - now) + stale;
    }

    private void AttachToTopCamera()
    {
        try
        {
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null || !camera.isActiveAndEnabled ||
                    !camera.stereoEnabled ||
                    !string.Equals(camera.name, "Camera (UI on Top)",
                        StringComparison.Ordinal)) continue;

                _uiCamera = camera;
                _fallbackRoot!.transform.SetParent(camera.transform, false);
                _fallbackRoot.transform.localPosition = FallbackOffset;
                _fallbackRoot.transform.localRotation = Quaternion.identity;
                _fallbackRoot.transform.localScale = Vector3.one;
                _loggedMissingCamera = false;
                Plugin.Logger.LogInfo("[GoblinTimer] Large camera HUD attached. " +
                    "Fallback local offset=" + FallbackOffset + ", fontSize=44, characterSize=0.0048.");
                return;
            }
            _uiCamera = null;
            if (!_loggedMissingCamera)
            {
                _loggedMissingCamera = true;
                Plugin.Logger.LogWarning("[GoblinTimer] Waiting for active stereo Camera (UI on Top).");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] Top camera lookup failed: " + ex.Message);
        }
    }

#if ORBUS_UI_AVAILABLE
    private void FindNativeLevelLabel()
    {
        // Only consider the player's own live HUD. This avoids journal entries,
        // nameplates, enemy level text, inactive prefabs and text in the world.
        try
        {
            foreach (Text candidate in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
                string label = candidate.text ?? "";
                label = label.Replace('\n', ' ').Trim();
                if (!label.StartsWith("Level ", StringComparison.OrdinalIgnoreCase)) continue;

                string suffix = label.Substring(6).Trim();
                if (suffix.Length == 0 || suffix.Length > 4 ||
                    !int.TryParse(suffix, out _)) continue;
                if (!IsUnderLocalRig(candidate.transform)) continue;

                RectTransform? levelRect = candidate.GetComponent<RectTransform>();
                Transform? parent = candidate.transform.parent;
                if (levelRect == null || parent == null) continue;

                if (_nativeRoot != null) UnityEngine.Object.Destroy(_nativeRoot);

                var go = new GameObject("OrbusGoblinTimer.NearPlayerLevel");
                go.layer = candidate.gameObject.layer;
                var rect = go.AddComponent<RectTransform>();
                go.transform.SetParent(parent, false);
                rect.anchorMin = levelRect.anchorMin;
                rect.anchorMax = levelRect.anchorMax;
                rect.pivot = levelRect.pivot;
                // Keep the same height, but to the right of the Level label;
                // no movement of any original HUD elements.
                float xOffset = Mathf.Max(110f, candidate.preferredWidth + 24f);
                rect.anchoredPosition = levelRect.anchoredPosition + new Vector2(xOffset, 0f);
                rect.sizeDelta = new Vector2(430f,
                    Mathf.Max(55f, levelRect.rect.height));
                rect.localRotation = levelRect.localRotation;
                rect.localScale = levelRect.localScale;

                // Avoid disturbing any native Horizontal/VerticalLayoutGroup parent.
                var layoutElement = go.AddComponent<LayoutElement>();
                layoutElement.ignoreLayout = true;

                Text text = go.AddComponent<Text>();
                text.font = candidate.font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.fontSize = Mathf.Max(24, candidate.fontSize);
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleLeft;
                text.color = new Color(1f, 0.93f, 0.75f, 1f);
                text.raycastTarget = false;
                text.supportRichText = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.text = BuildDisplayText();

                _nativeRoot = go;
                _nativeText = text;
                _levelAnchor = candidate;
                _loggedMissingLevel = false;
                Plugin.Logger.LogInfo("[GoblinTimer] Found native Level HUD: '" +
                    label + "' at " + candidate.transform.name +
                    ". Added matching-size label as Canvas sibling with X offset=" +
                    xOffset.ToString("0") + ", fontSize=" + text.fontSize + ".");
                if (_fallbackRoot != null) _fallbackRoot.SetActive(false);
                return;
            }

            if (!_loggedMissingLevel)
            {
                _loggedMissingLevel = true;
                Plugin.Logger.LogWarning("[GoblinTimer] Native 'Level NN' Unity UI label not found. " +
                    "Showing much larger upper-left fallback; native font matching unavailable.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] Level HUD search failed: " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool IsUnderLocalRig(Transform transform)
    {
        Transform? current = transform;
        for (int depth = 0; depth < 30 && current != null; depth++)
        {
            if (current.name.StartsWith("PlayerRigv4", StringComparison.OrdinalIgnoreCase))
                return true;
            current = current.parent;
        }
        return false;
    }
#endif

    // Win32 is used for the manual PC keyboard fallback, as in the already
    // working RebornCam shortcuts. No Unity InputLegacyModule dependency.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    private bool _chordWasDown;
    private bool ManualSyncPressed()
    {
        const int Control = 0x11;
        const int Shift = 0x10;
        const int F6 = 0x75;
        bool focus = Application.isFocused;
        bool chord = focus &&
            (GetAsyncKeyState(Control) & 0x8000) != 0 &&
            (GetAsyncKeyState(Shift) & 0x8000) != 0 &&
            (GetAsyncKeyState(F6) & 0x8000) != 0;
        bool pressed = chord && !_chordWasDown;
        _chordWasDown = chord;
        return pressed;
    }

    private void OnDestroy()
    {
#if ORBUS_UI_AVAILABLE
        if (_nativeRoot != null) UnityEngine.Object.Destroy(_nativeRoot);
#endif
        if (_fallbackRoot != null) UnityEngine.Object.Destroy(_fallbackRoot);
    }
}
