using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    [SerializeField] TreeSkeleton skeleton;

    UIDocument buttonDocument;
    Button trimButton;
    Button waterButton;
    Button wireButton;
    Button removeWireButton;
    Button rootPruneButton;
    Button quickWinterButton;
    Button pasteButton;
    Button airLayerButton;
    Slider selectionRadiusSlider;

    VisualElement rootHealthPanel;
    Label         rootHealthScoreLabel;
    VisualElement rootHealthSectors;

    // Start is called before the first frame update
    void OnEnable(){

        buttonDocument = GetComponent<UIDocument>();

        if(buttonDocument == null){
            Debug.LogError("No button document found");
        }

        trimButton       = buttonDocument.rootVisualElement.Q("TrimButton")       as Button;
        waterButton      = buttonDocument.rootVisualElement.Q("WaterButton")      as Button;
        wireButton       = buttonDocument.rootVisualElement.Q("WireButton")       as Button;
        removeWireButton = buttonDocument.rootVisualElement.Q("RemoveWireButton") as Button;
        rootPruneButton  = buttonDocument.rootVisualElement.Q("RootPruneButton")  as Button;
        quickWinterButton = buttonDocument.rootVisualElement.Q("QuickWinterButton") as Button;
        pasteButton      = buttonDocument.rootVisualElement.Q("PasteButton")      as Button;
        airLayerButton         = buttonDocument.rootVisualElement.Q("AirLayerButton")         as Button;
        selectionRadiusSlider  = buttonDocument.rootVisualElement.Q("SelectionRadiusSlider") as Slider;

        rootHealthPanel      = buttonDocument.rootVisualElement.Q("RootHealthPanel");
        rootHealthScoreLabel = buttonDocument.rootVisualElement.Q("RootHealthScoreLabel") as Label;
        rootHealthSectors    = buttonDocument.rootVisualElement.Q("RootHealthSectors");

        // Build the 8 sector indicator squares
        if (rootHealthSectors != null)
        {
            for (int i = 0; i < 8; i++)
            {
                var sq = new VisualElement();
                sq.name = $"RootHealthSector{i}";
                sq.style.width           = 14;
                sq.style.height          = 14;
                sq.style.borderTopLeftRadius     = 2;
                sq.style.borderTopRightRadius    = 2;
                sq.style.borderBottomLeftRadius  = 2;
                sq.style.borderBottomRightRadius = 2;
                sq.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
                rootHealthSectors.Add(sq);
            }
        }

        if(trimButton != null)       Debug.Log("TRIM BUTTON");
        if(waterButton != null)      Debug.Log("WATER BUTTON");
        if(wireButton != null)       Debug.Log("WIRE BUTTON");
        if(removeWireButton != null) Debug.Log("REMOVE WIRE BUTTON");
        if(rootPruneButton != null)  Debug.Log("ROOT PRUNE BUTTON");

        trimButton.RegisterCallback<ClickEvent>(OnTreeButtonClick);
        waterButton.RegisterCallback<ClickEvent>(OnWaterButtonClick);
        wireButton?.RegisterCallback<ClickEvent>(OnWireButtonClick);
        removeWireButton?.RegisterCallback<ClickEvent>(OnRemoveWireButtonClick);
        rootPruneButton?.RegisterCallback<ClickEvent>(OnRootPruneButtonClick);
        quickWinterButton?.RegisterCallback<ClickEvent>(OnQuickWinterButtonClick);
        pasteButton?.RegisterCallback<ClickEvent>(OnPasteButtonClick);
        airLayerButton?.RegisterCallback<ClickEvent>(OnAirLayerButtonClick);
        selectionRadiusSlider?.RegisterValueChangedCallback(evt => GameManager.selectionRadius = evt.newValue);
    }

    public void OnTreeButtonClick(ClickEvent evt){
        ToolManager.Instance.SelectTool(ToolType.SmallClippers);
        AudioManager.Instance.PlaySFX("Trim");
    }

    public void OnWaterButtonClick(ClickEvent evt){
        AudioManager.Instance.PlaySFX("Water");

        //REMEMBER TO UNCOMMENT THIS IF STATEMENT TO PUT THE TOOLTIP BACK IN
        if(GameManager.Instance.state == GameState.Idle){
            GameManager.Instance.UpdateGameState(GameState.Water);

            if(GameManager.waterings == -1){
                GameManager.Instance.UpdateGameState(GameState.BranchGrow);
                GameManager.waterings++;
                Debug.Log("The tree has been watered");
                // AudioManager.Instance.PlaySFX("Water");
            }
        }
        
        
     }

    public void OnWireButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.Wire);
    }

    public void OnRemoveWireButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.RemoveWire);
    }

    public void OnRootPruneButtonClick(ClickEvent evt)
    {
        GameManager.Instance.ToggleRootPrune();
        // Root health panel hidden until the air-layering system is built
        // bool inRootMode = GameManager.Instance.state == GameState.RootPrune;
        // if (rootHealthPanel != null)
        //     rootHealthPanel.style.display = inRootMode ? DisplayStyle.Flex : DisplayStyle.None;
        // if (inRootMode) UpdateRootHealthDisplay();
    }

    void UpdateRootHealthDisplay()
    {
        if (skeleton == null || rootHealthScoreLabel == null || rootHealthSectors == null) return;

        int score = Mathf.RoundToInt(skeleton.RootHealthScore);
        rootHealthScoreLabel.text = score.ToString();

        float[] coverage = skeleton.RootHealthSectorCoverage;
        for (int i = 0; i < 8 && i < rootHealthSectors.childCount; i++)
        {
            float t   = i < coverage.Length ? coverage[i] : 0f;
            // Lerp dim grey → muted green based on sector coverage strength
            var   col = Color.Lerp(new Color(0.15f, 0.15f, 0.15f), new Color(0.3f, 0.75f, 0.3f), t);
            rootHealthSectors[i].style.backgroundColor = new StyleColor(col);
        }
    }

    public void OnQuickWinterButtonClick(ClickEvent evt)
    {
        GameManager.quickWinter = !GameManager.quickWinter;
        quickWinterButton.style.backgroundColor = GameManager.quickWinter
            ? new StyleColor(new UnityEngine.Color(0.75f, 0.75f, 0.75f))
            : new StyleColor(new UnityEngine.Color(0.25f, 0.25f, 0.25f));
        quickWinterButton.style.color = GameManager.quickWinter
            ? new StyleColor(UnityEngine.Color.black)
            : new StyleColor(new UnityEngine.Color(0.78f, 0.78f, 0.78f));
    }

    public void OnPasteButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.Paste);
    }

    public void OnAirLayerButtonClick(ClickEvent evt)
    {
        ToolManager.Instance.SelectTool(ToolType.AirLayer);
    }


}
