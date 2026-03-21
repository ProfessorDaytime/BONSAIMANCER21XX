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

        if(trimButton != null)       Debug.Log("TRIM BUTTON");
        if(waterButton != null)      Debug.Log("WATER BUTTON");
        if(wireButton != null)       Debug.Log("WIRE BUTTON");
        if(removeWireButton != null) Debug.Log("REMOVE WIRE BUTTON");

        trimButton.RegisterCallback<ClickEvent>(OnTreeButtonClick);
        waterButton.RegisterCallback<ClickEvent>(OnWaterButtonClick);
        wireButton?.RegisterCallback<ClickEvent>(OnWireButtonClick);
        removeWireButton?.RegisterCallback<ClickEvent>(OnRemoveWireButtonClick);
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


}
