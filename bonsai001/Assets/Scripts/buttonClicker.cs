using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    UIDocument buttonDocument;
    Button trimButton;
    Button waterButton;
    Button wireButton;
    Button removeWireButton;
    Button rootPruneButton;
    Button quickWinterButton;
    Button pasteButton;

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


}
