using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central UI palette for the code-built progression overlays (shop / Journal / Customize / HUD).
/// Panels read their colours from <see cref="Current"/> at build time, so applying a different
/// theme and reopening a panel reskins it; the always-on HUD listens to <see cref="OnThemeChanged"/>
/// and rebuilds itself live.
///
/// Themes are code-defined (no asset authoring) and keyed by id; a UiTheme `ItemDefinition` in the
/// shop carries the matching `themeId`. See Docs/PROGRESSION_DESIGN.md §7.
///
/// (Scope: this themes the progression/shop overlays + AP HUD. The main UXML game UI uses USS and
/// would need its own variable pass — a later follow-up.)
/// </summary>
public class UiTheme
{
    public string id, displayName;

    public Color scrim;     // full-screen dim behind a modal
    public Color panelBg;   // panel background
    public Color cardBg;    // list-card / button background
    public Color cardSel;   // selected card
    public Color accent;    // headers, prices, highlights (the old "Gold")
    public Color edge;      // borders / dim
    public Color textMain;  // primary text
    public Color textSub;   // secondary text
    public Color ink;       // dark text on accent-filled buttons
    public Color locked;    // greyed/locked text
    public Color negative;  // spend / warning accent

    public static UiTheme Current { get; private set; } = Forest();
    public static event Action OnThemeChanged;

    static Dictionary<string, UiTheme> registry;

    public static IEnumerable<UiTheme> All { get { Ensure(); return registry.Values; } }

    /// <summary>Looks up a theme palette by id (null if unknown). Used to preview a theme's own colours.</summary>
    public static UiTheme Get(string id)
    {
        Ensure();
        return (!string.IsNullOrEmpty(id) && registry.TryGetValue(id, out var t)) ? t : null;
    }

    /// <summary>Sets the active theme by id (empty/unknown → the default Forest).</summary>
    public static void Apply(string id)
    {
        Ensure();
        Current = (!string.IsNullOrEmpty(id) && registry.TryGetValue(id, out var t)) ? t : Forest();
        OnThemeChanged?.Invoke();
    }

    static void Ensure()
    {
        if (registry != null) return;
        registry = new Dictionary<string, UiTheme>();
        void R(UiTheme t) => registry[t.id] = t;
        R(Forest()); R(Charcoal()); R(Night()); R(Sakura()); R(Parchment());
    }

    static UiTheme Forest() => new UiTheme
    {
        id = "forest", displayName = "Forest",
        scrim    = new Color(0f, 0f, 0f, 0.72f),
        panelBg  = new Color(0.07f, 0.10f, 0.07f),
        cardBg   = new Color(0.10f, 0.14f, 0.10f),
        cardSel  = new Color(0.14f, 0.19f, 0.11f),
        accent   = new Color(0.898f, 0.702f, 0.086f),
        edge     = new Color(0.20f, 0.28f, 0.20f),
        textMain = new Color(0.92f, 0.88f, 0.78f),
        textSub  = new Color(0.52f, 0.65f, 0.52f),
        ink      = new Color(0.06f, 0.06f, 0.06f),
        locked   = new Color(0.45f, 0.45f, 0.42f),
        negative = new Color(0.82f, 0.55f, 0.42f),
    };

    static UiTheme Charcoal() => new UiTheme
    {
        id = "charcoal", displayName = "Charcoal",
        scrim    = new Color(0f, 0f, 0f, 0.75f),
        panelBg  = new Color(0.10f, 0.10f, 0.11f),
        cardBg   = new Color(0.15f, 0.15f, 0.16f),
        cardSel  = new Color(0.20f, 0.20f, 0.23f),
        accent   = new Color(0.58f, 0.72f, 0.86f),
        edge     = new Color(0.28f, 0.28f, 0.31f),
        textMain = new Color(0.90f, 0.90f, 0.92f),
        textSub  = new Color(0.55f, 0.58f, 0.62f),
        ink      = new Color(0.06f, 0.06f, 0.07f),
        locked   = new Color(0.45f, 0.45f, 0.47f),
        negative = new Color(0.85f, 0.50f, 0.45f),
    };

    static UiTheme Night() => new UiTheme
    {
        id = "night", displayName = "Night",
        scrim    = new Color(0f, 0f, 0.02f, 0.76f),
        panelBg  = new Color(0.06f, 0.08f, 0.14f),
        cardBg   = new Color(0.10f, 0.13f, 0.20f),
        cardSel  = new Color(0.14f, 0.18f, 0.28f),
        accent   = new Color(0.45f, 0.70f, 0.95f),
        edge     = new Color(0.20f, 0.26f, 0.38f),
        textMain = new Color(0.88f, 0.90f, 0.96f),
        textSub  = new Color(0.50f, 0.58f, 0.72f),
        ink      = new Color(0.04f, 0.05f, 0.09f),
        locked   = new Color(0.42f, 0.46f, 0.55f),
        negative = new Color(0.85f, 0.55f, 0.50f),
    };

    static UiTheme Sakura() => new UiTheme
    {
        id = "sakura", displayName = "Sakura",
        scrim    = new Color(0.10f, 0.05f, 0.08f, 0.64f),
        panelBg  = new Color(0.16f, 0.10f, 0.13f),
        cardBg   = new Color(0.22f, 0.14f, 0.18f),
        cardSel  = new Color(0.30f, 0.18f, 0.24f),
        accent   = new Color(0.95f, 0.55f, 0.70f),
        edge     = new Color(0.40f, 0.24f, 0.32f),
        textMain = new Color(0.97f, 0.90f, 0.93f),
        textSub  = new Color(0.75f, 0.55f, 0.64f),
        ink      = new Color(0.10f, 0.05f, 0.07f),
        locked   = new Color(0.55f, 0.45f, 0.50f),
        negative = new Color(0.85f, 0.45f, 0.45f),
    };

    static UiTheme Parchment() => new UiTheme
    {
        id = "parchment", displayName = "Parchment",
        scrim    = new Color(0.20f, 0.16f, 0.10f, 0.52f),
        panelBg  = new Color(0.90f, 0.86f, 0.76f),
        cardBg   = new Color(0.84f, 0.79f, 0.67f),
        cardSel  = new Color(0.78f, 0.72f, 0.58f),
        accent   = new Color(0.55f, 0.40f, 0.15f),
        edge     = new Color(0.65f, 0.58f, 0.45f),
        textMain = new Color(0.18f, 0.14f, 0.08f),
        textSub  = new Color(0.40f, 0.34f, 0.24f),
        ink      = new Color(0.95f, 0.92f, 0.85f),
        locked   = new Color(0.55f, 0.50f, 0.42f),
        negative = new Color(0.70f, 0.30f, 0.20f),
    };
}
