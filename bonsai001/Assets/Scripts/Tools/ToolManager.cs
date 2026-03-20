using UnityEngine;

/// <summary>
/// Tracks which tool the player currently has selected.
/// Tools are selected by UI buttons; TreeInteraction reads ActiveTool each frame.
///
/// Tier list (only SmallClippers wired up in Phase 3):
///   Shears        — leaves only (Phase 4)
///   SmallClippers — thin branches (radius up to ~0.4)  ← active now
///   BigClippers   — thick branches (radius 0.4–1.5)    ← stub
///   Saw           — trunk-level cuts (radius 1.5+)     ← stub
/// </summary>
public enum ToolType { None, Shears, SmallClippers, BigClippers, Saw }

public class ToolManager : MonoBehaviour
{
    public static ToolManager Instance;

    public ToolType ActiveTool { get; private set; } = ToolType.None;

    void Awake() => Instance = this;

    /// <summary>Activate a tool. Pass None to deselect.</summary>
    public void SelectTool(ToolType tool)
    {
        ActiveTool     = tool;
        GameManager.canTrim = tool == ToolType.SmallClippers
                           || tool == ToolType.BigClippers
                           || tool == ToolType.Saw;
    }

    public void ClearTool()
    {
        ActiveTool          = ToolType.None;
        GameManager.canTrim = false;
    }
}
