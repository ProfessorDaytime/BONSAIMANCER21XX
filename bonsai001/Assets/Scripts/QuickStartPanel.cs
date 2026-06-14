using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Modal for the Quick-Start flow: pick a target style + age, then generate a developed tree.
/// (Species comes from the new-game species selection.) Built in code — no UXML — and styled to
/// match the catalog/species look: dark panel, style cards (click to select, gold highlight), a row
/// of age presets, Cancel / "Grow it". Fires back with (style, ageYears) on confirm; the actual
/// fast-forward growth is handled by <see cref="QuickStartManager"/>.
/// </summary>
public class QuickStartPanel
{
    readonly VisualElement parentRoot;

    VisualElement overlay;
    VisualElement styleList;
    Button        generateButton;
    StyleDefinition selectedStyle;
    VisualElement   selectedCard;
    int ageYears = 10;
    readonly List<(Button btn, int years)> ageButtons = new List<(Button, int)>();
    Action<StyleDefinition, int> onGenerate;

    static readonly int[] AgePresets = { 5, 10, 20, 40 };

    static readonly Color Scrim    = new Color(0f, 0f, 0f, 0.72f);
    static readonly Color PanelBg  = new Color(0.07f, 0.10f, 0.07f);
    static readonly Color CardBg   = new Color(0.10f, 0.14f, 0.10f);
    static readonly Color CardSel  = new Color(0.14f, 0.19f, 0.11f);
    static readonly Color Gold     = new Color(0.898f, 0.702f, 0.086f);
    static readonly Color Dim      = new Color(0.25f, 0.25f, 0.22f);
    static readonly Color TextMain = new Color(0.92f, 0.88f, 0.78f);
    static readonly Color TextSub  = new Color(0.52f, 0.65f, 0.52f);
    static readonly Color Ink      = new Color(0.06f, 0.06f, 0.06f);

    public bool IsOpen => overlay != null;

    public QuickStartPanel(VisualElement parentRoot) { this.parentRoot = parentRoot; }

    /// <summary>Opens the picker. <paramref name="onGenerate"/> fires with the chosen style + age.</summary>
    public void Open(IReadOnlyList<StyleDefinition> styles, int defaultAge,
                     Action<StyleDefinition, int> onGenerate)
    {
        Close();
        this.onGenerate = onGenerate;
        selectedStyle = null;
        selectedCard  = null;
        ageYears      = defaultAge > 0 ? defaultAge : 10;
        ageButtons.Clear();

        overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0f;
        overlay.style.backgroundColor = new StyleColor(Scrim);
        overlay.style.alignItems      = Align.Center;
        overlay.style.justifyContent  = Justify.Center;
        overlay.RegisterCallback<ClickEvent>(e => { if (e.target == overlay) Close(); });

        var panel = new VisualElement();
        panel.style.width     = 440f;
        panel.style.maxHeight = Length.Percent(85);
        panel.style.backgroundColor = new StyleColor(PanelBg);
        Pad(panel, 16); Round(panel, 10); Border(panel, new Color(0.20f, 0.28f, 0.20f), 1);

        var header = new Label("Quick-Start");
        header.style.fontSize = 18;
        header.style.color    = new StyleColor(TextMain);
        header.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        panel.Add(header);

        var sub = new Label("Grow a developed tree — pick a style and a target age, then watch it grow.");
        sub.style.fontSize   = 11;
        sub.style.color      = new StyleColor(TextSub);
        sub.style.whiteSpace = WhiteSpace.Normal;
        sub.style.marginBottom = 10;
        panel.Add(sub);

        panel.Add(SectionLabel("STYLE"));

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow  = 1;
        scroll.style.maxHeight = 220;
        styleList = scroll.contentContainer;
        panel.Add(scroll);

        if (styles == null || styles.Count == 0)
        {
            var empty = new Label("No styles available.\nAdd StyleDefinition assets to QuickStartManager's Styles list.");
            empty.style.color      = new StyleColor(TextSub);
            empty.style.whiteSpace = WhiteSpace.Normal;
            empty.style.marginTop  = 6;
            styleList.Add(empty);
        }
        else
        {
            foreach (var s in styles) if (s != null) styleList.Add(MakeStyleCard(s));
        }

        panel.Add(SectionLabel("AGE"));

        var ageRow = new VisualElement();
        ageRow.style.flexDirection = FlexDirection.Row;
        foreach (int yrs in AgePresets)
        {
            var b = MakeButton($"{yrs} yr", Dim, TextMain);
            b.style.flexGrow   = 1;
            b.style.marginRight = 4;
            int captured = yrs;
            b.RegisterCallback<ClickEvent>(_ => SelectAge(captured));
            ageButtons.Add((b, yrs));
            ageRow.Add(b);
        }
        panel.Add(ageRow);

        var footer = new VisualElement();
        footer.style.flexDirection  = FlexDirection.Row;
        footer.style.justifyContent = Justify.SpaceBetween;
        footer.style.marginTop      = 14;

        var cancel = MakeButton("Cancel", new Color(0.22f, 0.22f, 0.22f), TextMain);
        cancel.RegisterCallback<ClickEvent>(_ => Close());

        generateButton = MakeButton("Grow it ▶", Dim, new Color(0.5f, 0.5f, 0.5f));
        generateButton.RegisterCallback<ClickEvent>(_ => DoGenerate());

        footer.Add(cancel);
        footer.Add(generateButton);
        panel.Add(footer);

        overlay.Add(panel);
        parentRoot.Add(overlay);

        SelectAge(ageYears);
        RefreshGenerate();
    }

    VisualElement MakeStyleCard(StyleDefinition s)
    {
        var card = new VisualElement();
        Pad(card, 9);
        card.style.paddingLeft = card.style.paddingRight = 12;
        card.style.marginBottom = 5;
        Round(card, 6);
        card.style.backgroundColor = new StyleColor(CardBg);
        Border(card, Dim, 1);

        var name = new Label(string.IsNullOrWhiteSpace(s.styleName) ? s.name : s.styleName);
        name.style.fontSize = 14;
        name.style.color    = new StyleColor(TextMain);
        name.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        card.Add(name);

        card.RegisterCallback<ClickEvent>(_ => SelectStyle(s, card));
        return card;
    }

    void SelectStyle(StyleDefinition s, VisualElement card)
    {
        if (selectedCard != null)
        {
            selectedCard.style.backgroundColor = new StyleColor(CardBg);
            Border(selectedCard, Dim, 1);
        }
        selectedStyle = s;
        selectedCard  = card;
        card.style.backgroundColor = new StyleColor(CardSel);
        Border(card, Gold, 1);
        RefreshGenerate();
    }

    void SelectAge(int yrs)
    {
        ageYears = yrs;
        foreach (var (btn, years) in ageButtons)
        {
            bool on = years == yrs;
            btn.style.backgroundColor = new StyleColor(on ? Gold : Dim);
            btn.style.color           = new StyleColor(on ? Ink  : TextMain);
        }
    }

    void RefreshGenerate()
    {
        if (generateButton == null) return;
        bool on = selectedStyle != null;
        generateButton.SetEnabled(on);
        generateButton.style.backgroundColor = new StyleColor(on ? Gold : Dim);
        generateButton.style.color           = new StyleColor(on ? Ink  : new Color(0.5f, 0.5f, 0.5f));
    }

    void DoGenerate()
    {
        if (selectedStyle == null) return;
        var st = selectedStyle;
        var a  = ageYears;
        var cb = onGenerate;
        Close();
        cb?.Invoke(st, a);
    }

    public void Close()
    {
        if (overlay != null && overlay.parent != null) overlay.parent.Remove(overlay);
        overlay = null; styleList = null; generateButton = null; selectedCard = null;
        ageButtons.Clear();
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    static Label SectionLabel(string text)
    {
        var l = new Label(text);
        l.style.fontSize    = 10;
        l.style.color       = new StyleColor(TextSub);
        l.style.marginTop   = 10;
        l.style.marginBottom = 4;
        return l;
    }

    static Button MakeButton(string text, Color bg, Color fg)
    {
        var b = new Button { text = text };
        b.style.fontSize        = 13;
        b.style.backgroundColor = new StyleColor(bg);
        b.style.color           = new StyleColor(fg);
        Pad(b, 8);
        b.style.paddingLeft = b.style.paddingRight = 16;
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
