using UnityEngine;

/// <summary>
/// Legacy tooltip component — replaced by the UIElements TooltipOverlay system in ButtonClicker.
/// This stub keeps the scene reference intact. The tipPanel/tipCanvas GameObjects should be
/// disabled or deleted from the scene; this script no longer shows or hides them.
/// </summary>
public class ToolTip : MonoBehaviour
{
    [SerializeField] GameObject tipPanel;
    [SerializeField] GameObject tipCanvas;

    void Awake()
    {
        // Keep both objects permanently hidden — the new overlay handles TipPause.
        if (tipPanel  != null) tipPanel.SetActive(false);
        if (tipCanvas != null) tipCanvas.SetActive(false);
    }

    /// <summary>Legacy exit button handler — routes to ExitTipPause so it works with the new system.</summary>
    public void StateIdle()
    {
        GameManager.Instance?.ExitTipPause();
    }
}
