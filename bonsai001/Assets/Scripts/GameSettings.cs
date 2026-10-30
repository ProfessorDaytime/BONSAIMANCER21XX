using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Every player-facing setting: the single owner of the <c>set.*</c> PlayerPrefs keys, and the
/// single place that applies them to the running game (PLAN "Release Prep" R2).
///
/// <para>Before this existed the only player options anywhere were an anti-aliasing toggle, three
/// timescale prefs, and <c>FullscreenPrompt</c> — a raw <c>OnGUI</c> box that asked "Play
/// fullscreen?" once at startup. The rest of what looked like settings was the developer pause
/// panel.</para>
///
/// <para><b>Design:</b> values live in PlayerPrefs under a <c>set.</c> prefix, are read through
/// typed properties that cache, and are pushed into the engine by <see cref="ApplyAll"/>. Setting
/// any property applies its own effect immediately and persists it, so the UI never has to
/// remember to do both. <see cref="Install"/> applies everything once at startup via
/// <c>RuntimeInitializeOnLoadMethod</c>, so settings take effect in every scene with no wiring.
/// </para>
/// </summary>
public static class GameSettings
{
    // ── Keys ──────────────────────────────────────────────────────────────────
    // Namespaced from the start. The pre-existing un-namespaced keys (antiAliasing, ts_*,
    // playModes, activeModeIndex, BonsaiActiveSlot, ShownTooltips_v1) are left where they are;
    // antiAliasing is migrated below because this panel now owns it.

    const string K_DISPLAY_MODE  = "set.displayMode";
    const string K_RES_W         = "set.resW";
    const string K_RES_H         = "set.resH";
    const string K_VSYNC         = "set.vsync";
    const string K_FPS_CAP       = "set.fpsCap";
    const string K_AA            = "set.aa";
    const string K_QUALITY       = "set.quality";

    const string K_VOL_MASTER    = "set.volMaster";
    const string K_VOL_MUSIC     = "set.volMusic";
    const string K_VOL_SFX       = "set.volSfx";
    const string K_MUTE_MASTER   = "set.muteMaster";

    const string K_QUICK_WINTER  = "set.quickWinter";
    const string K_AUTOSAVE      = "set.autosave";
    const string K_DATE_FORMAT   = "set.dateFormat";

    const string K_COLORBLIND    = "set.colorblind";
    const string K_REDUCED_MOTION = "set.reducedMotion";
    const string K_CUSTOM_CURSORS = "set.customCursors";
    const string K_UI_SCALE       = "set.uiScale";
    const string K_HIGH_CONTRAST  = "set.highContrast";
    const string K_OUTLINE        = "set.treeOutline";

    // Legacy key this panel took over.
    const string K_LEGACY_AA     = "antiAliasing";

    /// <summary>Raised after any setting changes, so open UI can refresh itself.</summary>
    public static event Action OnChanged;

    // ── Install ───────────────────────────────────────────────────────────────

    static bool installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (installed) return;
        installed = true;

        MigrateLegacyKeys();
        ApplyAll();

        // ApplyAll runs before the first scene exists, so the scene-bound settings (audio sources,
        // WindManager, the idle orbit) have nothing to apply to yet. Re-apply on every scene load,
        // which is also what makes them survive the menu -> game transition.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => ApplySceneBound();
    }

    /// <summary>
    /// Applies every setting to the engine. Safe to call repeatedly. Audio and the per-scene
    /// settings (wind, idle orbit) are re-applied on scene load by <see cref="ApplySceneBound"/>,
    /// because the objects they touch don't exist until a scene is up.
    /// </summary>
    public static void ApplyAll()
    {
        ApplyDisplay();
        ApplyQuality();
        ApplySceneBound();   // applies audio and interface size too
        TreeInteraction.ApplyHighlightPalette(Colorblind);
        UiTheme.SetHighContrast(HighContrast);
        ApplyTreeOutline();
    }

    // ── Tree outline ──────────────────────────────────────────────────────────

    /// <summary>Default for the tree outline: off for now (2026-10-29). Set to 1 to bring the
    /// original outline back for everyone who hasn't chosen otherwise.</summary>
    public const float DefaultTreeOutline = 0f;
    public const float MaxTreeOutline     = 2f;

    /// <summary>Thickness of the dark outline around the bark, as a multiple of the material's
    /// width: 0 = off, 1 = the original look.</summary>
    public static float TreeOutline
    {
        get => PlayerPrefs.GetFloat(K_OUTLINE, DefaultTreeOutline);
        set { PlayerPrefs.SetFloat(K_OUTLINE, Mathf.Clamp(value, 0f, MaxTreeOutline)); Save(); ApplyTreeOutline(); Notify(); }
    }

    /// <summary>The bark shader reads this global, so it reaches every bark material in every scene.</summary>
    public static void ApplyTreeOutline() => Shader.SetGlobalFloat("_BonsaiOutlineScale", TreeOutline);

    static void MigrateLegacyKeys()
    {
        // antiAliasing predates the set.* namespace and was the only real graphics option.
        if (!PlayerPrefs.HasKey(K_AA) && PlayerPrefs.HasKey(K_LEGACY_AA))
        {
            PlayerPrefs.SetInt(K_AA, PlayerPrefs.GetInt(K_LEGACY_AA, 1));
            PlayerPrefs.Save();
            Debug.Log("[Settings] Migrated 'antiAliasing' → 'set.aa'.");
        }
    }

    // ── Display ───────────────────────────────────────────────────────────────

    public enum DisplayMode { Fullscreen = 0, Borderless = 1, Windowed = 2 }

    public static DisplayMode Mode
    {
        get => (DisplayMode)PlayerPrefs.GetInt(K_DISPLAY_MODE, (int)DisplayMode.Borderless);
        set { PlayerPrefs.SetInt(K_DISPLAY_MODE, (int)value); Save(); ApplyDisplay(); }
    }

    /// <summary>Chosen resolution, or the current screen resolution when never set.</summary>
    public static Vector2Int Resolution
    {
        get => new Vector2Int(PlayerPrefs.GetInt(K_RES_W, Screen.width),
                              PlayerPrefs.GetInt(K_RES_H, Screen.height));
        set
        {
            PlayerPrefs.SetInt(K_RES_W, value.x);
            PlayerPrefs.SetInt(K_RES_H, value.y);
            Save();
            ApplyDisplay();
        }
    }

    public static bool VSync
    {
        get => PlayerPrefs.GetInt(K_VSYNC, 1) != 0;
        set { PlayerPrefs.SetInt(K_VSYNC, value ? 1 : 0); Save(); ApplyDisplay(); }
    }

    /// <summary>0 = unlimited. Ignored while <see cref="VSync"/> is on, which is why the UI
    /// disables the control rather than silently doing nothing.</summary>
    public static int FpsCap
    {
        get => PlayerPrefs.GetInt(K_FPS_CAP, 0);
        set { PlayerPrefs.SetInt(K_FPS_CAP, Mathf.Max(0, value)); Save(); ApplyDisplay(); }
    }

    static void ApplyDisplay()
    {
        var res = Resolution;
        var mode = Mode switch
        {
            DisplayMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
            DisplayMode.Windowed   => FullScreenMode.Windowed,
            _                      => FullScreenMode.FullScreenWindow,
        };

        if (res.x > 0 && res.y > 0 &&
            (Screen.width != res.x || Screen.height != res.y || Screen.fullScreenMode != mode))
            Screen.SetResolution(res.x, res.y, mode);
        else
            Screen.fullScreenMode = mode;

        QualitySettings.vSyncCount  = VSync ? 1 : 0;
        // With VSync on, targetFrameRate is ignored by Unity — leave it uncapped so the two
        // settings can't fight each other.
        Application.targetFrameRate = VSync ? -1 : (FpsCap <= 0 ? -1 : FpsCap);
    }

    /// <summary>Distinct width×height pairs the display supports, largest first. Refresh rates are
    /// collapsed — the panel does not offer a refresh-rate choice yet.</summary>
    public static List<Vector2Int> AvailableResolutions()
    {
        var seen = new HashSet<long>();
        var list = new List<Vector2Int>();

        foreach (var r in Screen.resolutions)
        {
            long key = ((long)r.width << 32) | (uint)r.height;
            if (seen.Add(key)) list.Add(new Vector2Int(r.width, r.height));
        }

        // Editor and some headless setups report nothing useful.
        if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));

        list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
        return list;
    }

    // ── Quality ───────────────────────────────────────────────────────────────

    public static bool AntiAliasing
    {
        get => PlayerPrefs.GetInt(K_AA, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(K_AA, value ? 1 : 0);
            PlayerPrefs.SetInt(K_LEGACY_AA, value ? 1 : 0);   // keep the old key in step
            Save();
            ApplyQuality();
        }
    }

    /// <summary>Index into <c>QualitySettings.names</c>. -1 means "leave the project default".</summary>
    public static int QualityLevel
    {
        get => PlayerPrefs.GetInt(K_QUALITY, -1);
        set { PlayerPrefs.SetInt(K_QUALITY, value); Save(); ApplyQuality(); }
    }

    static void ApplyQuality()
    {
        int q = QualityLevel;
        if (q >= 0 && q < QualitySettings.names.Length && q != QualitySettings.GetQualityLevel())
            QualitySettings.SetQualityLevel(q, applyExpensiveChanges: true);

        // After a quality-level change, re-assert AA: quality levels carry their own AA value and
        // would otherwise silently override the player's explicit choice.
        QualitySettings.antiAliasing = AntiAliasing ? 4 : 0;
    }

    // ── Audio ─────────────────────────────────────────────────────────────────
    //
    // There is no AudioMixer in this project and no ambient source: AudioManager owns two bare
    // AudioSources (music, sfx) and MusicManager owns a third for the cosmetic music item. So
    // volume is applied by scaling those sources directly against a captured base volume.
    //
    // A real mixer is the right long-term answer — it is the only way to get per-category effects,
    // ducking, and a master bus that a new source joins automatically. It needs an .mixer asset
    // authored in the editor, which is why it isn't done here. An "Ambient" category is
    // deliberately NOT exposed: nothing in the project plays an ambient bed yet, and a slider that
    // controls nothing is worse than an absent one.

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(K_VOL_MASTER, 1f);
        set { PlayerPrefs.SetFloat(K_VOL_MASTER, Mathf.Clamp01(value)); Save(); ApplyAudio(); }
    }

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(K_VOL_MUSIC, 1f);
        set { PlayerPrefs.SetFloat(K_VOL_MUSIC, Mathf.Clamp01(value)); Save(); ApplyAudio(); }
    }

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(K_VOL_SFX, 1f);
        set { PlayerPrefs.SetFloat(K_VOL_SFX, Mathf.Clamp01(value)); Save(); ApplyAudio(); }
    }

    public static bool MuteAll
    {
        get => PlayerPrefs.GetInt(K_MUTE_MASTER, 0) != 0;
        set { PlayerPrefs.SetInt(K_MUTE_MASTER, value ? 1 : 0); Save(); ApplyAudio(); }
    }

    /// <summary>
    /// Pushes volumes onto the live sources. Called on every change and on scene load.
    /// <para>Note <see cref="AudioManager"/> is <c>DontDestroyOnLoad</c> so it survives the menu →
    /// game transition; its music source is faded by the menu on the way out, which is why the
    /// volume is always recomputed from the setting rather than from whatever is on the source.
    /// </para>
    /// </summary>
    public static void ApplyAudio()
    {
        float master = MuteAll ? 0f : MasterVolume;

        var am = AudioManager.Instance;
        if (am != null)
        {
            if (am.musicSource != null) am.musicSource.volume = master * MusicVolume;
            if (am.sfxSource   != null) am.sfxSource.volume   = master * SfxVolume;
        }

        // MusicManager keeps its own target volume and fades toward it, so hand it the value
        // rather than writing its source directly — writing the source would be overwritten by an
        // in-flight fade.
        if (MusicManager.Instance != null)
            MusicManager.Instance.SetVolume(master * MusicVolume);

        OnChanged?.Invoke();
    }

    // ── Gameplay ──────────────────────────────────────────────────────────────

    // No default-speed setting: each play mode owns its own speed, and PlayModeManager applies it
    // continuously, so a separate global default would simply be overridden. (Removed 2026-10-13
    // when the calendar's Modes tab moved into Settings.)

    public static bool QuickWinter
    {
        get => PlayerPrefs.GetInt(K_QUICK_WINTER, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(K_QUICK_WINTER, value ? 1 : 0);
            Save();
            GameManager.quickWinter = value;
            Notify();
        }
    }

    /// <summary>Autosave at the end of each growing season. On by default — the save system was
    /// built so "the game is never silently lost", and turning this off opts out of that.</summary>
    public static bool Autosave
    {
        get => PlayerPrefs.GetInt(K_AUTOSAVE, 1) != 0;
        set { PlayerPrefs.SetInt(K_AUTOSAVE, value ? 1 : 0); Save(); Notify(); }
    }

    public enum DateFormat { MonthYear = 0, Numeric = 1 }

    public static DateFormat DateDisplay
    {
        get => (DateFormat)PlayerPrefs.GetInt(K_DATE_FORMAT, 0);
        set
        {
            PlayerPrefs.SetInt(K_DATE_FORMAT, (int)value);
            Save();
            GameManager.Instance?.TextCallFunction();   // repaint the HUD date now, not next minute
            Notify();
        }
    }

    /// <summary>First-use tooltips. Backed by the key R5 introduced, so the two agree.</summary>
    public static bool Tooltips
    {
        get => ButtonClicker.tooltipsEnabled;
        set { ButtonClicker.tooltipsEnabled = value; Notify(); }
    }

    // ── Accessibility ─────────────────────────────────────────────────────────

    public enum ColorblindMode { Off = 0, Deuteranopia = 1, Protanopia = 2, Tritanopia = 3 }

    /// <summary>
    /// Repalettes the tool hover highlights. Matters because the defaults are red / gold / green
    /// for trim / wire / unwire — the worst possible triad for the most common form of colour
    /// blindness, on the three most destructive actions in the game.
    /// </summary>
    public static ColorblindMode Colorblind
    {
        get => (ColorblindMode)PlayerPrefs.GetInt(K_COLORBLIND, 0);
        set
        {
            PlayerPrefs.SetInt(K_COLORBLIND, (int)value);
            Save();
            TreeInteraction.ApplyHighlightPalette(value);
            Notify();
        }
    }

    /// <summary>Overrides the equipped UI theme with a black / white / yellow palette.</summary>
    public static bool HighContrast
    {
        get => PlayerPrefs.GetInt(K_HIGH_CONTRAST, 0) != 0;
        set { PlayerPrefs.SetInt(K_HIGH_CONTRAST, value ? 1 : 0); Save(); UiTheme.SetHighContrast(value); Notify(); }
    }

    public static readonly float[] UiScaleOptions = { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };

    /// <summary>Interface size, as a multiplier on every UI panel's scale.</summary>
    public static float UiScale
    {
        get => PlayerPrefs.GetFloat(K_UI_SCALE, 1f);
        set { PlayerPrefs.SetFloat(K_UI_SCALE, value); Save(); ApplyUiScale(); Notify(); }
    }

    // Runtime copies of the PanelSettings in use, keyed by the original, with each copy's own
    // base scale. The originals are assets: changing PanelSettings.scale on an asset at runtime
    // writes into the project in the editor, and would also scale every other scene using it.
    static readonly Dictionary<PanelSettings, PanelSettings> scaledCopyOf = new Dictionary<PanelSettings, PanelSettings>();
    static readonly Dictionary<PanelSettings, float>         copyBaseScale = new Dictionary<PanelSettings, float>();

    /// <summary>
    /// Applies <see cref="UiScale"/> to every UIDocument in the loaded scenes. Documents that share
    /// a PanelSettings keep sharing one copy, so they stay one panel and keep their layering.
    /// At 100% nothing is copied, so the default path is exactly as before.
    /// </summary>
    public static void ApplyUiScale()
    {
        float k = UiScale;
        foreach (var doc in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
        {
            var ps = doc.panelSettings;
            if (ps == null) continue;

            if (!copyBaseScale.ContainsKey(ps))
            {
                if (Mathf.Approximately(k, 1f)) continue;
                if (!scaledCopyOf.TryGetValue(ps, out var copy) || copy == null)
                {
                    copy = UnityEngine.Object.Instantiate(ps);
                    copy.name = ps.name + " (sized)";
                    scaledCopyOf[ps]    = copy;
                    copyBaseScale[copy] = ps.scale;
                }
                doc.panelSettings = copy;
                ps = copy;
            }
            ps.scale = copyBaseScale[ps] * k;
        }
    }

    /// <summary>Per-tool cursors (trimmer, watering can). Off = the system arrow everywhere.</summary>
    public static bool CustomCursors
    {
        get => PlayerPrefs.GetInt(K_CUSTOM_CURSORS, 1) != 0;
        set { PlayerPrefs.SetInt(K_CUSTOM_CURSORS, value ? 1 : 0); Save(); Notify(); }
    }

    /// <summary>Stops the idle camera orbit and the wind sway. For motion sensitivity, and it
    /// doubles as a performance lever.</summary>
    public static bool ReducedMotion
    {
        get => PlayerPrefs.GetInt(K_REDUCED_MOTION, 0) != 0;
        set { PlayerPrefs.SetInt(K_REDUCED_MOTION, value ? 1 : 0); Save(); ApplySceneBound(); Notify(); }
    }

    /// <summary>
    /// Applies the settings that need objects from a loaded scene. Called from
    /// <see cref="ApplyAll"/> and worth calling again after a scene load, since these components
    /// are recreated each time.
    /// </summary>
    public static void ApplySceneBound()
    {
        bool reduced = ReducedMotion;

        var wind = WindManager.Instance;
        if (wind != null) wind.strength = reduced ? 0f : DefaultWindStrength(wind);

        var modes = PlayModeManager.Instance;
        if (modes != null && modes.ActiveMode != null && reduced)
            modes.ActiveMode.idleOrbit = false;

        GameManager.quickWinter = QuickWinter;
        ApplyAudio();
        ApplyUiScale();
    }

    // Remembers the authored wind strength so Reduced Motion can be switched back off without
    // having permanently zeroed the design value. WindManager is a singleton recreated per scene,
    // so the capture is keyed on the instance: a fresh instance arrives with its authored value
    // intact and is captured again.
    static WindManager windCaptured;
    static float       windCapturedStrength;

    static float DefaultWindStrength(WindManager w)
    {
        if (!ReferenceEquals(windCaptured, w))
        {
            windCaptured = w;
            windCapturedStrength = w.strength;
        }
        return windCapturedStrength;
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>Clears every <c>set.*</c> key and re-applies defaults. Deliberately does not touch
    /// saves, the progression profile, or the seen-tooltip list.</summary>
    public static void ResetToDefaults()
    {
        foreach (var k in new[]
        {
            K_DISPLAY_MODE, K_RES_W, K_RES_H, K_VSYNC, K_FPS_CAP, K_AA, K_QUALITY,
            K_VOL_MASTER, K_VOL_MUSIC, K_VOL_SFX, K_MUTE_MASTER,
            K_QUICK_WINTER, K_AUTOSAVE, K_DATE_FORMAT,
            K_COLORBLIND, K_REDUCED_MOTION, K_CUSTOM_CURSORS, K_UI_SCALE, K_HIGH_CONTRAST,
            K_OUTLINE,
        })
            PlayerPrefs.DeleteKey(k);
        foreach (var k in KeyBindings.PrefKeys())
            PlayerPrefs.DeleteKey(k);

        Save();
        ApplyAll();
        Notify();
        Debug.Log("[Settings] Reset to defaults.");
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    static void Save() => PlayerPrefs.Save();
    static void Notify() => OnChanged?.Invoke();
}
