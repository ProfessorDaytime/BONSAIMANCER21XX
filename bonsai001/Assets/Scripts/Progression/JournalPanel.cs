using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The Journal / Encyclopedia overlay — the zen reward layer. Shows earned **Achievements**
/// (milestones) and an **Encyclopedia** (Techniques / Phenomena / Species) that fills in as you
/// play. Read-only, no scoring. Built entirely in code (UI Toolkit), mirroring ItemCatalogPanel.
/// Opened from the Aesthetic-Points chip in <see cref="ProgressionHUD"/>.
///
/// See Docs/PROGRESSION_DESIGN.md §4.
/// </summary>
public class JournalPanel
{
    readonly VisualElement parentRoot;
    VisualElement overlay;

    static Color Scrim    => UiTheme.Current.scrim;
    static Color PanelBg  => UiTheme.Current.panelBg;
    static Color CardBg   => UiTheme.Current.cardBg;
    static Color Gold     => UiTheme.Current.accent;
    static Color Dim      => UiTheme.Current.edge;
    static Color TextMain => UiTheme.Current.textMain;
    static Color TextSub  => UiTheme.Current.textSub;
    static Color LockedC  => UiTheme.Current.locked;

    public bool IsOpen => overlay != null;

    public JournalPanel(VisualElement parentRoot) { this.parentRoot = parentRoot; }

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        Close();
        var profile = ProgressionManager.Profile;

        overlay = new VisualElement();
        overlay.name = "pm-overlay";   // GameUiThemer skips "pm-" elements (we self-theme)
        overlay.style.position        = Position.Absolute;
        overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0f;
        overlay.style.backgroundColor = new StyleColor(Scrim);
        overlay.style.alignItems      = Align.Center;
        overlay.style.justifyContent  = Justify.Center;
        overlay.RegisterCallback<ClickEvent>(e => { if (e.target == overlay) Close(); });

        var panel = new VisualElement();
        panel.style.width     = 480f;
        panel.style.maxHeight = Length.Percent(84);
        panel.style.backgroundColor = new StyleColor(PanelBg);
        Pad(panel, 16);
        Round(panel, 10);
        Border(panel, Dim, 1);

        var header = new Label("Journal");
        header.style.fontSize     = 18;
        header.style.color        = new StyleColor(TextMain);
        header.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        panel.Add(header);

        var sub = new Label(profile != null
            ? $"✦ {profile.aestheticPoints} AP   ·   {profile.gameMode}"
            : "");
        sub.style.fontSize     = 12;
        sub.style.color        = new StyleColor(Gold);
        sub.style.marginBottom = 10;
        panel.Add(sub);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        var body = scroll.contentContainer;
        panel.Add(scroll);

        BuildAchievements(body, profile);
        BuildCategory(body, profile, JournalCategory.Technique,  "Techniques");
        BuildCategory(body, profile, JournalCategory.Phenomenon, "Phenomena");
        BuildCategory(body, profile, JournalCategory.Species,    "Species");

        var close = MakeButton("Close", new Color(0.22f, 0.22f, 0.22f), TextMain);
        close.style.marginTop = 12;
        close.style.alignSelf  = Align.FlexEnd;
        close.RegisterCallback<ClickEvent>(_ => Close());
        panel.Add(close);

        overlay.Add(panel);
        parentRoot.Add(overlay);
    }

    void BuildAchievements(VisualElement body, ProgressionProfile profile)
    {
        if (profile == null) return;
        int shown = 0;
        var section = SectionHeader("Achievements");
        body.Add(section);

        foreach (var id in profile.milestones)
        {
            var def = ProgressionMilestone.Get(id);
            if (def == null) continue;   // dynamic (species) milestones surface under Species
            body.Add(MakeEntry(def.title, def.journalText, unlocked: true));
            shown++;
        }
        if (shown == 0)
            body.Add(Hint("No milestones yet — tend your tree and they'll come."));
    }

    void BuildCategory(VisualElement body, ProgressionProfile profile, JournalCategory cat, string title)
    {
        int unlocked = 0, total = 0;
        var rows = new System.Collections.Generic.List<VisualElement>();
        foreach (var e in Journal.Entries)
        {
            if (e.category != cat) continue;
            total++;
            bool open = profile != null && e.unlocked(profile);
            if (open) unlocked++;
            rows.Add(MakeEntry(open ? e.title : "🔒  " + e.title,
                               open ? e.body  : "Not yet discovered.",
                               open));
        }
        body.Add(SectionHeader($"{title}   ({unlocked}/{total})"));
        foreach (var r in rows) body.Add(r);
    }

    VisualElement MakeEntry(string title, string bodyText, bool unlocked)
    {
        var card = new VisualElement();
        Pad(card, 9);
        card.style.paddingLeft = card.style.paddingRight = 12;
        card.style.marginBottom = 5;
        Round(card, 6);
        card.style.backgroundColor = new StyleColor(CardBg);
        Border(card, Dim, 1);

        var t = new Label(title);
        t.style.fontSize = 13;
        t.style.color    = new StyleColor(unlocked ? TextMain : LockedC);
        t.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        card.Add(t);

        if (!string.IsNullOrEmpty(bodyText))
        {
            var b = new Label(bodyText);
            b.style.fontSize   = 11;
            b.style.color      = new StyleColor(unlocked ? TextSub : LockedC);
            b.style.whiteSpace = WhiteSpace.Normal;
            b.style.marginTop  = 3;
            card.Add(b);
        }
        if (!unlocked) card.style.opacity = 0.7f;
        return card;
    }

    static Label SectionHeader(string text)
    {
        var l = new Label(text);
        l.style.fontSize     = 14;
        l.style.color        = new StyleColor(Gold);
        l.style.marginTop    = 10;
        l.style.marginBottom = 5;
        l.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        return l;
    }

    static Label Hint(string text)
    {
        var l = new Label(text);
        l.style.fontSize   = 11;
        l.style.color      = new StyleColor(TextSub);
        l.style.whiteSpace = WhiteSpace.Normal;
        l.style.marginBottom = 4;
        return l;
    }

    public void Close()
    {
        if (overlay != null && overlay.parent != null) overlay.parent.Remove(overlay);
        overlay = null;
    }

    // ── style helpers (match ItemCatalogPanel) ────────────────────────────────
    static Button MakeButton(string text, Color bg, Color fg)
    {
        var b = new Button { text = text };
        b.style.fontSize        = 13;
        b.style.backgroundColor = new StyleColor(bg);
        b.style.color           = new StyleColor(fg);
        Pad(b, 8);
        b.style.paddingLeft = b.style.paddingRight = 18;
        Round(b, 6);
        b.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        return b;
    }

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
