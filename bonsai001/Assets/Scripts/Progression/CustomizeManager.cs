using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The shared entry point for non-placeable cosmetics — Backgrounds, Music, UI Themes (and later
/// Decorations). Opens a small "Customize" menu, and for each category reuses the existing shop
/// (<see cref="ItemCatalogPanel"/>: price / owned / locked / buy). Choosing an item **applies** it
/// (routes to the per-category manager) and **equips** it (saved on the global ProgressionProfile,
/// restored on load).
///
/// Add this component to the scene and assign the same `ItemCatalog` used by buttonClicker. The
/// Aesthetic-Points chip's ⚙ button (ProgressionHUD) opens the menu. Categories whose manager
/// isn't built yet (Music / UI Theme / Decoration) still browse + buy + equip; their apply is a
/// no-op until the manager lands.
///
/// See Docs/PROGRESSION_DESIGN.md.
/// </summary>
public class CustomizeManager : MonoBehaviour
{
    public static CustomizeManager Instance { get; private set; }

    [Tooltip("The shared ItemCatalog (same asset assigned to buttonClicker's Item Catalog field).")]
    [SerializeField] ItemCatalog catalog;

    ItemCatalogPanel catalogPanel;
    VisualElement    root, menu;

    static Color PanelBg  => new Color(UiTheme.Current.panelBg.r, UiTheme.Current.panelBg.g, UiTheme.Current.panelBg.b, 0.96f);
    static Color Gold     => UiTheme.Current.accent;
    static Color TextMain => UiTheme.Current.textMain;
    static Color Edge     => UiTheme.Current.edge;
    static Color CardBg   => UiTheme.Current.cardBg;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => RestoreEquipped();   // after ProgressionManager loaded the profile + managers exist

    VisualElement Root()
    {
        if (root != null) return root;
        var doc = FindFirstObjectByType<UIDocument>();
        root = doc != null ? doc.rootVisualElement : null;
        if (root != null && catalogPanel == null) catalogPanel = new ItemCatalogPanel(root, catalog);
        return root;
    }

    // ── Menu ──────────────────────────────────────────────────────────────────
    public void ToggleMenu()
    {
        if (Root() == null) return;
        if (menu != null) { CloseMenu(); return; }
        menu = BuildMenu();
        root.Add(menu);
    }

    void CloseMenu()
    {
        if (menu != null && menu.parent != null) menu.parent.Remove(menu);
        menu = null;
    }

    VisualElement BuildMenu()
    {
        var overlay = new VisualElement();
        overlay.name = "pm-overlay";   // GameUiThemer skips "pm-" elements (we self-theme)
        overlay.style.position = Position.Absolute;
        overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0f;
        overlay.RegisterCallback<ClickEvent>(e => { if (e.target == overlay) CloseMenu(); });

        var panel = new VisualElement();
        panel.style.position  = Position.Absolute;
        panel.style.top       = 44;
        panel.style.left      = Length.Percent(50);
        panel.style.translate = new Translate(Length.Percent(-50), 0);
        panel.style.minWidth  = 180;
        panel.style.backgroundColor = new StyleColor(PanelBg);
        Pad(panel, 8);
        Round(panel, 8);
        Border(panel, Edge, 1);

        var title = new Label("Customize");
        title.style.color        = new StyleColor(Gold);
        title.style.fontSize     = 14;
        title.style.marginBottom = 6;
        title.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        panel.Add(title);

        panel.Add(MenuButton("Background", ItemCategory.Background, "Backgrounds"));
        panel.Add(MenuButton("Music",      ItemCategory.Music,      "Music"));
        panel.Add(MenuButton("UI Theme",   ItemCategory.UiTheme,    "UI Themes"));
        panel.Add(MenuButton("Decoration", ItemCategory.Decoration, "Decorations"));

        overlay.Add(panel);
        return overlay;
    }

    Button MenuButton(string label, ItemCategory cat, string title)
    {
        var b = new Button { text = label };
        b.style.fontSize        = 13;
        b.style.color           = new StyleColor(TextMain);
        b.style.backgroundColor = new StyleColor(CardBg);
        b.style.marginBottom    = 3;
        Pad(b, 7);
        Round(b, 5);
        b.RegisterCallback<ClickEvent>(_ => OpenCategory(cat, title));
        return b;
    }

    void OpenCategory(ItemCategory cat, string title)
    {
        CloseMenu();
        if (Root() == null || catalogPanel == null) return;
        catalogPanel.Open(cat, title, CurrentEquipped(cat), def => ApplyAndEquip(cat, def));
    }

    // ── Apply + equip + restore ───────────────────────────────────────────────
    void ApplyAndEquip(ItemCategory cat, ItemDefinition def)
    {
        Apply(cat, def);
        var p = ProgressionManager.Profile;
        if (p != null) { SetEquippedId(p, cat, def != null ? def.Id : ""); p.Save(); }
    }

    void Apply(ItemCategory cat, ItemDefinition def)
    {
        switch (cat)
        {
            case ItemCategory.Background:
                BackgroundManager.Instance?.Apply(def);
                break;
            case ItemCategory.UiTheme:
                UiTheme.Apply(def != null ? def.themeId : "");
                break;
            case ItemCategory.Music:
                MusicManager.Instance?.Apply(def);
                break;
            // Decoration manager lands in a later increment.
            default:
                Debug.Log($"[Customize] {cat} apply not implemented yet ({def?.Label}).");
                break;
        }
    }

    public void RestoreEquipped()
    {
        if (catalog == null) return;

        var bg = CurrentEquipped(ItemCategory.Background);
        if (bg != null) BackgroundManager.Instance?.Apply(bg);

        var theme = CurrentEquipped(ItemCategory.UiTheme);
        if (theme != null) UiTheme.Apply(theme.themeId);

        var music = CurrentEquipped(ItemCategory.Music);
        if (music != null) MusicManager.Instance?.Apply(music);
    }

    ItemDefinition CurrentEquipped(ItemCategory cat)
    {
        var p = ProgressionManager.Profile;
        if (p == null || catalog == null) return null;
        string id = EquippedId(p, cat);
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var it in catalog.ByCategory(cat))
            if (it != null && it.Id == id) return it;
        return null;
    }

    static string EquippedId(ProgressionProfile p, ItemCategory cat) => cat switch
    {
        ItemCategory.Background => p.equippedBackground,
        ItemCategory.Music      => p.equippedMusic,
        ItemCategory.UiTheme    => p.equippedTheme,
        _                       => "",
    };

    static void SetEquippedId(ProgressionProfile p, ItemCategory cat, string id)
    {
        switch (cat)
        {
            case ItemCategory.Background: p.equippedBackground = id; break;
            case ItemCategory.Music:      p.equippedMusic      = id; break;
            case ItemCategory.UiTheme:    p.equippedTheme      = id; break;
        }
    }

    // ── style helpers ─────────────────────────────────────────────────────────
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
