using UnityEngine;

public enum ToolType { None, Shears, SmallClippers, BigClippers, Saw, Wire, RemoveWire }

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
    }

    public void ClearTool()
    {
        ActiveTool                = ToolType.None;
        GameManager.canTrim       = false;
        GameManager.canWire       = false;
        GameManager.canRemoveWire = false;
    }
}
