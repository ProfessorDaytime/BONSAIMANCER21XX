using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared palette painter for theming existing UI Toolkit elements (the in-game UXML panels).
/// A caller captures an element's ORIGINAL colours once (from <c>resolvedStyle</c> — the actual
/// rendered colour, which reflects UXML inline styles reliably, unlike the inline <c>style</c>
/// getter), then <see cref="Apply"/> classifies each into a <see cref="UiTheme"/> slot and writes
/// it as an inline override. Re-applying from the stored original on theme change repaints cleanly.
///
/// Classification mirrors the menus' intent:
///   • background — near-black → panelBg, dark → cardBg, bright+saturated (filled buttons) → accent
///   • border     — → edge
///   • text       — gold/blue → accent, bright → textMain, else → textSub, with a contrast guard
/// Functional status-bar fills (moisture/compaction/saturation/nutrient) are denylisted.
///
/// This is the coloring layer of the in-game UI theming rebuild (PLAN: Themeable In-Game UI Rebuild).
/// </summary>
public static class UiThemePainter
{
    public class Captured
    {
        public bool hasBg, hasBorder, hasText;
        public Color bg, border, text;
    }

    static readonly Color[] Protected =
    {
        new Color(0.314f, 0.627f, 1.000f),  // moisture   (80,160,255)
        new Color(0.706f, 0.471f, 0.157f),  // compaction (180,120,40)
        new Color(0.235f, 0.471f, 0.863f),  // saturation (60,120,220)
        new Color(0.314f, 0.784f, 0.314f),  // nutrient   (80,200,80)
    };

    /// <summary>Reads an element's original colours from resolvedStyle (call once, after layout).</summary>
    public static Captured Capture(VisualElement e)
    {
        var c = new Captured();

        Color bg = e.resolvedStyle.backgroundColor;
        if (bg.a > 0.04f && !IsProtected(bg)) { c.hasBg = true; c.bg = bg; }

        Color bt = e.resolvedStyle.borderTopColor;
        if (bt.a > 0.04f) { c.hasBorder = true; c.border = bt; }

        if (e is TextElement) { c.hasText = true; c.text = e.resolvedStyle.color; }

        return c;
    }

    /// <summary>Maps the captured originals into the active theme and writes inline overrides.</summary>
    public static void Apply(VisualElement e, Captured c)
    {
        if (c == null) return;

        Color mappedBg = default;
        if (c.hasBg) { mappedBg = MapBg(c.bg); e.style.backgroundColor = new StyleColor(mappedBg); }

        if (c.hasText)
        {
            Color tc = MapText(c.text);
            if (c.hasBg) tc = Contrast(mappedBg, tc);
            e.style.color = new StyleColor(tc);
        }

        if (c.hasBorder) SetBorder(e, MapBorder(c.border));
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
