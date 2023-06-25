using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    UIDocument buttonDocument;
    Button trimButton;
    Button waterButton;
    // Start is called before the first frame update
    void OnEnable(){

        buttonDocument = GetComponent<UIDocument>();

        if(buttonDocument == null){
            Debug.LogError("No button document found");
        }

        trimButton = buttonDocument.rootVisualElement.Q("TrimButton") as Button;

        waterButton = buttonDocument.rootVisualElement.Q("WaterButton") as Button;

        if(trimButton != null){
            Debug.Log("TRIM BUTTON");
        }
        if(waterButton != null){
            Debug.Log("WATER BUTTON");
        }

        trimButton.RegisterCallback<ClickEvent>(OnTreeButtonClick);
        waterButton.RegisterCallback<ClickEvent>(OnWaterButtonClick);
    }

    public void OnTreeButtonClick(ClickEvent evt){
        // Debug.Log("The tree has been trimmed");
        GameManager.canTrim = true;
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


}
