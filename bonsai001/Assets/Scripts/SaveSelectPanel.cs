using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The save-slot card list — one implementation, two hosts (PLAN "Release Prep" R1).
///
/// <para>Extracted from <c>buttonClicker.MakeSaveCard</c> so the main menu and the in-game pause
/// menu show the same thing. It can live in the main-menu scene because
/// <see cref="SaveManager"/> is a static class doing nothing but file IO — no scene
/// dependencies, so the picker is fully portable.</para>
///
/// <para>The host supplies the container and decides what Load means: the main menu sets a
/// pending launch and loads the game scene, while the in-game menu loads into the live
/// skeleton. Everything else — layout, theming, the two-click delete, thumbnail decoding — is
/// shared.</para>
///
/// <para>Instance-based rather than static because the two-click delete needs per-panel state:
/// two hosts must not share an armed DELETE button.</para>
/// </summary>
public class SaveSelectPanel
{
    // ── Host wiring ───────────────────────────────────────────────────────────

    /// <summary>Slot the host should load. The host owns what that means.</summary>
    public Action<string> OnLoadRequested;

    /// <summary>Fired after a slot is actually deleted (second DELETE click), with the number of
    /// saves remaining. The in-game host uses 0 to fall through to a new game.</summary>
    public Action<int> OnSlotDeleted;

    // ── State ─────────────────────────────────────────────────────────────────

    VisualElement container;
    string pendingDeleteSlotId;

    /// <summary>Thumbnail textures created for the current card set. Decoded thumbnails are
    /// unmanaged allocations that <c>Clear()</c>ing the container does not release, so they are
    /// tracked and destroyed on every rebuild — the pre-extraction code leaked one texture per
    /// card per rebuild, and the load menu is rebuilt every time it opens.</summary>
    readonly List<Texture2D> thumbTextures = new List<Texture2D>();

    // ── Paths ─────────────────────────────────────────────────────────────────

    /// <summary>Screenshot thumbnail for a slot, written alongside its save.json.</summary>
    public static string ThumbPath(string slotId) =>
        System.IO.Path.Combine(SaveManager.SavesRoot, slotId, "thumb.png");

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <paramref name="into"/> with one card per save, newest first. Safe to call
    /// repeatedly — each call releases the previous card set's thumbnails first.
    /// </summary>
    public void Build(VisualElement into)
    {
        if (into == null) return;

        container = into;
        container.Clear();
        ReleaseThumbnails();
        pendingDeleteSlotId = null;

        var saves = SaveManager.ListAllSaves();
        if (saves.Count == 0)
        {
            var empty = new Label { text = "No saved games found." };
            empty.style.color          = new StyleColor(UiTheme.Current.textSub);
            empty.style.fontSize       = 13;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.marginTop      = 40;
            container.Add(empty);
            return;
        }

        foreach (var meta in saves)
            container.Add(MakeCard(meta));
    }

    /// <summary>Releases decoded thumbnail textures. Call when tearing the panel down.</summary>
    public void ReleaseThumbnails()
    {
        foreach (var t in thumbTextures)
            if (t != null) UnityEngine.Object.Destroy(t);
        thumbTextures.Clear();
    }

    // ── Card ──────────────────────────────────────────────────────────────────

    VisualElement MakeCard(SaveMeta meta)
    {
        var th = UiTheme.Current;

        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems    = Align.Center;
        card.style.backgroundColor = new StyleColor(th.cardBg);
        card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
        card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
        card.style.marginBottom = 8;
        card.style.paddingTop = card.style.paddingBottom = 10;
        card.style.paddingLeft = card.style.paddingRight = 14;

        AddThumbnail(card, meta);

        // ── Info column ──
        var info = new VisualElement();
        info.style.flexGrow = 1;
        info.style.flexShrink = 1;
        info.style.marginRight = 12;

        var nameLabel = new Label { text = string.IsNullOrEmpty(meta.saveName) ? "Unnamed" : meta.saveName };
        nameLabel.style.fontSize = 16;
        nameLabel.style.color    = new StyleColor(th.textMain);
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.marginBottom = 4;
        info.Add(nameLabel);

        info.Add(OriginRow(meta, th));
        info.Add(DateRow(meta, th));
        info.Add(HealthRow(meta, th));

        // Schema warning — a save from a newer build cannot be loaded, and saying so on the card
        // beats letting the player click LOAD and get a toast. See SaveManager.IsSchemaLoadable.
        bool loadable = SaveManager.IsSchemaLoadable(meta.schemaVersion);
        if (!loadable)
        {
            var warn = new Label { text = "⚠ Made with a newer version of the game" };
            warn.style.fontSize  = 10;
            warn.style.color     = new StyleColor(th.negative);
            warn.style.marginTop = 4;
            warn.style.whiteSpace = WhiteSpace.Normal;
            info.Add(warn);
        }

        card.Add(info);
        card.Add(ButtonColumn(meta, card, th, loadable));
        return card;
    }

    void AddThumbnail(VisualElement card, SaveMeta meta)
    {
        if (string.IsNullOrEmpty(meta.slotId)) return;

        string path = ThumbPath(meta.slotId);
        if (!System.IO.File.Exists(path)) return;

        try
        {
            var data = System.IO.File.ReadAllBytes(path);
            var tex  = new Texture2D(2, 2);
            if (!tex.LoadImage(data))
            {
                UnityEngine.Object.Destroy(tex);
                return;
            }

            thumbTextures.Add(tex);

            var el = new VisualElement();
            el.style.width = el.style.minWidth = 80;
            el.style.height = el.style.minHeight = 45;
            el.style.marginRight = 12;
            el.style.borderTopLeftRadius = el.style.borderTopRightRadius =
            el.style.borderBottomLeftRadius = el.style.borderBottomRightRadius = 4;
            el.style.backgroundImage = new StyleBackground(tex);
            card.Add(el);
        }
        catch { /* unreadable thumb is cosmetic — the card still works */ }
    }

    static VisualElement OriginRow(SaveMeta meta, UiTheme th)
    {
        var row = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 3 }
        };

        var (badgeText, badgeCol) = (TreeOrigin)meta.treeOrigin switch
        {
            TreeOrigin.AirLayer => ("✦ Air Layer", new Color(0.3f,  0.85f, 0.65f)),
            TreeOrigin.Cutting  => ("✂ Cutting",   new Color(0.85f, 0.75f, 0.3f)),
            _                   => ("● Seedling",  new Color(0.5f,  0.85f, 0.4f)),
        };

        var badge = new Label { text = badgeText };
        badge.style.fontSize = 10;
        badge.style.color    = new StyleColor(badgeCol);
        badge.style.backgroundColor = new StyleColor(
            new Color(badgeCol.r * 0.15f, badgeCol.g * 0.15f, badgeCol.b * 0.15f, 0.8f));
        badge.style.paddingLeft = badge.style.paddingRight = 5;
        badge.style.paddingTop  = badge.style.paddingBottom = 2;
        badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius =
        badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 3;
        badge.style.marginRight = 7;
        row.Add(badge);

        var spec = new Label { text = meta.speciesName ?? "" };
        spec.style.fontSize = 12;
        spec.style.color    = new StyleColor(th.textSub);
        row.Add(spec);

        return row;
    }

    static Label DateRow(SaveMeta meta, UiTheme th)
    {
        string monthName = meta.month switch
        {
            1 => "Jan", 2 => "Feb", 3 => "Mar", 4 => "Apr", 5 => "May", 6 => "Jun",
            7 => "Jul", 8 => "Aug", 9 => "Sep", 10 => "Oct", 11 => "Nov", 12 => "Dec",
            _ => "???"
        };
        string ageTxt = meta.treeAge <= 0 ? "seedling"
                      : meta.treeAge == 1 ? "1 yr"
                                          : $"{meta.treeAge} yrs";

        var row = new Label { text = $"{monthName} {meta.year}  ·  {ageTxt}" };
        row.style.fontSize = 11;
        row.style.color    = new StyleColor(th.locked);
        return row;
    }

    static VisualElement HealthRow(SaveMeta meta, UiTheme th)
    {
        var row = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 4 }
        };

        var label = new Label { text = "Health " };
        label.style.fontSize = 10;
        label.style.color    = new StyleColor(th.locked);
        row.Add(label);

        float h = Mathf.Clamp01(meta.avgHealth);
        Color hCol = h > 0.6f ? new Color(0.25f, 0.75f, 0.25f)
                   : h > 0.3f ? new Color(0.85f, 0.65f, 0.1f)
                              : new Color(0.75f, 0.1f,  0.1f);

        var bg = new VisualElement();
        bg.style.width = 80;
        bg.style.height = 5;
        bg.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));
        bg.style.borderTopLeftRadius = bg.style.borderTopRightRadius =
        bg.style.borderBottomLeftRadius = bg.style.borderBottomRightRadius = 2;

        var fill = new VisualElement();
        fill.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
        fill.style.width  = new StyleLength(new Length(h * 100f, LengthUnit.Percent));
        fill.style.backgroundColor = new StyleColor(hCol);
        fill.style.borderTopLeftRadius = fill.style.borderTopRightRadius =
        fill.style.borderBottomLeftRadius = fill.style.borderBottomRightRadius = 2;
        bg.Add(fill);
        row.Add(bg);

        var pct = new Label { text = $"  {h * 100f:F0}%" };
        pct.style.fontSize = 10;
        pct.style.color    = new StyleColor(hCol);
        row.Add(pct);

        return row;
    }

    VisualElement ButtonColumn(SaveMeta meta, VisualElement card, UiTheme th, bool loadable)
    {
        var col = new VisualElement();
        col.style.flexDirection = FlexDirection.Column;
        col.style.alignItems    = Align.FlexEnd;

        string slotId = meta.slotId;

        var loadBtn = new Button { text = "LOAD" };
        StyleButton(loadBtn, th.accent, th.ink);
        loadBtn.style.marginBottom = 5;
        if (loadable)
            loadBtn.RegisterCallback<ClickEvent>(_ => OnLoadRequested?.Invoke(slotId));
        else
        {
            loadBtn.SetEnabled(false);
            loadBtn.style.opacity = 0.4f;
        }
        col.Add(loadBtn);

        // Two-click delete: the first click arms, the second commits. Deliberately not a modal —
        // the armed state is visible on the button itself and resets on any rebuild.
        var delBtn = new Button { text = "DELETE" };
        StyleButton(delBtn, th.cardSel, th.textSub);
        delBtn.style.fontSize = 11;
        delBtn.RegisterCallback<ClickEvent>(_ => HandleDelete(slotId, delBtn, card));
        col.Add(delBtn);

        return col;
    }

    void HandleDelete(string slotId, Button delBtn, VisualElement card)
    {
        if (pendingDeleteSlotId == slotId)
        {
            SaveManager.DeleteSlot(slotId);
            container?.Remove(card);
            pendingDeleteSlotId = null;
            OnSlotDeleted?.Invoke(SaveManager.ListAllSaves().Count);
            return;
        }

        pendingDeleteSlotId = slotId;
        delBtn.text = "SURE?";
        delBtn.style.backgroundColor = new StyleColor(UiTheme.Current.negative);
        delBtn.style.color           = new StyleColor(UiTheme.Current.textMain);
    }

    static void StyleButton(Button btn, Color bg, Color fg)
    {
        btn.style.backgroundColor = new StyleColor(bg);
        btn.style.color           = new StyleColor(fg);
        btn.style.borderTopWidth = btn.style.borderBottomWidth =
        btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
        btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
        btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 5;
        btn.style.paddingLeft = btn.style.paddingRight = 10;
        btn.style.paddingTop  = btn.style.paddingBottom = 5;
    }
}
