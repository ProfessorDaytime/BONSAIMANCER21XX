using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared modal card-grid overlay for picking an <see cref="ItemDefinition"/> by category (pots,
/// rocks, tables, ground cover). Built entirely in code — no UXML — so any caller can open it with
/// a category + confirm callback. Reuses the SpeciesSelect look: dark panel, cards with
/// thumbnail/swatch + name, click to select (gold highlight), Cancel / Confirm. One instance is
/// reused for every category. Lives outside MonoBehaviour so it can be unit-constructed and reused.
/// </summary>
public class ItemCatalogPanel
{
    readonly VisualElement parentRoot;
    readonly ItemCatalog   catalog;

    VisualElement          overlay;        // full-screen scrim + panel; null when closed
    VisualElement          listContainer;
    Button                 confirmButton;
    ItemDefinition         selected;
    VisualElement          selectedCard;
    Action<ItemDefinition> onConfirm;

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

    public ItemCatalogPanel(VisualElement parentRoot, ItemCatalog catalog)
    {
        this.parentRoot = parentRoot;
        this.catalog    = catalog;
    }

    /// <summary>Opens the catalog for one category. <paramref name="current"/> is pre-selected
    /// (may be null). <paramref name="onConfirm"/> fires with the chosen item on Choose.</summary>
    public void Open(ItemCategory category, string title, ItemDefinition current,
                     Action<ItemDefinition> onConfirm)
    {
        Close();
        this.onConfirm = onConfirm;
        selected       = current;
        selectedCard   = null;

        overlay = new VisualElement();
        overlay.style.position        = Position.Absolute;
        overlay.style.left  = overlay.style.right = overlay.style.top = overlay.style.bottom = 0f;
        overlay.style.backgroundColor = new StyleColor(Scrim);
        overlay.style.alignItems      = Align.Center;
        overlay.style.justifyContent  = Justify.Center;
        // Click on the scrim (outside the panel) cancels.
        overlay.RegisterCallback<ClickEvent>(e => { if (e.target == overlay) Close(); });

        var panel = new VisualElement();
        panel.style.width     = 440f;
        panel.style.maxHeight = Length.Percent(82);
        panel.style.backgroundColor = new StyleColor(PanelBg);
        Pad(panel, 16);
        Round(panel, 10);
        Border(panel, new Color(0.20f, 0.28f, 0.20f), 1);

        var header = new Label(title);
        header.style.fontSize     = 18;
        header.style.color        = new StyleColor(TextMain);
        header.style.marginBottom = 10;
        header.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        panel.Add(header);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        listContainer = scroll.contentContainer;
        panel.Add(scroll);

        var items = catalog != null ? catalog.ByCategory(category) : new List<ItemDefinition>();
        if (items.Count == 0)
        {
            var empty = new Label($"No {category.ToString().ToLower()} items in the catalog yet.\n" +
                                  "Add ItemDefinition assets to the ItemCatalog to populate this list.");
            empty.style.color      = new StyleColor(TextSub);
            empty.style.whiteSpace = WhiteSpace.Normal;
            empty.style.marginTop  = 8;
            listContainer.Add(empty);
        }
        else
        {
            foreach (var it in items) listContainer.Add(MakeCard(it));
        }

        var footer = new VisualElement();
        footer.style.flexDirection  = FlexDirection.Row;
        footer.style.justifyContent = Justify.SpaceBetween;
        footer.style.marginTop      = 12;

        var cancel = MakeButton("Cancel", new Color(0.22f, 0.22f, 0.22f), TextMain);
        cancel.RegisterCallback<ClickEvent>(_ => Close());

        confirmButton = MakeButton("Choose", Dim, new Color(0.5f, 0.5f, 0.5f));
        confirmButton.RegisterCallback<ClickEvent>(_ => Confirm());

        footer.Add(cancel);
        footer.Add(confirmButton);
        panel.Add(footer);

        overlay.Add(panel);
        parentRoot.Add(overlay);
        RefreshConfirm();
    }

    VisualElement MakeCard(ItemDefinition it)
    {
        bool locked = !it.unlockedByDefault;   // future: query the progression system for unlockId

        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems    = Align.Center;
        Pad(card, 8);
        card.style.paddingLeft = card.style.paddingRight = 12;
        card.style.marginBottom = 5;
        Round(card, 6);
        card.style.backgroundColor = new StyleColor(it == selected ? CardSel : CardBg);
        Border(card, it == selected ? Gold : Dim, 1);

        var swatch = new VisualElement();
        swatch.style.width = swatch.style.height = 40;
        swatch.style.marginRight = 12;
        swatch.style.flexShrink  = 0;
        Round(swatch, 4);
        if (it.thumbnail != null) swatch.style.backgroundImage = new StyleBackground(it.thumbnail);
        else                      swatch.style.backgroundColor = new StyleColor(it.swatchColor);
        card.Add(swatch);

        var col = new VisualElement();
        col.style.flexGrow = 1;

        var name = new Label(it.Label);
        name.style.fontSize = 14;
        name.style.color    = new StyleColor(TextMain);
        name.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        col.Add(name);

        if (!string.IsNullOrWhiteSpace(it.description))
        {
            var desc = new Label(it.description);
            desc.style.fontSize   = 10;
            desc.style.color      = new StyleColor(TextSub);
            desc.style.whiteSpace = WhiteSpace.Normal;
            col.Add(desc);
        }
        card.Add(col);

        if (locked)
        {
            var lockTag = new Label("LOCKED");
            lockTag.style.fontSize  = 9;
            lockTag.style.color     = new StyleColor(new Color(0.8f, 0.5f, 0.4f));
            lockTag.style.marginLeft = 8;
            card.Add(lockTag);
            card.style.opacity = 0.5f;
        }
        else
        {
            card.RegisterCallback<ClickEvent>(_ => Select(it, card));
        }

        if (it == selected) selectedCard = card;
        return card;
    }

    void Select(ItemDefinition it, VisualElement card)
    {
        if (selectedCard != null)
        {
            selectedCard.style.backgroundColor = new StyleColor(CardBg);
            Border(selectedCard, Dim, 1);
        }
        selected     = it;
        selectedCard = card;
        card.style.backgroundColor = new StyleColor(CardSel);
        Border(card, Gold, 1);
        RefreshConfirm();
    }

    void RefreshConfirm()
    {
        if (confirmButton == null) return;
        bool on = selected != null;
        confirmButton.SetEnabled(on);
        confirmButton.style.backgroundColor = new StyleColor(on ? Gold : Dim);
        confirmButton.style.color           = new StyleColor(on ? Ink : new Color(0.5f, 0.5f, 0.5f));
    }

    void Confirm()
    {
        if (selected == null) return;
        var pick = selected;
        var cb   = onConfirm;
        Close();
        cb?.Invoke(pick);
    }

    public void Close()
    {
        if (overlay != null && overlay.parent != null) overlay.parent.Remove(overlay);
        overlay = null; listContainer = null; confirmButton = null; selectedCard = null;
    }

    // ── small style helpers ──────────────────────────────────────────────────
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
