using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Keeps input sane when the game window loses and regains focus (PLAN R5b, 2026-10-29). A
/// friend's Mac build stopped responding to clicks after a while; this both guards against the
/// usual causes and logs enough to find the real one next time.
///
/// <list type="bullet">
/// <item><b>Focus lost:</b> raises <see cref="FocusLost"/> so anything mid-gesture ends cleanly
/// (watering pour, saw, wire aim, camera drag/pan), and logs the game state.</item>
/// <item><b>Focus regained:</b> resets the mouse and keyboard so no button is stuck "held" (a
/// stuck press makes UI and tools ignore clicks), clears the uGUI selection, and swallows the
/// click that brought the window back (<see cref="SwallowingClick"/>) so it can't trim or water.
/// </item>
/// <item><b>Every left click</b> (player builds only) is logged with its screen position and the
/// UI element under it. If clicks stop working again, the log shows whether something invisible
/// is catching them or the input never arrives.</item>
/// </list>
/// Installs itself before the first scene loads and lives for the whole session.
/// </summary>
public class FocusGuard : MonoBehaviour
{
    /// <summary>Raised when the window loses focus. End any press-and-hold action here.</summary>
    public static event Action FocusLost;

    /// <summary>True from regaining focus until the mouse button that came with it is released,
    /// so world clicks (tools, camera drags) ignore it.</summary>
    public static bool SwallowingClick { get; private set; }

    static FocusGuard instance;
    float swallowUntil;
    bool  focused = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("_FocusGuard");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<FocusGuard>();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus == focused) return;
        focused = hasFocus;

        if (!hasFocus)
        {
            Debug.Log($"[Focus] Lost | {StateSummary()}");
            try { FocusLost?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            return;
        }

        // Clear anything the OS left half-pressed while we weren't looking.
        if (Mouse.current != null)    InputSystem.ResetDevice(Mouse.current);
        if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
        EventSystem.current?.SetSelectedGameObject(null);

        SwallowingClick = true;
        swallowUntil    = Time.unscaledTime + 0.4f;
        Debug.Log($"[Focus] Regained | {StateSummary()}");
    }

    void Update()
    {
        var mouse = Mouse.current;

        if (SwallowingClick)
        {
            bool held = mouse != null && mouse.leftButton.isPressed;
            if (!held && Time.unscaledTime >= swallowUntil) SwallowingClick = false;
        }

        // Click log: player builds only — in the editor it would bury the console.
        if (!Application.isEditor && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 pos = mouse.position.ReadValue();
            Debug.Log($"[Input] Click at ({pos.x:F0}, {pos.y:F0}) of {Screen.width}×{Screen.height} → " +
                      $"{UiUnder(pos)}{(SwallowingClick ? " (swallowed: refocus click)" : "")} | {StateSummary()}");
        }
    }

    static string StateSummary()
    {
        var gm = GameManager.Instance;
        string state = gm != null ? gm.state.ToString() : "no GameManager";
        return $"state={state} timeScale={Time.timeScale:F2} focused={Application.isFocused}";
    }

    /// <summary>The topmost UI Toolkit element under a screen point, across every UIDocument, or
    /// "no UI". Full-screen containers are reported too — an invisible one catching clicks is
    /// exactly the failure being looked for.</summary>
    static string UiUnder(Vector2 screenPos)
    {
        Vector2 flipped = new Vector2(screenPos.x, Screen.height - screenPos.y);
        foreach (var doc in FindObjectsByType<UIDocument>())
        {
            if (doc == null || !doc.isActiveAndEnabled) continue;
            var panel = doc.rootVisualElement?.panel;
            if (panel == null) continue;
            var picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, flipped));
            if (picked == null) continue;
            Rect whole = panel.visualTree.worldBound;
            Rect box   = picked.worldBound;
            bool full  = box.width >= whole.width * 0.98f && box.height >= whole.height * 0.98f;
            string name = string.IsNullOrEmpty(picked.name) ? "" : "#" + picked.name;
            string text = picked is TextElement te && !string.IsNullOrEmpty(te.text)
                ? $" \"{(te.text.Length > 24 ? te.text.Substring(0, 24) + "…" : te.text)}\"" : "";
            return $"{picked.GetType().Name}{name}{text}{(full ? " [full-screen]" : "")} in {doc.name}";
        }
        return "no UI";
    }
}
