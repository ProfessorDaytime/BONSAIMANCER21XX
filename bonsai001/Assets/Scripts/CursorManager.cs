using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

/// <summary>
/// Per-tool mouse cursors (PLAN "Release Prep" R4).
///
/// <para>Installs itself in any scene with a <see cref="GameManager"/>, and reads the active tool
/// from <see cref="ToolManager"/> every frame, so no other code has to remember to update it.
/// The arrow comes back over UI, in menus and dialogs, and whenever custom cursors are switched off
/// in Settings ▸ Accessibility.</para>
///
/// <para>Art lives in <c>Resources/Cursors/</c> as 48×48 frames cut from the sprite sheets in
/// <c>Assets/Sprite/</c> (each sheet frame is 48-px art drawn at 5×). <c>CursorImportSettings</c>
/// imports them as Cursor textures. A tool's frame 0 is its idle cursor; <see cref="PlayUse"/>
/// runs the rest once — the trimmer snips, the can pours.</para>
///
/// <para>Only the trim tools and the watering can have art so far. Every other tool keeps the
/// arrow until it gets its own.</para>
/// </summary>
public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    enum Kind { Arrow, Trimmer, Can }

    // Hotspots are where the click lands, in pixels from the top-left of a 48×48 frame:
    // between the trimmer's open blades (idle frame 0 is open; the snip closes and reopens them)
    // and the can's spout.
    static readonly Vector2 TrimmerHotspot = new Vector2(24, 6);
    static readonly Vector2 CanHotspot     = new Vector2(3, 22);

    const float TrimmerFrameTime = 0.05f;
    const float CanFrameTime     = 0.09f;

    Texture2D[] trimmer, can;

    Kind  shownKind  = Kind.Arrow;
    int   shownFrame = -1;
    Kind  animKind   = Kind.Arrow;
    float animStart  = -1f;

    // ── Install ───────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded += (_, __) => EnsureExists();
        EnsureExists();
    }

    static void EnsureExists()
    {
        if (Instance != null || FindAnyObjectByType<GameManager>() == null) return;
        new GameObject("CursorManager").AddComponent<CursorManager>();
    }

    void Awake()
    {
        Instance = this;
        trimmer  = LoadFrames("trimmer", 4);
        can      = LoadFrames("can", 8);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    // Alt-tabbing away can drop a custom cursor on some platforms; re-apply on return.
    void OnApplicationFocus(bool focused)
    {
        if (focused) shownFrame = -1;
    }

    static Texture2D[] LoadFrames(string name, int count)
    {
        var frames = new Texture2D[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = Resources.Load<Texture2D>($"Cursors/{name}_{i}");
            if (frames[i] == null) return null;   // incomplete set: fall back to the arrow
        }
        return frames;
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>Plays the current tool's use animation once (a snip, a pour).</summary>
    public static void PlayUse()
    {
        if (Instance == null) return;
        Instance.animKind  = Instance.DesiredKind();
        Instance.animStart = Time.unscaledTime;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        Kind kind  = DesiredKind();
        int  frame = 0;

        if (animStart >= 0f && animKind == kind)
        {
            var frames = Frames(kind);
            float step = kind == Kind.Can ? CanFrameTime : TrimmerFrameTime;
            int   f    = Mathf.FloorToInt((Time.unscaledTime - animStart) / step);
            if (frames != null && f < frames.Length) frame = f;
            else animStart = -1f;
        }
        else
        {
            animStart = -1f;
        }

        Show(kind, frame);
    }

    void Show(Kind kind, int frame)
    {
        if (kind == shownKind && frame == shownFrame) return;
        shownKind  = kind;
        shownFrame = frame;

        var frames = Frames(kind);
        if (frames == null)
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        else
            Cursor.SetCursor(frames[frame], kind == Kind.Can ? CanHotspot : TrimmerHotspot, CursorMode.Auto);
    }

    Texture2D[] Frames(Kind kind) => kind switch
    {
        Kind.Trimmer => trimmer,
        Kind.Can     => can,
        _            => null,
    };

    Kind DesiredKind()
    {
        if (!GameSettings.CustomCursors) return Kind.Arrow;

        var state = GameManager.Instance != null ? GameManager.Instance.state : GameState.Idle;
        if (state == GameState.GamePause || state == GameState.CalendarOpen ||
            state == GameState.TipPause  || state == GameState.LoadMenu     ||
            state == GameState.SpeciesSelect || state == GameState.TreeDead ||
            state == GameState.AirLayerSever || state == GameState.RockPlace ||
            state == GameState.TreeOrient)
            return Kind.Arrow;

        var tool = ToolManager.Instance != null ? ToolManager.Instance.ActiveTool : ToolType.None;
        Kind kind = tool switch
        {
            ToolType.Shears or ToolType.SmallClippers or ToolType.BigClippers => Kind.Trimmer,
            ToolType.WateringCan                                               => Kind.Can,
            _                                                                  => Kind.Arrow,
        };
        if (kind == Kind.Arrow) return kind;

        // Buttons and panels always get the arrow — nobody wants to click "Save" with shears.
        return PointerOverUI() ? Kind.Arrow : kind;
    }

    // ── UI hover ──────────────────────────────────────────────────────────────

    UIDocument[] docs;
    float        docsRefreshAt;

    /// <summary>
    /// True when the mouse is over a UI Toolkit element. The in-game panel is a full-screen
    /// container, so a hit on an element that covers the whole panel (the root, a transparent
    /// overlay layer) doesn't count — only buttons, panels and other real UI do.
    /// </summary>
    public static bool PointerOverUI()
    {
        if (Instance == null) return false;
        return Instance.PointerOverUIInternal();
    }

    bool PointerOverUIInternal()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return false;

        if (docs == null || Time.unscaledTime >= docsRefreshAt)
        {
            docs = FindObjectsByType<UIDocument>();
            docsRefreshAt = Time.unscaledTime + 2f;
        }

        Vector2 screen = mouse.position.ReadValue();
        screen.y = Screen.height - screen.y;   // panel space is top-down

        foreach (var doc in docs)
        {
            if (doc == null || !doc.isActiveAndEnabled) continue;
            var panel = doc.rootVisualElement?.panel;
            if (panel == null) continue;

            var picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, screen));
            if (picked == null) continue;

            Rect whole = panel.visualTree.worldBound;
            Rect box   = picked.worldBound;
            if (box.width >= whole.width * 0.98f && box.height >= whole.height * 0.98f) continue;
            return true;
        }
        return false;
    }
}
