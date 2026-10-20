using System;
using System.Runtime.InteropServices;
using UnityEngine;
#if ORBUS_UI_AVAILABLE
using UnityEngine.UI;
#endif

namespace OrbusGoblinTimer;

/// <summary>
/// Integrates a small, non-interactive label into the actual Orbus TopHUD
/// world-space Canvas. Nothing is attached to either of the eye cameras.
/// </summary>
public sealed class GoblinHud : MonoBehaviour
{
    public GoblinHud(IntPtr pointer) : base(pointer) { }

#if ORBUS_UI_AVAILABLE
    private Canvas? _hudCanvas;
    private GameObject? _entry;
    private Text? _label;
    private float _nextSearch;
    private float _nextRefresh;
    private bool _warnedNoHud;
    private bool _loggedHierarchy;
#else
    private bool _warnedNoInterop;
#endif
    private bool _chordWasDown;

    private void Start()
    {
        Plugin.Logger.LogInfo("[GoblinTimer] v0.1.4: native PlayerRigv4/TopHUD/Canvas ONLY. " +
            "No headset-following fallback.");
    }

    private void Update()
    {
        if (ManualSyncPressed())
        {
            Plugin.OnObservedSpawn("manual-CtrlShiftF6");
            Plugin.Logger.LogInfo("[GoblinTimer] Ctrl+Shift+F6 manual spawn sync requested.");
        }
    }

    private void LateUpdate()
    {
#if ORBUS_UI_AVAILABLE
        if (!Plugin.Enabled.Value)
        {
            if (_entry != null && _entry.activeSelf) _entry.SetActive(false);
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (_hudCanvas == null || !_hudCanvas.gameObject.activeInHierarchy ||
            _entry == null || _label == null ||
            _entry.transform.parent != _hudCanvas.transform)
        {
            if (now >= _nextSearch)
            {
                _nextSearch = now + 2f;
                AttachToNativeTopHud();
            }
            return;
        }

        if (!_entry.activeSelf) _entry.SetActive(true);
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + 0.5f;
            _label.text = DisplayText();
        }
#else
        if (!_warnedNoInterop)
        {
            _warnedNoInterop = true;
            Plugin.Logger.LogWarning("[GoblinTimer] No UnityEngine.UI interop references in " +
                "this build. Cannot use native TopHUD; floating display intentionally disabled.");
        }
#endif
    }

    private static string DisplayText()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!SpawnClock.TryParseUtc(Plugin.LastObservedSpawnUtc.Value,
            out DateTimeOffset last))
            return "Goblin: no sync";

        DateTimeOffset next = SpawnClock.NextSpawn(last, now,
            Plugin.SpawnIntervalHours.Value);
        string estimate = (now - last >= TimeSpan.FromHours(24)) ? " ~" : "";
        return "Goblin: " + SpawnClock.FormatRemaining(next - now) + estimate;
    }

#if ORBUS_UI_AVAILABLE
    private static bool IsPlayerTopHud(Canvas candidate)
    {
        try
        {
            if (!candidate.gameObject.activeInHierarchy ||
                candidate.name != "Canvas")
                return false;

            Transform? hud = candidate.transform.parent;
            Transform? rig = hud?.parent;
            return hud != null && rig != null &&
                hud.name == "TopHUD" &&
                rig.name.StartsWith("PlayerRigv4", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private void AttachToNativeTopHud()
    {
        try
        {
            Canvas? found = null;
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas != null && IsPlayerTopHud(canvas))
                {
                    found = canvas;
                    break;
                }
            }

            if (found == null)
            {
                if (!_warnedNoHud)
                {
                    _warnedNoHud = true;
                    Plugin.Logger.LogWarning("[GoblinTimer] Waiting for PlayerRigv4/TopHUD/Canvas. " +
                        "No camera/floating fallback will appear.");
                }
                return;
            }

            if (_entry != null)
            {
                UnityEngine.Object.Destroy(_entry);
                _entry = null;
                _label = null;
            }

            _hudCanvas = found;
            _warnedNoHud = false;

            RectTransform? parentRect = found.GetComponent<RectTransform>();
            if (parentRect == null)
            {
                Plugin.Logger.LogWarning("[GoblinTimer] Native TopHUD Canvas has no RectTransform.");
                return;
            }

            // The inspector measured TopHUD/Canvas at 1 x 1 local UI units.
            // Scale from its actual bounds, not headset world metres.
            // This layout occupies roughly 60% of the canvas width and
            // a thin single line in the upper part of the native HUD.
            float width = Mathf.Max(0.1f, parentRect.rect.width);
            float height = Mathf.Max(0.1f, parentRect.rect.height);
            float lineHeight = height * 0.065f;
            float scale = lineHeight / 40f;

            _entry = new GameObject("OrbusGoblinTimer.NativeTopHudLabel");
            _entry.layer = found.gameObject.layer; // Native HUD: UI layer 5.
            RectTransform rect = _entry.AddComponent<RectTransform>();
            rect.SetParent(found.transform, false);
            rect.anchorMin = new Vector2(0.055f, 0.91f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width * 0.65f / scale, 40f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = new Vector3(scale, scale, scale);
            rect.localRotation = Quaternion.identity;

            LayoutElement layout = _entry.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            _label = _entry.AddComponent<Text>();
            _label.font = ResolveNativeFont(found);
            _label.fontSize = 27;
            _label.fontStyle = FontStyle.Bold;
            _label.color = new Color(1f, 0.93f, 0.76f, 0.98f);
            _label.alignment = TextAnchor.MiddleLeft;
            _label.supportRichText = false;
            _label.raycastTarget = false;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.text = DisplayText();
            _nextRefresh = 0f;

            Plugin.Logger.LogInfo("[GoblinTimer] ATTACHED to native " +
                "PlayerRigv4/TopHUD/Canvas, not a camera. " +
                "canvasSize=(" + width.ToString("0.000") + "," +
                height.ToString("0.000") + "), childScale=" +
                scale.ToString("0.000000") + ", label='" + _label.text +
                "', layer=" + _entry.layer + ", nativeCanvasMode=" +
                found.renderMode + ".");
            if (!_loggedHierarchy)
            {
                _loggedHierarchy = true;
                LogNativeHudChildren(found.transform, 0, 22);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("[GoblinTimer] Native TopHUD creation failed: " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Font ResolveNativeFont(Canvas root)
    {
        try
        {
            // Borrow the font of any existing Orbus UI text inside the
            // same TopHUD if available, otherwise use Unity's built-in font.
            foreach (Text candidate in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (candidate == null || candidate.font == null ||
                    candidate.gameObject.name == "OrbusGoblinTimer.NativeTopHudLabel")
                    continue;
                Transform? node = candidate.transform;
                for (int depth = 0; depth < 10 && node != null; depth++)
                {
                    if (node == root.transform)
                        return candidate.font;
                    node = node.parent;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] Native font lookup: " + ex.Message);
        }
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static void LogNativeHudChildren(Transform parent, int depth, int budget)
    {
        try
        {
            // This log gives us names and component types of the real HUD,
            // without altering game components or recording player content.
            int count = 0;
            for (int i = 0; i < parent.childCount && count < budget; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null ||
                    child.name == "OrbusGoblinTimer.NativeTopHudLabel")
                    continue;

                RectTransform? rect = child.GetComponent<RectTransform>();
                Text? label = child.GetComponent<Text>();
                Plugin.Logger.LogInfo("[GoblinTimer] NativeTopHUD child " +
                    new string(' ', depth * 2) + child.name +
                    " active=" + child.gameObject.activeInHierarchy +
                    " layer=" + child.gameObject.layer +
                    " rect=" + (rect != null ?
                        "(" + rect.rect.width.ToString("0.000") +
                        " x " + rect.rect.height.ToString("0.000") + ")" : "(none)") +
                    " localPos=" + child.localPosition +
                    " UI.Text=" + (label != null) +
                    (label != null ? " fontSize=" + label.fontSize : ""));
                count++;
                if (depth < 2 && child.childCount > 0)
                    LogNativeHudChildren(child, depth + 1, Math.Min(8, budget - count));
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[GoblinTimer] Native HUD survey failed: " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }
#endif

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private bool ManualSyncPressed()
    {
        const int Ctrl = 0x11;
        const int Shift = 0x10;
        const int F6 = 0x75;
        bool held = Application.isFocused &&
            (GetAsyncKeyState(Ctrl) & 0x8000) != 0 &&
            (GetAsyncKeyState(Shift) & 0x8000) != 0 &&
            (GetAsyncKeyState(F6) & 0x8000) != 0;
        bool justPressed = held && !_chordWasDown;
        _chordWasDown = held;
        return justPressed;
    }

    private void OnDestroy()
    {
#if ORBUS_UI_AVAILABLE
        if (_entry != null)
            UnityEngine.Object.Destroy(_entry);
#endif
    }
}
