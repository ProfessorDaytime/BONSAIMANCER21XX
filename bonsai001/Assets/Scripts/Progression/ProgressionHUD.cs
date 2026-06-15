using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Minimal UI Toolkit HUD for the soft economy: an **Aesthetic Points** balance chip (top-centre)
/// and transient toasts for awards + milestones. Attaches itself to the existing `UIDocument` root
/// so it needs no buttonClicker surgery. Subscribes to <see cref="ProgressionManager"/> events.
///
/// Slice 1 (economy + zen core). The full Journal/Encyclopedia panel and the cosmetic shop come
/// in later slices. See `Docs/PROGRESSION_DESIGN.md`.
/// </summary>
public class ProgressionHUD : MonoBehaviour
{
    static Color PanelBg => new Color(UiTheme.Current.panelBg.r, UiTheme.Current.panelBg.g, UiTheme.Current.panelBg.b, 0.92f);
    static Color Gold    => UiTheme.Current.accent;
    static Color Spend   => UiTheme.Current.negative;
    static Color Border_ => UiTheme.Current.edge;
    static Color Sub     => UiTheme.Current.textSub;

    VisualElement root, chip, toastHost;
    Label         chipLabel;
    JournalPanel  journalPanel;
    bool          built;

    void OnEnable()
    {
        ProgressionManager.OnCurrencyChanged += HandleCurrency;
        ProgressionManager.OnMilestone       += HandleMilestone;
        ProgressionManager.OnToolUnlocked    += HandleToolUnlocked;
        UiTheme.OnThemeChanged               += HandleThemeChanged;
    }

    void OnDisable()
    {
        ProgressionManager.OnCurrencyChanged -= HandleCurrency;
        ProgressionManager.OnMilestone       -= HandleMilestone;
        ProgressionManager.OnToolUnlocked    -= HandleToolUnlocked;
        UiTheme.OnThemeChanged               -= HandleThemeChanged;
    }

    // Rebuild the chip in the new palette. Drop the existing elements and let Update's TryBuild
    // recreate them next frame with the active theme.
    void HandleThemeChanged()
    {
        if (!built) return;
        if (chip != null && chip.parent != null)           chip.parent.Remove(chip);
        if (toastHost != null && toastHost.parent != null) toastHost.parent.Remove(toastHost);
        chip = toastHost = null;
        built = false;
    }

    void Update()
    {
        // The UIDocument root isn't guaranteed to exist on the first frame — keep trying until it is.
        if (!built) TryBuild();
    }

    void TryBuild()
    {
        var doc = FindFirstObjectByType<UIDocument>();
        root = doc != null ? doc.rootVisualElement : null;
        if (root == null) return;

        journalPanel = new JournalPanel(root);

        chip = new VisualElement();
        chip.name                = "pm-chip";   // GameUiThemer skips "pm-" elements (we self-theme)
        chip.style.position      = Position.Absolute;
        chip.style.top           = 10;
        chip.style.left          = Length.Percent(50);
        chip.style.translate     = new Translate(Length.Percent(-50), 0);
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.alignItems    = Align.Center;
        Pad(chip, 6);
        chip.style.paddingLeft = chip.style.paddingRight = 12;
        Round(chip, 10);
        chip.style.backgroundColor = new StyleColor(PanelBg);
        Border(chip, Border_, 1);
        // Click the chip to open the Journal / Encyclopedia.
        chip.RegisterCallback<ClickEvent>(_ => journalPanel?.Toggle());

        chipLabel = new Label("✦ 0 AP");
        chipLabel.style.color    = new StyleColor(Gold);
        chipLabel.style.fontSize = 14;
        chipLabel.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        chip.Add(chipLabel);

        var hint = new Label("  ☰");
        hint.style.color    = new StyleColor(Sub);
        hint.style.fontSize = 13;
        chip.Add(hint);

        // ⚙ opens the Customize menu (backgrounds / music / themes). StopPropagation so it
        // doesn't also toggle the Journal via the chip's click handler.
        var gear = new Label("  ⚙");
        gear.style.color    = new StyleColor(Sub);
        gear.style.fontSize = 14;
        gear.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); CustomizeManager.Instance?.ToggleMenu(); });
        chip.Add(gear);

        root.Add(chip);

        toastHost = new VisualElement();
        toastHost.name              = "pm-toast";
        toastHost.style.position    = Position.Absolute;
        toastHost.style.top         = 46;
        toastHost.style.left        = Length.Percent(50);
        toastHost.style.translate   = new Translate(Length.Percent(-50), 0);
        toastHost.style.alignItems  = Align.Center;
        toastHost.pickingMode       = PickingMode.Ignore;
        root.Add(toastHost);

        built = true;
        if (ProgressionManager.Instance != null)
            chipLabel.text = $"✦ {ProgressionManager.Instance.Balance} AP";
    }

    void HandleCurrency(int balance, int delta, string reason)
    {
        if (chipLabel != null) chipLabel.text = $"✦ {balance} AP";
        if (delta != 0 && built)
            ShowToast((delta > 0 ? $"+{delta}" : $"{delta}") + $" AP · {reason}",
                      delta > 0 ? Gold : Spend, big: false);
    }

    void HandleMilestone(ProgressionMilestone m)
    {
        if (m != null && built) ShowToast($"✦ {m.title}", Gold, big: true);
    }

    void HandleToolUnlocked(string toolId)
    {
        if (built) ShowToast($"✦ Unlocked: {ToolLabel(toolId)}", Gold, big: true);
    }

    static string ToolLabel(string toolId)
    {
        switch (toolId)
        {
            case "trim":      return "Pruning";
            case "wire":      return "Wiring";
            case "pinch":     return "Pinching";
            case "defoliate": return "Defoliation";
            case "soil":      return "Repotting & Feeding";
            case "root":      return "Root Work";
            case "advanced":  return "Air Layering & Rock Planting";
            default:          return toolId;
        }
    }

    void ShowToast(string text, Color color, bool big)
    {
        if (toastHost == null) return;

        var t = new Label(text);
        t.style.color    = new StyleColor(color);
        t.style.fontSize = big ? 18 : 13;
        t.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        Pad(t, 6);
        t.style.paddingLeft = t.style.paddingRight = 14;
        Round(t, 8);
        t.style.backgroundColor = new StyleColor(PanelBg);
        t.style.marginBottom    = 4;
        t.pickingMode           = PickingMode.Ignore;
        toastHost.Add(t);

        StartCoroutine(FadeAndRemove(t, big ? 2.6f : 1.8f));
    }

    IEnumerator FadeAndRemove(VisualElement e, float hold)
    {
        e.style.opacity = 0f;
        float ti = 0f;
        while (ti < 0.2f) { ti += Time.unscaledDeltaTime; e.style.opacity = Mathf.Clamp01(ti / 0.2f); yield return null; }
        e.style.opacity = 1f;

        yield return new WaitForSecondsRealtime(hold);

        float to = 0f;
        while (to < 0.5f) { to += Time.unscaledDeltaTime; e.style.opacity = 1f - Mathf.Clamp01(to / 0.5f); yield return null; }
        if (e.parent != null) e.parent.Remove(e);
    }

    // ── style helpers (match ItemCatalogPanel idiom) ──────────────────────────
    static void Pad(VisualElement e, float v)
    {
        e.style.paddingTop = e.style.paddingBottom = e.style.paddingLeft = e.style.paddingRight = v;
    }

    static void Round(VisualElement e, float r)
    {
        e.style.borderTopLeftRadius = e.style.borderTopRightRadius =
            e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
    }

    static void Border(VisualElement e, Color c, float w)
    {
        e.style.borderTopWidth = e.style.borderBottomWidth =
            e.style.borderLeftWidth = e.style.borderRightWidth = w;
        e.style.borderTopColor = e.style.borderBottomColor =
            e.style.borderLeftColor = e.style.borderRightColor = new StyleColor(c);
    }
}
