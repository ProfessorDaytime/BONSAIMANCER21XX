using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The main-menu front end, built in code as a UI Toolkit panel (PLAN "Release Prep" R1).
///
/// <para>Replaces the uGUI Canvas the scene shipped with since the project's start. Built the
/// same way as the in-game panels so it reads <see cref="UiTheme"/> and repaints on
/// <c>UiTheme.OnThemeChanged</c> — which is what makes an equipped UI-theme cosmetic visible
/// from the first frame instead of only once you're in-game.</para>
///
/// <para>Views are mutually exclusive siblings of one root: Menu → Save Select → Credits.
/// The save picker lives here rather than in the game scene because
/// <see cref="SaveSelectPanel"/> needs only <see cref="SaveManager"/>, which is static file IO.
/// The game scene is therefore only ever entered once the player has decided what they are
/// loading — which is what allows a loading screen to exist at all.</para>
/// </summary>
public class MainMenuUI
{
    // ── Views ─────────────────────────────────────────────────────────────────

    enum View { Menu, SaveSelect, Credits, Settings, Greenhouse }

    VisualElement root;
    VisualElement menuView, saveSelectView, creditsView, settingsView;
    VisualElement saveCardContainer;

    readonly MonoBehaviour host;
    readonly SaveSelectPanel savePanel = new SaveSelectPanel();
    readonly SettingsPanel   settings  = new SettingsPanel();

    View current = View.Menu;
    bool  launching;

    public MainMenuUI(MonoBehaviour host) => this.host = host;

    // ── Build ─────────────────────────────────────────────────────────────────

    public void Build(VisualElement documentRoot)
    {
        root = documentRoot;

        // Anchor the root so full-screen views measure against the real panel bounds rather than
        // collapsing to content height — the same fix buttonClicker needs for its overlays.
        root.style.position = Position.Absolute;
        root.style.top = root.style.left = root.style.right = root.style.bottom = 0;

        savePanel.OnLoadRequested = OnLoadSlot;
        savePanel.OnSlotDeleted   = remaining =>
        {
            // Last save deleted — the picker has nothing left to show, so drop back to the menu
            // where Play now means "new tree".
            if (remaining == 0) Show(View.Menu);
        };

        menuView       = BuildMenuView();
        saveSelectView = BuildSaveSelectView();
        creditsView    = BuildCreditsView();
        settingsView   = BuildSettingsView();

        root.Add(menuView);
        root.Add(saveSelectView);
        root.Add(creditsView);
        root.Add(settingsView);

        UiTheme.OnThemeChanged += Repaint;
        Show(View.Menu);
    }

    public void Dispose()
    {
        UiTheme.OnThemeChanged -= Repaint;
        savePanel.ReleaseThumbnails();
    }

    /// <summary>Rebuilds every view from the current theme. Cheap — these are small panels and
    /// this only runs when the player changes theme.</summary>
    void Repaint()
    {
        if (root == null) return;

        var wasShowing = current;
        root.Clear();

        menuView       = BuildMenuView();
        saveSelectView = BuildSaveSelectView();
        creditsView    = BuildCreditsView();
        settingsView   = BuildSettingsView();

        root.Add(menuView);
        root.Add(saveSelectView);
        root.Add(creditsView);
        root.Add(settingsView);

        Show(wasShowing);
    }

    // ── Menu view ─────────────────────────────────────────────────────────────

    VisualElement BuildMenuView()
    {
        var th = UiTheme.Current;

        var view = Column();
        view.style.justifyContent = Justify.Center;
        view.style.alignItems     = Align.FlexStart;
        view.style.paddingLeft    = 72;

        var title = new Label { text = "BONSAIMANCER" };
        title.style.fontSize = 46;
        title.style.color    = new StyleColor(th.accent);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 2;
        view.Add(title);

        var tagline = new Label { text = "Grow with time, not power." };
        tagline.style.fontSize = 14;
        tagline.style.color    = new StyleColor(th.textSub);
        tagline.style.marginBottom = 34;
        view.Add(tagline);

        // Play means "continue" when saves exist and "new tree" when they don't, so the player
        // never meets an empty picker.
        bool hasSaves = SaveManager.HasAnySave();
        view.Add(MenuButton(hasSaves ? "PLAY" : "NEW TREE", primary: true,
                            onClick: () => { if (hasSaves) Show(View.SaveSelect); else LaunchNewTree(); }));

        if (hasSaves)
            view.Add(MenuButton("NEW TREE", primary: false, onClick: LaunchNewTree));

        view.Add(MenuButton("GREENHOUSE", primary: false, onClick: OpenGreenhouse));
        view.Add(MenuButton("SETTINGS", primary: false, onClick: () => Show(View.Settings)));
        view.Add(MenuButton("CREDITS",  primary: false, onClick: () => Show(View.Credits)));
        view.Add(MenuButton("QUIT",     primary: false, onClick: Application.Quit));

        view.Add(VersionLabel(th));
        return view;
    }

    /// <summary>Build and version, bottom-left. Needed so a bug report identifies which build it
    /// came from (PLAN R0).</summary>
    static Label VersionLabel(UiTheme th)
    {
        var v = new Label { text = $"v{Application.version}" };
        v.style.fontSize  = 11;
        v.style.color     = new StyleColor(th.locked);
        v.style.marginTop = 30;
        return v;
    }

    // ── Save select view ──────────────────────────────────────────────────────

    VisualElement BuildSaveSelectView()
    {
        var th = UiTheme.Current;

        var view = Column();
        view.style.alignItems     = Align.Center;
        view.style.justifyContent = Justify.Center;

        var panel = new VisualElement();
        panel.style.width     = new StyleLength(new Length(90f, LengthUnit.Percent));
        panel.style.maxWidth  = 560;
        panel.style.maxHeight = new StyleLength(new Length(80f, LengthUnit.Percent));
        panel.style.backgroundColor = new StyleColor(th.panelBg);
        panel.style.borderTopWidth = panel.style.borderBottomWidth =
        panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
        panel.style.borderTopColor = panel.style.borderBottomColor =
        panel.style.borderLeftColor = panel.style.borderRightColor = new StyleColor(th.edge);
        panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
        panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 10;
        panel.style.paddingTop = panel.style.paddingBottom = 20;
        panel.style.paddingLeft = panel.style.paddingRight = 20;
        view.Add(panel);

        panel.Add(Header("SELECT A SAVE", th));

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        panel.Add(scroll);

        saveCardContainer = new VisualElement();
        scroll.Add(saveCardContainer);

        var row = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginTop = 14 }
        };
        row.Add(SmallButton("◂ BACK", th.cardBg, th.textSub, () => Show(View.Menu)));
        row.Add(SmallButton("+ NEW TREE", th.accent, th.ink, LaunchNewTree));
        panel.Add(row);

        return view;
    }

    // ── Credits view ──────────────────────────────────────────────────────────

    VisualElement BuildCreditsView()
    {
        var th = UiTheme.Current;

        var view = Column();
        view.style.alignItems     = Align.Center;
        view.style.justifyContent = Justify.Center;

        var panel = new VisualElement();
        panel.style.width    = new StyleLength(new Length(90f, LengthUnit.Percent));
        panel.style.maxWidth = 520;
        panel.style.backgroundColor = new StyleColor(th.panelBg);
        panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
        panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 10;
        panel.style.paddingTop = panel.style.paddingBottom = 24;
        panel.style.paddingLeft = panel.style.paddingRight = 28;
        view.Add(panel);

        panel.Add(Header("CREDITS", th));

        // Company name is read from the player settings rather than hardcoded, so it tracks
        // whatever PLAN R0 settled on.
        panel.Add(Body($"Developed by {Application.companyName}", th, bold: true));
        panel.Add(Body("Design, code, art and music — Seth", th));
        panel.Add(Spacer(14));
        panel.Add(Body("Built with Unity. Bonsai horticulture reference drawn from published " +
                       "works by Peter Chan, Werner M. Busch and others.", th));
        panel.Add(Spacer(14));
        panel.Add(Body("Third-party assets and fonts are credited in ATTRIBUTIONS.txt " +
                       "alongside the game executable.", th, dim: true));
        panel.Add(Spacer(20));
        panel.Add(SmallButton("◂ BACK", th.cardBg, th.textSub, () => Show(View.Menu)));

        return view;
    }

    // ── Settings view ─────────────────────────────────────────────────────────

    /// <summary>Hosts the shared <see cref="SettingsPanel"/> as a full-screen menu view — the same
    /// panel the pause menu opens as an overlay (PLAN R2).</summary>
    VisualElement BuildSettingsView()
    {
        var view = Column();
        settings.OnClose = () => Show(View.Menu);
        settings.Build(view, withScrim: false);
        return view;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    void Show(View v)
    {
        current = v;
        if (menuView       != null) menuView.style.display       = v == View.Menu       ? DisplayStyle.Flex : DisplayStyle.None;
        if (saveSelectView != null) saveSelectView.style.display = v == View.SaveSelect ? DisplayStyle.Flex : DisplayStyle.None;
        if (creditsView    != null) creditsView.style.display    = v == View.Credits    ? DisplayStyle.Flex : DisplayStyle.None;
        if (settingsView   != null) settingsView.style.display   = v == View.Settings   ? DisplayStyle.Flex : DisplayStyle.None;

        if (v == View.SaveSelect) savePanel.Build(saveCardContainer);

        // Re-read every control from GameSettings on open. The parent view governs actual
        // visibility, so this is just the refresh.
        if (v == View.Settings) settings.SetVisible(true);
    }

    /// <summary>The greenhouse of saved trees (2026-10-29). It hides the menu and runs its own HUD
    /// and Esc handling; its BACK reloads this scene.</summary>
    void OpenGreenhouse()
    {
        if (launching || current == View.Greenhouse) return;
        Show(View.Greenhouse);
        Greenhouse.Open(root, OnLoadSlot);
    }

    /// <summary>Esc backs out of a sub-view to the menu. Called from the host's Update.</summary>
    public void OnEscape()
    {
        if (launching || current == View.Greenhouse) return;
        if (current != View.Menu) Show(View.Menu);
    }

    // ── Launch ────────────────────────────────────────────────────────────────

    void OnLoadSlot(string slotId)
    {
        if (launching) return;

        // Hand the decision across the scene load. ActiveSlotId already persists through
        // PlayerPrefs; PendingLaunch tells GameManager to obey it rather than deciding for itself.
        SaveManager.ActiveSlotId = slotId;
        SaveManager.PendingLaunch = PendingLaunchAction.LoadSlot;
        Launch("Waking the tree…");
    }

    void LaunchNewTree()
    {
        if (launching) return;

        SaveManager.ActiveSlotId  = null;
        SaveManager.PendingLaunch = PendingLaunchAction.NewTree;
        Launch("Preparing the bench…");
    }

    void Launch(string flavour)
    {
        launching = true;
        host.StartCoroutine(LaunchRoutine(flavour));
    }

    IEnumerator LaunchRoutine(string flavour)
    {
        // Hand the menu music over rather than destroying the AudioManager outright, which is what
        // the old MainMenu.StartGame did — it cut the track mid-note on the way into the game.
        yield return host.StartCoroutine(FadeOutMenuMusic(0.6f));

        // The menu's AudioManager MUST be gone before Game001 loads. AudioManager is a
        // DontDestroyOnLoad singleton, so if the menu's copy survives, Game001's own copy finds it
        // in Awake and runs Destroy(gameObject) — and in Game001 that GameObject also carries
        // GameManager, PlayModeManager, ProgressionManager, MusicManager and five more managers.
        // GameManager.Update then never runs, so the clock and all growth stop while the UI keeps
        // working. The original menu destroyed it for exactly this reason; R1 dropped that call to
        // get a fade instead of a hard cut, and this restores it after the fade. (Found in the
        // first play-test, 2026-10-13.)
        if (AudioManager.Instance != null) AudioManager.Instance.DestroyAudioManager();

        var loading = new LoadingScreen();
        yield return host.StartCoroutine(loading.Run(root, "Game001", flavour));
    }

    static IEnumerator FadeOutMenuMusic(float seconds)
    {
        var src = AudioManager.Instance != null ? AudioManager.Instance.musicSource : null;
        if (src == null) yield break;

        float start = src.volume;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(start, 0f, t / seconds);
            yield return null;
        }
        src.volume = 0f;
        src.Stop();
    }

    // ── Small builders ────────────────────────────────────────────────────────

    static VisualElement Column()
    {
        var v = new VisualElement();
        v.style.position = Position.Absolute;
        v.style.top = v.style.left = v.style.right = v.style.bottom = 0;
        v.style.flexDirection = FlexDirection.Column;
        return v;
    }

    Button MenuButton(string text, bool primary, System.Action onClick)
    {
        var th = UiTheme.Current;

        var b = new Button { text = text };
        b.style.width     = 230;
        b.style.height    = 42;
        b.style.fontSize  = primary ? 16 : 14;
        b.style.marginBottom = 8;
        b.style.unityTextAlign = TextAnchor.MiddleLeft;
        b.style.paddingLeft = 18;
        b.style.backgroundColor = new StyleColor(primary ? th.accent : th.cardBg);
        b.style.color           = new StyleColor(primary ? th.ink    : th.textMain);
        b.style.unityFontStyleAndWeight = primary ? FontStyle.Bold : FontStyle.Normal;
        b.style.borderTopWidth = b.style.borderBottomWidth =
        b.style.borderLeftWidth = b.style.borderRightWidth = 0;
        b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
        b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 6;
        b.RegisterCallback<ClickEvent>(_ => onClick());
        return b;
    }

    static Button SmallButton(string text, Color bg, Color fg, System.Action onClick)
    {
        var b = new Button { text = text };
        b.style.height   = 34;
        b.style.fontSize = 12;
        b.style.paddingLeft = b.style.paddingRight = 16;
        b.style.backgroundColor = new StyleColor(bg);
        b.style.color           = new StyleColor(fg);
        b.style.borderTopWidth = b.style.borderBottomWidth =
        b.style.borderLeftWidth = b.style.borderRightWidth = 0;
        b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
        b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 5;
        b.RegisterCallback<ClickEvent>(_ => onClick());
        return b;
    }

    static Label Header(string text, UiTheme th)
    {
        var l = new Label { text = text };
        l.style.fontSize = 20;
        l.style.color    = new StyleColor(th.accent);
        l.style.unityFontStyleAndWeight = FontStyle.Bold;
        l.style.marginBottom = 16;
        return l;
    }

    static Label Body(string text, UiTheme th, bool bold = false, bool dim = false)
    {
        var l = new Label { text = text };
        l.style.fontSize  = 12;
        l.style.color     = new StyleColor(dim ? th.locked : th.textMain);
        l.style.whiteSpace = WhiteSpace.Normal;
        if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
        return l;
    }

    static VisualElement Spacer(float h) => new VisualElement { style = { height = h } };
}
