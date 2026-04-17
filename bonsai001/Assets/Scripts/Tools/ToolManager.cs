using UnityEngine;
using UnityEngine.InputSystem;

public enum ToolType { None, Shears, SmallClippers, BigClippers, Saw, Wire, RemoveWire, Paste, AirLayer, Pinch, Defoliate, Graft }

public class ToolManager : MonoBehaviour
{
    public static ToolManager Instance;

    public ToolType ActiveTool { get; private set; } = ToolType.None;

    // Ordered list of tools the player cycles through with A / D.
    // Shears is not a selection-mode tool so it is excluded from the cycle.
    static readonly ToolType[] CycleOrder =
    {
        ToolType.SmallClippers,
        ToolType.BigClippers,
        ToolType.Saw,
        ToolType.Wire,
        ToolType.RemoveWire,
        ToolType.Paste,
        ToolType.AirLayer,
        ToolType.Pinch,
        ToolType.Defoliate,
        ToolType.Graft,
    };

    void Awake() => Instance = this;

    void Update()
    {
        if (Keyboard.current == null) return;

        // Only cycle tools during normal gameplay states (not menus, paused, etc.)
        var state = GameManager.Instance?.state ?? GameState.Idle;
        if (state == GameState.GamePause   || state == GameState.SpeciesSelect ||
            state == GameState.LoadMenu    || state == GameState.TreeDead      ||
            state == GameState.AirLayerSever) return;

        if (Keyboard.current.dKey.wasPressedThisFrame) CycleNext();
        if (Keyboard.current.aKey.wasPressedThisFrame) CyclePrev();
        if (Keyboard.current.sKey.wasPressedThisFrame) ClearTool();
    }

    void CycleNext()
    {
        int cur = System.Array.IndexOf(CycleOrder, ActiveTool);
        // If current tool isn't in cycle (None or Shears), start at index 0
        int next = cur < 0 ? 0 : (cur + 1) % CycleOrder.Length;
        SelectTool(CycleOrder[next]);
    }

    void CyclePrev()
    {
        int cur = System.Array.IndexOf(CycleOrder, ActiveTool);
        int prev = cur <= 0 ? CycleOrder.Length - 1 : cur - 1;
        SelectTool(CycleOrder[prev]);
    }

    /// <summary>Activate a tool. Pass None to deselect.</summary>
    public void SelectTool(ToolType tool)
    {
        ActiveTool = tool;
        GameManager.Instance?.TextCallFunction();
        GameManager.canTrim       = tool == ToolType.SmallClippers
                                 || tool == ToolType.BigClippers
                                 || tool == ToolType.Saw;
        GameManager.canWire       = tool == ToolType.Wire;
        GameManager.canRemoveWire = tool == ToolType.RemoveWire;
        GameManager.canPaste      = tool == ToolType.Paste;
        GameManager.canAirLayer   = tool == ToolType.AirLayer;
        GameManager.canPinch      = tool == ToolType.Pinch;
        GameManager.canDefoliate  = tool == ToolType.Defoliate;
        GameManager.canGraft      = tool == ToolType.Graft;
        Debug.Log($"[Tool] SelectTool={tool} | canTrim={GameManager.canTrim} canWire={GameManager.canWire} canRemoveWire={GameManager.canRemoveWire} canPaste={GameManager.canPaste} | gameState={GameManager.Instance?.state}");
    }

    public void ClearTool()
    {
        ActiveTool = ToolType.None;
        GameManager.Instance?.TextCallFunction();
        GameManager.canTrim       = false;
        GameManager.canWire       = false;
        GameManager.canRemoveWire = false;
        GameManager.canPaste      = false;
        GameManager.canAirLayer   = false;
        GameManager.canPinch      = false;
        GameManager.canDefoliate  = false;
        GameManager.canGraft      = false;
        Debug.Log($"[Tool] ClearTool | gameState={GameManager.Instance?.state}");
    }
}
