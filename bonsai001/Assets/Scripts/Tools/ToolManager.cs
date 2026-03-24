using UnityEngine;

public enum ToolType { None, Shears, SmallClippers, BigClippers, Saw, Wire, RemoveWire, Paste }

public class ToolManager : MonoBehaviour
{
    public static ToolManager Instance;

    public ToolType ActiveTool { get; private set; } = ToolType.None;

    void Awake() => Instance = this;

    /// <summary>Activate a tool. Pass None to deselect.</summary>
    public void SelectTool(ToolType tool)
    {
        ActiveTool                = tool;
        GameManager.canTrim       = tool == ToolType.SmallClippers
                                 || tool == ToolType.BigClippers
                                 || tool == ToolType.Saw;
        GameManager.canWire       = tool == ToolType.Wire;
        GameManager.canRemoveWire = tool == ToolType.RemoveWire;
        GameManager.canPaste      = tool == ToolType.Paste;
        Debug.Log($"[Tool] SelectTool={tool} | canTrim={GameManager.canTrim} canWire={GameManager.canWire} canRemoveWire={GameManager.canRemoveWire} canPaste={GameManager.canPaste} | gameState={GameManager.Instance?.state}");
    }

    public void ClearTool()
    {
        ActiveTool                = ToolType.None;
        GameManager.canTrim       = false;
        GameManager.canWire       = false;
        GameManager.canRemoveWire = false;
        GameManager.canPaste      = false;
        Debug.Log($"[Tool] ClearTool | gameState={GameManager.Instance?.state}");
    }
}
