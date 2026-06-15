using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Themes the main game UI (the buttonClicker UXML, which is styled with inline colours — so they
/// can only be overridden at runtime from code, not via stylesheets). It captures each element's
/// original inline colours the first time it sees it, classifies each into a <see cref="UiTheme"/>
/// slot, and re-applies on theme change.
///
/// Because much of the in-game UI (care log, health panels, pause menu, etc.) is built lazily, it
/// re-scans on every game-state change and on a light timer to catch newly-appeared elements.
/// Elements named with the "pm-" prefix (the progression overlays — AP chip, shop, Journal,
/// Customize) are skipped: they theme themselves via UiTheme.
///
/// Classification:
///   • background — near-black → panelBg, dark → cardBg, bright+saturated (filled buttons) → accent
///   • border     — → edge
///   • text       — gold/blue → accent, bright → textMain, else → textSub, with a contrast guard
/// Functional status-bar fills (moisture / compaction / saturation / nutrient) are denylisted.
///
/// Add this component anywhere in the scene. See Docs/PROGRESSION_DESIGN.md §7.
/// </summary>
public class GameUiThemer : MonoBehaviour
{
    class Orig { public bool hasBg, hasBorder, hasText; public Color bg, border, text; }

    readonly Dictionary<VisualElement, Orig> tracked = new Dictionary<VisualElement, Orig>();
    float rescanTimer;

    const float RescanInterval = 0.5f;

    // Functional status-bar fills keep their semantic colours (never themed).
    static readonly Color[] Protected =
    {
        new Color(0.314f, 0.627f, 1.000f),  // moisture   (80,160,255)
        new Color(0.706f, 0.471f, 0.157f),  // compaction (180,120,40)
        new Color(0.235f, 0.471f, 0.863f),  // saturation (60,120,220)
        new Color(0.314f, 0.784f, 0.314f),  // nutrient   (80,200,80)
    };

    void OnEnable()
    {
        UiTheme.OnThemeChanged          += OnThemeChanged;
        GameManager.OnGameStateChanged  += OnState;
    }

    void OnDisable()
    {
        UiTheme.OnThemeChanged          -= OnThemeChanged;
        GameManager.OnGameStateChanged  -= OnState;
    }

    void Start()  => Scan();
    void OnState(GameState s) => Scan();

    void Update()
    {
        rescanTimer += Time.unscaledDeltaTime;
        if (rescanTimer >= RescanInterval) { rescanTimer = 0f; Scan(); }
    }

    void OnThemeChanged()
    {
        // Re-apply to everything already tracked, then scan for anything new.
        foreach (var kv in tracked) ApplyTo(kv.Key, kv.Value);
        Scan();
    }

    // Captures + themes any element not seen before; leaves already-tracked ones (so per-frame
    // dynamic colours set by buttonClicker aren't fought every scan — only on theme change).
    // Walks EVERY UIDocument: the game HUD and menus can live on separate documents, and
    // FindFirstObjectByType isn't deterministic about which one it returns.
    void Scan()
    {
        var docs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        if (docs == null || docs.Length == 0) return;
        Prune();
        foreach (var doc in docs)
        {
            var r = doc != null ? doc.rootVisualElement : null;
            if (r != null) Walk(r);
        }
    }

    void Walk(VisualElement e)
    {
        if (!string.IsNullOrEmpty(e.name) && e.name.StartsWith("pm-")) return;  // progression overlays self-theme
        TrackNew(e);
        for (int i = 0; i < e.childCount; i++) Walk(e[i]);
    }

    void TrackNew(VisualElement e)
    {
        if (tracked.ContainsKey(e)) return;

        var o = new Orig();
        var bg = e.style.backgroundColor;
        if (bg.keyword == StyleKeyword.Undefined && !IsProtected(bg.value)) { o.hasBg = true; o.bg = bg.value; }
        var bt = e.style.borderTopColor;
        if (bt.keyword == StyleKeyword.Undefined) { o.hasBorder = true; o.border = bt.value; }
        var c = e.style.color;
        if (c.keyword == StyleKeyword.Undefined) { o.hasText = true; o.text = c.value; }

        if (!o.hasBg && !o.hasBorder && !o.hasText) return;
        tracked[e] = o;
        ApplyTo(e, o);
    }

    void Prune()
    {
        List<VisualElement> dead = null;
        foreach (var kv in tracked)
            if (kv.Key.panel == null) (dead ??= new List<VisualElement>()).Add(kv.Key);
        if (dead != null) foreach (var d in dead) tracked.Remove(d);
    }

    void ApplyTo(VisualElement e, Orig o)
    {
        Color mappedBg = default;
        if (o.hasBg) { mappedBg = MapBg(o.bg); e.style.backgroundColor = new StyleColor(mappedBg); }

        if (o.hasText)
        {
            Color tc = MapText(o.text);
            if (o.hasBg) tc = Contrast(mappedBg, tc);   // keep text readable on its own background
            e.style.color = new StyleColor(tc);
        }

        if (o.hasBorder) SetBorder(e, MapBorder(o.border));
    }

    // ── Classification ────────────────────────────────────────────────────────
    static bool IsProtected(Color c)
    {
        foreach (var p in Protected)
            if (Mathf.Abs(c.r - p.r) < 0.04f && Mathf.Abs(c.g - p.g) < 0.04f && Mathf.Abs(c.b - p.b) < 0.04f)
                return true;
        return false;
    }

    static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
    static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

    static Color MapBg(Color o)
    {
        var t = UiTheme.Current;
        Color.RGBToHSV(o, out _, out float s, out float v);
        if (v < 0.07f) return A(t.panelBg, o.a);
        if (v < 0.28f) return A(t.cardBg,  o.a);
        if (s > 0.20f) return A(t.accent,  o.a);
        return A(t.cardBg, o.a);
    }

    static Color MapBorder(Color o) => A(UiTheme.Current.edge, o.a);

    static Color MapText(Color o)
    {
        var t = UiTheme.Current;
        Color.RGBToHSV(o, out float h, out float s, out float v);
        if (s > 0.45f && h > 0.09f && h < 0.19f) return A(t.accent,   o.a);  // gold / yellow
        if (s > 0.30f && h > 0.52f && h < 0.72f) return A(t.accent,   o.a);  // blue
        if (v > 0.62f)                            return A(t.textMain, o.a);
        return A(t.textSub, o.a);
    }

    static Color Contrast(Color bg, Color text)
    {
        if (Mathf.Abs(Lum(bg) - Lum(text)) >= 0.35f) return text;
        var t = UiTheme.Current;
        float bgL = Lum(bg);
        return A(Mathf.Abs(bgL - Lum(t.ink)) > Mathf.Abs(bgL - Lum(t.textMain)) ? t.ink : t.textMain, text.a);
    }

    static void SetBorder(VisualElement e, Color c)
    {
        var sc = new StyleColor(c);
        e.style.borderTopColor = e.style.borderBottomColor =
            e.style.borderLeftColor = e.style.borderRightColor = sc;
    }
}
