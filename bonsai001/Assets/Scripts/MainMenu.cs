using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void StartGame(){
        SceneManager.LoadSceneAsync("Game001");
        //Change the gamestate to TipPause
        GameManager.Instance.UpdateGameState(GameState.TipPause);
    }

    public void QuitGame(){
        Application.Quit();
    }
}
