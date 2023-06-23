using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    UIDocument buttonDocument;
    Button uiButton;
    // Start is called before the first frame update
    void OnEnable()
    {

        buttonDocument = GetComponent<UIDocument>();

        if(buttonDocument == null)
        {
            Debug.LogError("No button document found");
        }

        uiButton = buttonDocument.rootVisualElement.Q("TrimButton") as Button;

        if(uiButton != null)
        {
            Debug.Log("TRIM BUTTON");
        }
        uiButton.RegisterCallback<ClickEvent>(OnButtonClick);
    }

    public void OnButtonClick(ClickEvent evt)
    {
        Debug.Log("The tree has been trimmed");
    }


}
