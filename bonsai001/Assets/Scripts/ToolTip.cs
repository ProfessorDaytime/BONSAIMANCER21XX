using UnityEngine;

public class ToolTip : MonoBehaviour
{
    // Drag the ToolTipDarkPanel and ToolTipCanvas GameObjects here in the Inspector.
    // The script hides these two children without touching the parent (which holds the camera).
    [SerializeField] GameObject tipPanel;
    [SerializeField] GameObject tipCanvas;

    void Awake()
    {
        GameManager.OnGameStateChanged += OnStateChanged;
    }

    void Start()
    {
        if (GameManager.Instance != null)
            OnStateChanged(GameManager.Instance.state);
    }

    void OnDestroy()
    {
        GameManager.OnGameStateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameState state)
    {
        bool show = state == GameState.TipPause;
        if (tipPanel != null)  tipPanel.SetActive(show);
        if (tipCanvas != null) tipCanvas.SetActive(show);
    }

    public void StateIdle()
    {
        if (GameManager.Instance.state == GameState.SpeciesSelect) return;
        if (GameManager.Instance.state == GameState.LoadMenu) return;
        Debug.Log("Clicked the X");
        GameManager.Instance.UpdateGameState(GameState.Idle);
    }
}
