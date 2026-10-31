using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Pop-out mini player (2026-10-31), like a video player's picture-in-picture button: the game
/// shrinks to a small resizable window that shows only the tree, for leaving it growing on a
/// second screen.
/// <list type="bullet">
/// <item>Every UI document and screen canvas is hidden; only a small "exit mini" button shows,
/// and only while the mouse is moving over the window.</item>
/// <item>The tool is put down so clicks can't trim or water; dragging still orbits and the wheel
/// still zooms.</item>
/// <item>On Windows the window stays on top of other windows. Elsewhere it is a plain window.</item>
/// <item>Esc (caught in <see cref="buttonClicker"/>) or the button returns to the window size
/// and mode it came from. Anything that needs the player (pause, a dialog, the tree dying)
/// brings the full game back on its own; first-use tips wait until it's back.</item>
/// </list>
/// The mini window's size is remembered between uses. Window changes only happen in a build;
/// in the editor the UI still hides, which is enough to try it.
/// </summary>
public class MiniPlayer : MonoBehaviour
{
    public static bool Active => instance != null;

    const string K_W = "MiniPlayer.Width";
    const string K_H = "MiniPlayer.Height";
    const int    DefaultW = 420, DefaultH = 520;
    const float  ButtonShowSeconds = 2f;

    static MiniPlayer instance;

    FullScreenMode savedMode;
    int            savedW, savedH;
    readonly List<(VisualElement el, DisplayStyle display)> hidden = new List<(VisualElement, DisplayStyle)>();
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    Button exitButton;
    float  lastMouseMove;

    /// <summary>Shrinks to the mini window. <paramref name="host"/> is the panel root the exit
    /// button goes in.</summary>
    public static void Enter(VisualElement host)
    {
        if (instance != null || host == null) return;
        var s = GameManager.Instance != null ? GameManager.Instance.state : GameState.Idle;
        if (NeedsFullGame(s) || s == GameState.TipPause) return;
        if (QuickStartManager.Instance != null && QuickStartManager.Instance.IsGenerating) return;

        var go = new GameObject("_MiniPlayer");
        instance = go.AddComponent<MiniPlayer>();
        instance.Begin(host);
    }

    public static void Exit()
    {
        if (instance == null) return;
        instance.End();
        Destroy(instance.gameObject);
        instance = null;
    }

    /// <summary>States the player has to see and answer.</summary>
    static bool NeedsFullGame(GameState s) =>
        (GameManager.IsOverlayState(s) && s != GameState.TipPause) ||
        s == GameState.TreeDead || s == GameState.Menu || s == GameState.SpeciesSelect;

    void Begin(VisualElement host)
    {
        ToolManager.Instance?.ClearTool();

        foreach (var doc in FindObjectsByType<UIDocument>())
        {
            var root = doc.rootVisualElement;
            if (root == null) continue;
            foreach (var child in root.Children())
            {
                hidden.Add((child, child.style.display.value));
                child.style.display = DisplayStyle.None;
            }
        }
        foreach (var c in FindObjectsByType<Canvas>())
        {
            if (!c.enabled || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            c.enabled = false;
            hiddenCanvases.Add(c);
        }

        exitButton = new Button(Exit) { name = "MiniPlayerExit", text = "Exit mini", tooltip = "Back to the full game (Esc)" };
        var b = exitButton.style;
        b.position = Position.Absolute;
        b.top      = 8;
        b.right    = 8;
        b.width    = 72;
        b.height   = 28;
        b.fontSize = 11;
        b.paddingLeft = b.paddingRight = b.paddingTop = b.paddingBottom = 0;
        b.backgroundColor = new StyleColor(new Color(20f / 255f, 20f / 255f, 20f / 255f, 0.85f));
        b.color           = new StyleColor(new Color(229f / 255f, 162f / 255f, 16f / 255f));
        b.borderTopColor = b.borderBottomColor =
        b.borderLeftColor = b.borderRightColor = new StyleColor(new Color(80f / 255f, 80f / 255f, 80f / 255f));
        b.borderTopWidth = b.borderBottomWidth = b.borderLeftWidth = b.borderRightWidth = 1;
        b.borderTopLeftRadius = b.borderTopRightRadius =
        b.borderBottomLeftRadius = b.borderBottomRightRadius = 4;
        host.Add(exitButton);
        lastMouseMove = Time.unscaledTime;

        savedMode = Screen.fullScreenMode;
        savedW    = Screen.width;
        savedH    = Screen.height;
        int w = PlayerPrefs.GetInt(K_W, DefaultW), h = PlayerPrefs.GetInt(K_H, DefaultH);
        Screen.SetResolution(w, h, FullScreenMode.Windowed);
        StartCoroutine(AfterResize(true));

        GameManager.OnGameStateChanged += OnStateChanged;
        Debug.Log($"[MiniPlayer] On | {w}×{h} from {savedW}×{savedH} {savedMode}");
    }

    void End()
    {
        GameManager.OnGameStateChanged -= OnStateChanged;
        if (Screen.fullScreenMode == FullScreenMode.Windowed)
        {
            PlayerPrefs.SetInt(K_W, Screen.width);
            PlayerPrefs.SetInt(K_H, Screen.height);
        }
        SetTopmost(false);
        Screen.SetResolution(savedW, savedH, savedMode);

        exitButton?.RemoveFromHierarchy();
        foreach (var (el, display) in hidden) el.style.display = display;
        foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
        hidden.Clear();
        hiddenCanvases.Clear();
        FindAnyObjectByType<CameraOrbit>()?.ResetView();
        Debug.Log($"[MiniPlayer] Off | back to {savedW}×{savedH} {savedMode}");
    }

    /// <summary>The new size lands at the end of the frame; frame the tree for it after that.</summary>
    IEnumerator AfterResize(bool topmost)
    {
        yield return null;
        yield return null;
        SetTopmost(topmost);
        FindAnyObjectByType<CameraOrbit>()?.ResetView();
    }

    void OnStateChanged(GameState s)
    {
        if (NeedsFullGame(s)) Exit();
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f) lastMouseMove = Time.unscaledTime;
        if (exitButton != null)
            exitButton.style.display = Time.unscaledTime - lastMouseMove < ButtonShowSeconds
                ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void OnDestroy()
    {
        if (instance == this) { GameManager.OnGameStateChanged -= OnStateChanged; instance = null; }
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HwndTopmost = new IntPtr(-1), HwndNoTopmost = new IntPtr(-2);
    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    static IntPtr hwnd;

    // The window is caught while it has focus (the pop-out click), so turning topmost off later
    // still works if the game is in the background by then.
    static void SetTopmost(bool on)
    {
        if (hwnd == IntPtr.Zero) hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, on ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
    }
#else
    static void SetTopmost(bool on) { }
#endif
}
