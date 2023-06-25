using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ToolTip : MonoBehaviour
{
    public void StateIdle(){
        Debug.Log("Clicked the X");
        GameManager.Instance.UpdateGameState(GameState.Idle);
    }
}
