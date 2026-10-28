using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Main-menu scene driver (PLAN "Release Prep" R1).
///
/// <para><b>The class name is load-bearing</b> — this component is already attached to the Main
/// Camera in <c>MainMenu.unity</c>, and renaming the class would break that scene reference.
/// The old implementation was 33 lines: a 1.5-second blind wait, destroy the AudioManager, load
/// the game scene. All of that now lives in <see cref="MainMenuUI"/> and
/// <see cref="LoadingScreen"/>; this is just the bootstrapper.</para>
///
/// <para>It builds its own <see cref="UIDocument"/> at runtime so the scene needs no manual
/// wiring, and hides the legacy uGUI Canvas rather than requiring it be deleted by hand.
/// <c>Assets/Editor/MainMenuSceneSetup.cs</c> does the tidy-up properly when run.</para>
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Tooltip("Panel settings for the menu UI. Leave empty to resolve Assets/UI/MainPanelSettings " +
             "automatically in the editor, or to fall back to a runtime-created instance.")]
    [SerializeField] PanelSettings panelSettings;

    [Tooltip("Legacy uGUI canvas to hide. Leave empty to find a root object named 'Canvas'.")]
    [SerializeField] GameObject legacyCanvas;

    MainMenuUI ui;

    void Awake()
    {
        HideLegacyCanvas();
        UpgradeLegacyInputModule();

        // The player-grown showcase tree (R3), if one has been baked. Static set dressing only.
        MenuTreeShowcase.SpawnIfBaked();

        var doc = GetComponent<UIDocument>();
        if (doc == null) doc = gameObject.AddComponent<UIDocument>();

        if (doc.panelSettings == null)
            doc.panelSettings = ResolvePanelSettings();

        if (doc.panelSettings == null)
        {
            Debug.LogError("[MainMenu] No PanelSettings could be resolved — the menu cannot render. " +
                           "Assign one in the Inspector, or run Bonsai ▸ Set Up Main Menu Scene.");
            return;
        }

        doc.panelSettings = ScreenTargetCopy(doc.panelSettings);

        ui = new MainMenuUI(this);
        ui.Build(doc.rootVisualElement);
    }

    void OnDestroy() => ui?.Dispose();

    void Update()
    {
        if (ui == null) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !KeyBindings.Capturing)
            ui.OnEscape();
    }

    /// <summary>
    /// The old menu was a uGUI Canvas with TMP buttons. Rather than require it be deleted by hand
    /// before this works, it is simply switched off — which also means reverting to the old menu
    /// is a one-line change if something here goes wrong.
    /// </summary>
    void HideLegacyCanvas()
    {
        var go = legacyCanvas;

        if (go == null)
        {
            // Only look for a root-level Canvas; this must not disable a UIDocument's own panel.
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
            foreach (var c in canvases)
            {
                if (c.transform.parent == null) { go = c.gameObject; break; }
            }
        }

        if (go != null && go.activeSelf)
        {
            go.SetActive(false);
            Debug.Log($"[MainMenu] Legacy uGUI canvas '{go.name}' hidden in favour of the UI Toolkit menu.");
        }
    }

    /// <summary>
    /// The scene's EventSystem carries the legacy <c>StandaloneInputModule</c>, which reads the old
    /// <c>UnityEngine.Input</c> class. The project's Active Input Handling is "Input System
    /// Package" only, so that module throws an InvalidOperationException every frame in
    /// <c>UpdateModule</c> — and because UI Toolkit routes pointer input through the EventSystem
    /// when one is present, the menu receives no clicks at all.
    ///
    /// <para>This predates R1 (the module is in the committed scene) — the old uGUI menu was
    /// throwing the same error. Swapped for <c>InputSystemUIInputModule</c> here, in Awake, which
    /// runs before the EventSystem's first Update, so not even one frame throws.
    /// <c>Bonsai ▸ Set Up Main Menu Scene</c> makes the swap permanent in the scene.</para>
    /// </summary>
    static void UpgradeLegacyInputModule()
    {
        var es = FindAnyObjectByType<EventSystem>();
        if (es == null) return;

        var legacy = es.GetComponent<StandaloneInputModule>();
        if (legacy == null) return;

        // Disable first: Destroy is deferred to end of frame, and a disabled module is not ticked.
        legacy.enabled = false;
        Destroy(legacy);

        if (es.GetComponent<InputSystemUIInputModule>() == null)
            es.gameObject.AddComponent<InputSystemUIInputModule>();

        Debug.Log("[MainMenu] Replaced legacy StandaloneInputModule with InputSystemUIInputModule " +
                  "(project uses the Input System package only).");
    }

    /// <summary>
    /// <c>MainPanelSettings</c> does not draw to the screen: its Target Texture is
    /// <c>UIToolkitRT</c>, and in <c>Game001</c> a uGUI RawImage composites that texture onto the
    /// screen (which is presumably how the in-game UI gets the CRT treatment). The menu scene has
    /// no such RawImage, so a menu built on the shared asset renders into a texture nobody displays
    /// — a blank screen with no errors. Found in the first play-test.
    ///
    /// <para>Fix: a runtime copy with no target texture, so it draws straight to the screen. A copy
    /// rather than an edit, because mutating the shared asset would break <c>Game001</c>'s HUD and,
    /// in the editor, persist into the project. This is also the separate menu PanelSettings that
    /// R2's UI-scale note said was needed.</para>
    /// </summary>
    PanelSettings ScreenTargetCopy(PanelSettings source)
    {
        if (source.targetTexture == null) return source;   // already screen-targeted

        var copy = Instantiate(source);
        copy.name = source.name + " (menu, screen)";
        copy.targetTexture = null;
        // The shared asset clears its render texture to transparent black before drawing, which is
        // right for a texture. Drawing to the screen, that clear wipes out everything the camera
        // just rendered — the R3 showcase tree included — leaving a black backdrop.
        copy.clearColor = false;
        return copy;
    }

    PanelSettings ResolvePanelSettings()
    {
        if (panelSettings != null) return panelSettings;

#if UNITY_EDITOR
        // Editor-only convenience so a freshly opened scene just works. A real build relies on the
        // serialized reference, which the editor setup tool assigns.
        const string path = "Assets/UI/MainPanelSettings.asset";
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (asset != null) return asset;
#endif

        // Last resort: a bare instance. Renders unstyled defaults, but every element in this menu
        // sets its own colours inline, so the menu is still usable.
        Debug.LogWarning("[MainMenu] Falling back to a runtime-created PanelSettings. " +
                         "Assign Assets/UI/MainPanelSettings in the Inspector to silence this.");
        return ScriptableObject.CreateInstance<PanelSettings>();
    }

    // ── Legacy API ────────────────────────────────────────────────────────────
    // Kept because the hidden uGUI buttons still hold persistent onClick references to these.
    // A broken UnityEvent target logs an error every time the object is enabled, so removing
    // these would be noisier than keeping them.

    /// <summary>Starts a new tree. Equivalent to the menu's NEW TREE button.</summary>
    public void StartGame()
    {
        SaveManager.ActiveSlotId  = null;
        SaveManager.PendingLaunch = PendingLaunchAction.NewTree;
        StartCoroutine(LegacyLoad());
    }

    public void QuitGame() => Application.Quit();

    IEnumerator LegacyLoad()
    {
        // Same reason as MainMenuUI.LaunchRoutine: never let the menu's AudioManager reach Game001.
        if (AudioManager.Instance != null) AudioManager.Instance.DestroyAudioManager();
        yield return null;
        SceneManager.LoadSceneAsync("Game001");
    }
}
