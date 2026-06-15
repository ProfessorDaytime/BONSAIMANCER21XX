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
    Label                  balanceLabel;   // shop balance header (null when no economy)
    ItemCategory           openCategory;   // category currently shown (for list refresh after a buy)
    ItemDefinition         selected;
    VisualElement          selectedCard;
    Action<ItemDefinition> onConfirm;

    // Colours come from the active UiTheme so the shop reskins with the equipped UI theme.
    static Color Scrim    => UiTheme.Current.scrim;
    static Color PanelBg  => UiTheme.Current.panelBg;
    static Color CardBg   => UiTheme.Current.cardBg;
    static Color CardSel  => UiTheme.Current.cardSel;
    static Color Gold     => UiTheme.Current.accent;
    static Color Dim      => UiTheme.Current.edge;
    static Color TextMain => UiTheme.Current.textMain;
    static Color TextSub  => UiTheme.Current.textSub;
    static Color Ink      => UiTheme.Current.ink;

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
        openCategory   = category;
        selected       = current;
        selectedCard   = null;

        overlay = new VisualElement();
        overlay.name = "pm-overlay";   // GameUiThemer skips "pm-" elements (we self-theme)
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

        // Balance header — only when the economy is present (so the catalog still works without it).
        balanceLabel = null;
        if (ProgressionManager.Instance != null)
        {
            balanceLabel = new Label();
            balanceLabel.style.color        = new StyleColor(Gold);
            balanceLabel.style.fontSize     = 13;
            balanceLabel.style.marginBottom = 8;
            balanceLabel.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
            panel.Add(balanceLabel);
            UpdateBalanceLabel();
        }

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
        // Resolve shop state. Without a ProgressionManager the catalog ignores the economy
        // (free = unlockedByDefault), so it keeps working in scenes with no progression.
        var  pm = ProgressionManager.Instance;
        bool milestoneLocked, owned, purchasable;
        if (pm == null)
        {
            milestoneLocked = !it.unlockedByDefault;
            owned           = !milestoneLocked;
            purchasable     = false;
        }
        else
        {
            bool granted    = pm.Owns(it.Id);
            milestoneLocked = !it.unlockedByDefault && !granted;          // must be earned via a milestone
            owned           = granted || (it.unlockedByDefault && it.cost <= 0);
            purchasable     = !milestoneLocked && !owned && it.cost > 0;
        }
        bool locked = milestoneLocked;

        // UI-theme cards preview their OWN palette so you can see what you're buying; everything
        // else uses the active theme's colours.
        var   ct        = it.category == ItemCategory.UiTheme ? UiTheme.Get(it.themeId) : null;
        Color cCardBg   = ct != null ? ct.cardBg   : CardBg;
        Color cCardSel  = ct != null ? ct.cardSel  : CardSel;
        Color cAccent   = ct != null ? ct.accent   : Gold;
        Color cEdge     = ct != null ? ct.edge     : Dim;
        Color cTextMain = ct != null ? ct.textMain : TextMain;
        Color cTextSub  = ct != null ? ct.textSub  : TextSub;
        Color cInk      = ct != null ? ct.ink      : Ink;

        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems    = Align.Center;
        Pad(card, 8);
        card.style.paddingLeft = card.style.paddingRight = 12;
        card.style.marginBottom = 5;
        Round(card, 6);
        card.style.backgroundColor = new StyleColor(it == selected ? cCardSel : cCardBg);
        Border(card, it == selected ? cAccent : cEdge, 1);

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
        name.style.color    = new StyleColor(cTextMain);
        name.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        col.Add(name);

        if (!string.IsNullOrWhiteSpace(it.description))
        {
            var desc = new Label(it.description);
            desc.style.fontSize   = 10;
            desc.style.color      = new StyleColor(cTextSub);
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
        else if (purchasable)
        {
            bool affordable = ProgressionManager.Instance != null
                              && ProgressionManager.Instance.CanAfford(it.cost);
            var buy = MakeButton($"✦ {it.cost}", affordable ? cAccent : cEdge,
                                                 affordable ? cInk    : new Color(0.5f, 0.5f, 0.5f));
            buy.style.marginLeft = 8;
            buy.SetEnabled(affordable);
            buy.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); TryBuy(it, card); });
            card.Add(buy);
            // Not selectable until bought; dim slightly to read as "for sale".
            card.style.opacity = 0.85f;
        }
        else   // owned / free
        {
            var ownTag = new Label("✓");
            ownTag.style.fontSize  = 12;
            ownTag.style.color     = new StyleColor(cTextSub);
            ownTag.style.marginLeft = 8;
            card.Add(ownTag);
            card.RegisterCallback<ClickEvent>(_ => Select(it, card));
        }

        if (it == selected) selectedCard = card;
        return card;
    }

    /// <summary>Attempts to buy an item with Aesthetic Points; on success grants ownership and
    /// swaps the card to its owned state (selected).</summary>
    void TryBuy(ItemDefinition it, VisualElement oldCard)
    {
        var pm = ProgressionManager.Instance;
        if (pm == null || it.cost <= 0) return;
        if (!pm.TrySpend(it.cost, $"Bought {it.Label}")) return;   // not affordable → no change
        pm.GrantItem(it.Id);

        // The bought item is now owned and becomes the selection; rebuild the whole list so every
        // card re-evaluates affordability against the new balance.
        selected = it;
        RefreshList();
        UpdateBalanceLabel();
        RefreshConfirm();
    }

    /// <summary>Rebuilds the card list for the open category (after a purchase changes ownership/balance).</summary>
    void RefreshList()
    {
        if (listContainer == null) return;
        listContainer.Clear();
        selectedCard = null;
        var items = catalog != null ? catalog.ByCategory(openCategory) : new List<ItemDefinition>();
        foreach (var it in items) listContainer.Add(MakeCard(it));
    }

    void UpdateBalanceLabel()
    {
        if (balanceLabel == null || ProgressionManager.Instance == null) return;
        balanceLabel.text = $"✦ {ProgressionManager.Instance.Balance} AP";
    }

    void Select(ItemDefinition it, VisualElement card)
    {
        // Each card uses its own theme's colours (so a UI-theme card's selection previews that theme).
        if (selectedCard != null && selected != null)
        {
            var (bg, _, _, edge) = CardColors(selected);
            selectedCard.style.backgroundColor = new StyleColor(bg);
            Border(selectedCard, edge, 1);
        }
        selected     = it;
        selectedCard = card;
        var (_, sel, accent, _) = CardColors(it);
        card.style.backgroundColor = new StyleColor(sel);
        Border(card, accent, 1);
        RefreshConfirm();
    }

    /// <summary>Palette for a card — its own theme's colours for UI-theme items, else the active theme.</summary>
    (Color cardBg, Color cardSel, Color accent, Color edge) CardColors(ItemDefinition it)
    {
        var ct = it != null && it.category == ItemCategory.UiTheme ? UiTheme.Get(it.themeId) : null;
        return ct != null ? (ct.cardBg, ct.cardSel, ct.accent, ct.edge)
                          : (CardBg, CardSel, Gold, Dim);
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
