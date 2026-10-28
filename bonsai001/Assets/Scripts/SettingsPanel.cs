using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The single settings-and-game screen (PLAN "Release Prep" R2, unified 2026-10-13).
///
/// <para>One panel, three contexts:</para>
/// <list type="bullet">
///   <item><b>Menu</b> — opened from the main menu. Player preferences only; footer says BACK.</item>
///   <item><b>Pause</b> — what Esc opens in game. Adds the Game tab (save / load / quit), the play
///         modes and speed tabs, and the developer tabs in a development build. Footer says
///         RESUME (ESC).</item>
///   <item><b>PreStart</b> — shown after a species is picked, before the first season. Opens on
///         Play Modes; footer says START.</item>
/// </list>
///
/// <para>Tabs come from two places. <b>Built-in</b> tabs (Display, Audio, Gameplay, Controls,
/// Access) are drawn here from <see cref="GameSettings"/>. <b>Adopted</b> tabs are existing UI the
/// host hands over with <see cref="AdoptTab"/> — the old pause menu's controls, the calendar's
/// Modes and Speed tabs, the dev tabs. Adopting moves the element into this panel rather than
/// rebuilding it, so every callback the host registered on it keeps working untouched. That is
/// what makes absorbing several hundred lines of UXML-bound UI a low-risk change.</para>
/// </summary>
public class SettingsPanel
{
    public enum Context { Menu, Pause, PreStart }

    // ── Host wiring ───────────────────────────────────────────────────────────

    /// <summary>The footer's primary button (BACK / RESUME / START). The host decides what it means
    /// for the current context.</summary>
    public Action OnClose;

    public Context CurrentContext { get; private set; } = Context.Menu;

    public bool IsOpen => root != null && root.style.display == DisplayStyle.Flex;

    // ── Tabs ──────────────────────────────────────────────────────────────────

    class TabDef
    {
        public string id, label;
        public Action<UiTheme> build;          // built-in: draws into `content`
        public VisualElement adopted;           // adopted: moved into `content` on show
        public Action onShow;
        public bool devOnly;
        public Context[] contexts;              // null = every context
        public readonly List<VisualElement> attachments = new List<VisualElement>();
    }

    /// <summary>Display order. Ids not listed here are appended in the order they were added.</summary>
    static readonly string[] Order =
    {
        "game", "modes", "speed", "display", "audio", "gameplay", "controls", "access",
        "growth", "ishitsuki", "debug",
    };

    readonly Dictionary<string, TabDef> tabs = new Dictionary<string, TabDef>();
    readonly List<string> addOrder = new List<string>();

    VisualElement root, nav, content;
    Button primaryButton, resetButton;
    string currentTab = "display";

    public SettingsPanel()
    {
        AddBuiltIn("display",  "Display",       BuildDisplay);
        AddBuiltIn("audio",    "Audio",         BuildAudio);
        AddBuiltIn("gameplay", "Gameplay",      BuildGameplay);
        AddBuiltIn("controls", "Controls",      BuildControls);
        AddBuiltIn("access",   "Accessibility", BuildAccessibility);
    }

    void AddBuiltIn(string id, string label, Action<UiTheme> build)
    {
        tabs[id] = new TabDef { id = id, label = label, build = build };
        addOrder.Add(id);
    }

    /// <summary>
    /// Takes ownership of an existing element as a tab. The element is moved into this panel when
    /// the tab is shown and detached when another tab is, so its own callbacks stay intact.
    /// </summary>
    /// <param name="onShow">Refresh hook, run every time the tab is shown.</param>
    /// <param name="contexts">Contexts the tab appears in; null for all.</param>
    public void AdoptTab(string id, string label, VisualElement element, Action onShow = null,
                         bool devOnly = false, params Context[] contexts)
    {
        if (element == null) return;
        tabs[id] = new TabDef
        {
            id = id, label = label, adopted = element, onShow = onShow, devOnly = devOnly,
            contexts = contexts != null && contexts.Length > 0 ? contexts : null,
        };
        if (!addOrder.Contains(id)) addOrder.Add(id);
    }

    /// <summary>Appends an existing element to the bottom of a tab (built-in or adopted) every time
    /// that tab is shown.</summary>
    public void AttachToTab(string id, VisualElement element)
    {
        if (element != null && tabs.TryGetValue(id, out var t)) t.attachments.Add(element);
    }

    /// <summary>The adopted element for a tab, so the host can add context-specific extras to it
    /// (the pre-start Quick Start section goes into Play Modes this way).</summary>
    public VisualElement AdoptedElement(string id) =>
        tabs.TryGetValue(id, out var t) ? t.adopted : null;

    // ── Build / show ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the panel inside <paramref name="parent"/>. Pass <paramref name="withScrim"/> true
    /// when overlaying live gameplay, false when it is already a full-screen menu view.
    /// </summary>
    public VisualElement Build(VisualElement parent, bool withScrim)
    {
        var th = UiTheme.Current;

        root = new VisualElement();
        root.style.position = Position.Absolute;
        root.style.top = root.style.left = root.style.right = root.style.bottom = 0;
        root.style.alignItems     = Align.Center;
        root.style.justifyContent = Justify.Center;
        if (withScrim) root.style.backgroundColor = new StyleColor(th.scrim);
        parent.Add(root);

        var panel = new VisualElement();
        panel.style.width     = new StyleLength(new Length(94f, LengthUnit.Percent));
        panel.style.maxWidth  = 900;
        panel.style.height    = new StyleLength(new Length(86f, LengthUnit.Percent));
        panel.style.backgroundColor = new StyleColor(th.panelBg);
        panel.style.borderTopWidth = panel.style.borderBottomWidth =
        panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
        panel.style.borderTopColor = panel.style.borderBottomColor =
        panel.style.borderLeftColor = panel.style.borderRightColor = new StyleColor(th.edge);
        panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
        panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 10;
        panel.style.paddingTop = panel.style.paddingBottom = 18;
        panel.style.paddingLeft = panel.style.paddingRight = 20;
        root.Add(panel);

        var title = new Label { text = "SETTINGS" };
        title.style.fontSize = 22;
        title.style.color    = new StyleColor(th.accent);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.marginBottom = 14;
        panel.Add(title);

        // ── Body: nav column + content ──
        var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 0 } };
        panel.Add(body);

        nav = new VisualElement();
        nav.style.width = 150;
        nav.style.flexShrink = 0;
        nav.style.marginRight = 16;
        body.Add(nav);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        body.Add(scroll);
        content = new VisualElement();
        content.style.paddingRight = 6;
        scroll.Add(content);

        // ── Footer ──
        var footer = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginTop = 14 }
        };
        resetButton = Btn("RESET TO DEFAULTS", th.cardBg, th.textSub, () =>
        {
            GameSettings.ResetToDefaults();
            ShowTab(currentTab);
        });
        footer.Add(resetButton);
        primaryButton = Btn("◂ BACK", th.accent, th.ink, () => OnClose?.Invoke());
        primaryButton.style.minWidth = 180;
        footer.Add(primaryButton);
        panel.Add(footer);

        ApplyContext(CurrentContext);
        return root;
    }

    /// <summary>Opens in a context, optionally on a specific tab.</summary>
    public void Open(Context ctx, string tabId = null)
    {
        if (root == null) return;
        CurrentContext = ctx;
        ApplyContext(ctx);
        root.style.display = DisplayStyle.Flex;

        string want = tabId ?? currentTab;
        if (!Visible(want)) want = FirstVisible();
        ShowTab(want);
    }

    /// <summary>Show/hide without changing context. Showing re-reads every control, since another
    /// host may have changed a value in the meantime.</summary>
    public void SetVisible(bool v)
    {
        if (root == null) return;
        if (v) Open(CurrentContext);
        else
        {
            DetachAdopted();
            root.style.display = DisplayStyle.None;
        }
    }

    public void Remove()
    {
        DetachAdopted();
        root?.RemoveFromHierarchy();
        root = null;
    }

    void ApplyContext(Context ctx)
    {
        if (primaryButton != null)
            primaryButton.text = ctx switch
            {
                Context.Pause    => "RESUME  (ESC)",
                Context.PreStart => "START  ▸",
                _                => "◂ BACK",
            };

        // Resetting every preference on the way into a new game would be a surprising thing to
        // offer next to START.
        if (resetButton != null)
            resetButton.style.display = ctx == Context.PreStart ? DisplayStyle.None : DisplayStyle.Flex;

        RebuildNav();
    }

    bool Visible(string id)
    {
        if (id == null || !tabs.TryGetValue(id, out var t)) return false;
        if (t.devOnly && !BonsaiDebug.Enabled) return false;
        if (t.contexts != null && Array.IndexOf(t.contexts, CurrentContext) < 0) return false;
        return true;
    }

    List<string> OrderedIds()
    {
        var ids = new List<string>();
        foreach (var id in Order) if (tabs.ContainsKey(id)) ids.Add(id);
        foreach (var id in addOrder) if (!ids.Contains(id)) ids.Add(id);
        return ids;
    }

    string FirstVisible()
    {
        foreach (var id in OrderedIds()) if (Visible(id)) return id;
        return "display";
    }

    void RebuildNav()
    {
        if (nav == null) return;
        nav.Clear();
        var th = UiTheme.Current;

        bool devHeaderAdded = false;
        foreach (var id in OrderedIds())
        {
            if (!Visible(id)) continue;
            var t = tabs[id];

            if (t.devOnly && !devHeaderAdded)
            {
                var h = new Label { text = "DEVELOPER" };
                h.style.fontSize  = 9;
                h.style.color     = new StyleColor(th.locked);
                h.style.marginTop = 12;
                h.style.marginBottom = 4;
                h.style.paddingLeft = 10;
                nav.Add(h);
                devHeaderAdded = true;
            }

            var b = new Button { text = t.label.ToUpperInvariant() };
            b.name = "settings-tab-" + id;
            b.style.height   = 32;
            b.style.fontSize = 11;
            b.style.marginBottom = 3;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingLeft = 12;
            b.style.borderTopWidth = b.style.borderBottomWidth =
            b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
            b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 4;
            b.RegisterCallback<ClickEvent>(_ => ShowTab(id));
            nav.Add(b);
        }
        PaintNav();
    }

    void PaintNav()
    {
        if (nav == null) return;
        var th = UiTheme.Current;
        foreach (var child in nav.Children())
        {
            if (!(child is Button b)) continue;
            bool active = b.name == "settings-tab-" + currentTab;
            b.style.backgroundColor = new StyleColor(active ? th.accent : th.cardBg);
            b.style.color           = new StyleColor(active ? th.ink    : th.textSub);
            b.style.unityFontStyleAndWeight =
                active ? new StyleEnum<FontStyle>(FontStyle.Bold) : new StyleEnum<FontStyle>(FontStyle.Normal);
        }
    }

    void ShowTab(string id)
    {
        if (!tabs.TryGetValue(id, out var t)) return;
        currentTab = id;
        PaintNav();

        DetachAdopted();
        content.Clear();

        if (t.adopted != null)
        {
            content.Add(t.adopted);
            // The old hosts hid these with display:none when their own tab wasn't selected.
            t.adopted.style.display = DisplayStyle.Flex;
        }
        else
            t.build?.Invoke(UiTheme.Current);

        foreach (var a in t.attachments)
        {
            content.Add(a);
            a.style.display = DisplayStyle.Flex;
        }

        t.onShow?.Invoke();
    }

    /// <summary>Removes adopted elements from the content area without destroying them.</summary>
    void DetachAdopted()
    {
        if (content == null) return;
        foreach (var t in tabs.Values)
        {
            if (t.adopted != null && t.adopted.parent == content) t.adopted.RemoveFromHierarchy();
            foreach (var a in t.attachments)
                if (a.parent == content) a.RemoveFromHierarchy();
        }
    }

    // ── Display ───────────────────────────────────────────────────────────────

    void BuildDisplay(UiTheme th)
    {
        content.Add(Header("Display", th));

        content.Add(Dropdown("Window mode",
            new List<string> { "Fullscreen", "Borderless", "Windowed" },
            (int)GameSettings.Mode,
            i => GameSettings.Mode = (GameSettings.DisplayMode)i, th));

        var resList = GameSettings.AvailableResolutions();
        var resNames = new List<string>();
        foreach (var r in resList) resNames.Add($"{r.x} × {r.y}");
        int resIndex = resList.IndexOf(GameSettings.Resolution);
        if (resIndex < 0)
        {
            // Current resolution isn't one the display enumerates (common in the editor) — show it
            // as an extra entry rather than silently selecting something else.
            resNames.Insert(0, $"{GameSettings.Resolution.x} × {GameSettings.Resolution.y} (current)");
            resList.Insert(0, GameSettings.Resolution);
            resIndex = 0;
        }
        content.Add(Dropdown("Resolution", resNames, resIndex,
            i => GameSettings.Resolution = resList[i], th));

        content.Add(Check("V-Sync", GameSettings.VSync, v =>
        {
            GameSettings.VSync = v;
            ShowTab("display");   // the frame cap below becomes meaningless, so redraw it
        }, th));

        var caps = new List<string> { "Unlimited", "30", "60", "120", "144", "165", "240" };
        var capValues = new List<int> { 0, 30, 60, 120, 144, 165, 240 };
        int capIdx = capValues.IndexOf(GameSettings.FpsCap);
        var capRow = Dropdown("Frame rate cap", caps, capIdx < 0 ? 0 : capIdx,
            i => GameSettings.FpsCap = capValues[i], th);
        if (GameSettings.VSync)
        {
            // Unity ignores targetFrameRate while vSyncCount > 0. Disable rather than let the
            // player set a value that does nothing.
            capRow.SetEnabled(false);
            capRow.style.opacity = 0.45f;
            content.Add(capRow);
            content.Add(Note("Frame cap is controlled by V-Sync while it is on.", th));
        }
        else content.Add(capRow);

        content.Add(Check("Anti-aliasing", GameSettings.AntiAliasing, v => GameSettings.AntiAliasing = v, th));

        var qNames = new List<string>(QualitySettings.names);
        int q = GameSettings.QualityLevel;
        if (q < 0)
        {
            qNames.Insert(0, "Project default");
            content.Add(Dropdown("Quality preset", qNames, 0,
                i => GameSettings.QualityLevel = i - 1, th));
        }
        else
            content.Add(Dropdown("Quality preset", qNames, Mathf.Clamp(q, 0, qNames.Count - 1),
                i => GameSettings.QualityLevel = i, th));

        content.Add(Note("UI scale is not available yet — it needs its own panel settings so the " +
                         "menu and the in-game HUD can scale independently.", th));
    }

    // ── Audio ─────────────────────────────────────────────────────────────────

    void BuildAudio(UiTheme th)
    {
        content.Add(Header("Audio", th));

        content.Add(Check("Mute all", GameSettings.MuteAll, v =>
        {
            GameSettings.MuteAll = v;
            ShowTab("audio");
        }, th));

        var sliders = new VisualElement();
        sliders.SetEnabled(!GameSettings.MuteAll);
        sliders.style.opacity = GameSettings.MuteAll ? 0.45f : 1f;

        sliders.Add(Slide("Master",  GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, th));
        sliders.Add(Slide("Music",   GameSettings.MusicVolume,  v => GameSettings.MusicVolume  = v, th));
        sliders.Add(Slide("Effects", GameSettings.SfxVolume,    v => GameSettings.SfxVolume    = v, th));
        content.Add(sliders);

        content.Add(Note("There is no separate ambient channel yet — nothing in the game plays an " +
                         "ambient bed. A dedicated mixer, and ambient audio to put through it, are " +
                         "still to come.", th));
    }

    // ── Gameplay ──────────────────────────────────────────────────────────────

    void BuildGameplay(UiTheme th)
    {
        content.Add(Header("Gameplay", th));

        // No "default speed" here: each play mode carries its own speed (Play Modes tab), and
        // PlayModeManager applies it continuously — a second control would just be overridden.

        content.Add(Check("Fast winter", GameSettings.QuickWinter, v => GameSettings.QuickWinter = v, th));
        content.Add(Note("Runs the dormant months at double speed.", th));

        content.Add(Check("Autosave each season", GameSettings.Autosave, v => GameSettings.Autosave = v, th));

        content.Add(Check("First-use tips", GameSettings.Tooltips, v => GameSettings.Tooltips = v, th));
        var replay = Btn("REPLAY FIRST-USE TIPS", th.cardBg, th.textSub, () =>
        {
            ButtonClicker.ResetShownTooltips();
            GameSettings.Tooltips = true;
            ShowTab("gameplay");
        });
        replay.style.marginBottom = 12;
        content.Add(replay);

        content.Add(Dropdown("Date display",
            new List<string> { "November 7, 2027", "2027-11-07" },
            (int)GameSettings.DateDisplay,
            i => GameSettings.DateDisplay = (GameSettings.DateFormat)i, th));

        // Career/Sandbox is owned by ProgressionManager and persisted in profile.json. Exposed
        // here by reading and writing that, rather than adding a second source of truth.
        var pm = ProgressionManager.Instance;
        if (pm != null)
        {
            content.Add(Dropdown("Mode",
                new List<string> { "Career", "Sandbox" },
                pm.CurrentMode == GameMode.Career ? 0 : 1,
                i => pm.SetMode(i == 0 ? GameMode.Career : GameMode.Sandbox), th));
            content.Add(Note("Career gates tools behind progression; Sandbox unlocks everything.", th));
        }

        content.Add(Note("UI themes, backgrounds, music and decorations are unlockable cosmetics — " +
                         "they live in the ⚙ customise menu, not here.", th));
    }

    // ── Controls ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A read-only reference sheet. Full rebinding needs an Input System actions asset, and the
    /// bindings are currently hardcoded <c>Keyboard.current</c> reads scattered across
    /// ToolManager, buttonClicker and CameraOrbit — a separate job that does not block a test
    /// build. The host attaches the tool-selection-radius slider below the sheet.
    /// </summary>
    void BuildControls(UiTheme th)
    {
        content.Add(Header("Controls", th));
        content.Add(Note("Click a key to change it, then press the new key. Esc cancels. Taking a " +
                         "key another action uses swaps the two.", th));

        foreach (var a in KeyBindings.All)
            content.Add(RebindRow(a, th));

        content.Add(Header("Fixed", th));
        AddBind("Left mouse",         "Use the selected tool", th);
        AddBind("Right mouse drag",   "Pull weeds", th);
        AddBind("Middle / left drag", "Orbit the camera", th);
        AddBind("Mouse wheel",        "Zoom", th);
        AddBind("Esc",                "Settings and pause, or back out of a menu", th);

        if (BonsaiDebug.Enabled)
            AddBind("F1", "Show the game-state readout (development builds only)", th);
    }

    /// <summary>
    /// One rebindable action: its name, and a button showing the key. Clicking the button waits
    /// for the next key press (polled each frame on the panel's scheduler, which runs after the
    /// scripts' Update — so the Esc handlers have already seen <see cref="KeyBindings.Capturing"/>
    /// and left Esc alone).
    /// </summary>
    VisualElement RebindRow(BindAction a, UiTheme th)
    {
        var row = Row(KeyBindings.Describe(a), th, out var slot);

        var btn = new Button { text = KeyBindings.Display(a) };
        btn.style.minWidth = 140;
        btn.style.height   = 26;
        btn.style.fontSize = 11;
        btn.style.backgroundColor = new StyleColor(th.cardBg);
        btn.style.color           = new StyleColor(th.accent);
        btn.style.borderTopWidth = btn.style.borderBottomWidth =
        btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
        btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
        btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 4;

        IVisualElementScheduledItem poll = null;
        void Stop()
        {
            poll?.Pause();
            KeyBindings.Capturing = false;
        }

        btn.RegisterCallback<ClickEvent>(_ =>
        {
            if (KeyBindings.Capturing) return;   // one capture at a time
            KeyBindings.Capturing = true;
            btn.text = "Press a key…";
            bool firstFrame = true;
            poll = btn.schedule.Execute(() =>
            {
                if (firstFrame) { firstFrame = false; return; }   // skip the frame of the click
                var key = KeyBindings.KeyPressedThisFrame();
                if (key == UnityEngine.InputSystem.Key.None) return;

                Stop();
                if (key != UnityEngine.InputSystem.Key.Escape)
                    KeyBindings.Set(a, key);
                ShowTab(currentTab);   // a swap may have changed another row too
            }).Every(0);
        });
        btn.RegisterCallback<DetachFromPanelEvent>(_ => { if (poll != null && poll.isActive) Stop(); });

        slot.Add(btn);
        return row;
    }

    void AddBind(string key, string what, UiTheme th)
    {
        var row = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 5 }
        };

        var k = new Label { text = key };
        k.style.fontSize = 12;
        k.style.color    = new StyleColor(th.accent);
        k.style.unityFontStyleAndWeight = FontStyle.Bold;
        k.style.minWidth = 150;
        row.Add(k);

        var v = new Label { text = what };
        v.style.fontSize  = 12;
        v.style.color     = new StyleColor(th.textMain);
        v.style.flexGrow  = 1;
        v.style.whiteSpace = WhiteSpace.Normal;
        row.Add(v);

        content.Add(row);
    }

    // ── Accessibility ─────────────────────────────────────────────────────────

    void BuildAccessibility(UiTheme th)
    {
        content.Add(Header("Accessibility", th));

        content.Add(Dropdown("Colour-blind mode",
            new List<string> { "Off", "Deuteranopia", "Protanopia", "Tritanopia" },
            (int)GameSettings.Colorblind,
            i => GameSettings.Colorblind = (GameSettings.ColorblindMode)i, th));
        content.Add(Note("Repalettes the tool highlights. The defaults use red for trim, gold for " +
                         "wire and green for unwire, which are hard to tell apart with red-green " +
                         "colour blindness — and those are the actions that change your tree most.", th));

        content.Add(Check("Reduced motion", GameSettings.ReducedMotion, v => GameSettings.ReducedMotion = v, th));
        content.Add(Note("Stops the wind sway, the idle camera drift and the rain overlay; leaves drop " +
                         "straight down, and wires snap into place without their spring. Also helps frame rate.", th));

        var sizes = new List<string>();
        int sizeIndex = 2;
        for (int i = 0; i < GameSettings.UiScaleOptions.Length; i++)
        {
            sizes.Add($"{GameSettings.UiScaleOptions[i] * 100f:F0}%");
            if (Mathf.Approximately(GameSettings.UiScaleOptions[i], GameSettings.UiScale)) sizeIndex = i;
        }
        content.Add(Dropdown("Interface size", sizes, sizeIndex,
            i => GameSettings.UiScale = GameSettings.UiScaleOptions[i], th));
        content.Add(Note("Scales every menu, panel and the in-game interface.", th));

        content.Add(Check("High contrast", GameSettings.HighContrast, v =>
        {
            GameSettings.HighContrast = v;
            content.schedule.Execute(() => { RebuildNav(); ShowTab(currentTab); });   // repaint in the new palette
        }, th));
        content.Add(Note("Black panels, white text and a bright accent, overriding the equipped UI theme.", th));

        content.Add(Check("Tool cursors", GameSettings.CustomCursors, v => GameSettings.CustomCursors = v, th));
        content.Add(Note("Shows the tool you're holding as the mouse cursor. Turn off for the " +
                         "standard arrow everywhere.", th));

    }

    // ── Control builders ──────────────────────────────────────────────────────

    static Label Header(string text, UiTheme th)
    {
        var l = new Label { text = text.ToUpperInvariant() };
        l.style.fontSize = 14;
        l.style.color    = new StyleColor(th.accent);
        l.style.unityFontStyleAndWeight = FontStyle.Bold;
        l.style.marginBottom = 12;
        return l;
    }

    static VisualElement Row(string label, UiTheme th, out VisualElement slot)
    {
        var row = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row,
                alignItems = Align.Center,
                justifyContent = Justify.SpaceBetween,
                marginBottom = 9,
            }
        };

        var l = new Label { text = label };
        l.style.fontSize = 12;
        l.style.color    = new StyleColor(th.textMain);
        l.style.flexGrow = 1;
        row.Add(l);

        slot = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        row.Add(slot);
        return row;
    }

    static VisualElement Check(string label, bool value, Action<bool> onChange, UiTheme th)
    {
        var row = Row(label, th, out var slot);
        var t = new Toggle { value = value };
        t.RegisterValueChangedCallback(e => onChange(e.newValue));
        slot.Add(t);
        return row;
    }

    static VisualElement Slide(string label, float value, Action<float> onChange, UiTheme th)
    {
        var row = Row(label, th, out var slot);

        var pct = new Label { text = $"{value * 100f:F0}%" };
        pct.style.fontSize = 11;
        pct.style.color    = new StyleColor(th.textSub);
        pct.style.minWidth = 40;
        pct.style.unityTextAlign = TextAnchor.MiddleRight;

        var s = new Slider(0f, 1f) { value = value };
        s.style.width = 180;
        s.RegisterValueChangedCallback(e =>
        {
            pct.text = $"{e.newValue * 100f:F0}%";
            onChange(e.newValue);
        });

        slot.Add(s);
        slot.Add(pct);
        return row;
    }

    /// <summary>A cycling button rather than a popup: UI Toolkit's <c>DropdownField</c> styling
    /// fights the hand-built theme, and a cycling button always shows the current value.</summary>
    static VisualElement Dropdown(string label, List<string> options, int index,
                                  Action<int> onChange, UiTheme th)
    {
        var row = Row(label, th, out var slot);

        index = Mathf.Clamp(index, 0, Mathf.Max(0, options.Count - 1));
        var btn = new Button { text = options.Count > 0 ? options[index] : "—" };
        btn.style.minWidth = 180;
        btn.style.height   = 26;
        btn.style.fontSize = 11;
        btn.style.backgroundColor = new StyleColor(th.cardBg);
        btn.style.color           = new StyleColor(th.textMain);
        btn.style.borderTopWidth = btn.style.borderBottomWidth =
        btn.style.borderLeftWidth = btn.style.borderRightWidth = 0;
        btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
        btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 4;

        btn.RegisterCallback<ClickEvent>(_ =>
        {
            if (options.Count == 0) return;
            index = (index + 1) % options.Count;
            btn.text = options[index];
            onChange(index);
        });

        slot.Add(btn);
        return row;
    }

    static Label Note(string text, UiTheme th)
    {
        var l = new Label { text = text };
        l.style.fontSize    = 10;
        l.style.color       = new StyleColor(th.locked);
        l.style.whiteSpace  = WhiteSpace.Normal;
        l.style.marginBottom = 12;
        l.style.marginTop   = -4;
        return l;
    }

    static Button Btn(string text, Color bg, Color fg, Action onClick)
    {
        var b = new Button { text = text };
        b.style.height   = 34;
        b.style.fontSize = 12;
        b.style.paddingLeft = b.style.paddingRight = 14;
        b.style.backgroundColor = new StyleColor(bg);
        b.style.color           = new StyleColor(fg);
        b.style.borderTopWidth = b.style.borderBottomWidth =
        b.style.borderLeftWidth = b.style.borderRightWidth = 0;
        b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
        b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 5;
        b.RegisterCallback<ClickEvent>(_ => onClick());
        return b;
    }
}
