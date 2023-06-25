using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void StartGame(){
        StartCoroutine(StartGameCoroutine());
    }


    IEnumerator StartGameCoroutine()
    {
        //Print the time of when the function is first called.
        // Debug.Log("Started Coroutine at timestamp : " + Time.time);

        //yield on a new YieldInstruction that waits for 5 seconds.
        yield return new WaitForSeconds(1.5f);

        //After we have waited 5 seconds print the time again.
        AudioManager.Instance.DestroyAudioManager();

        SceneManager.LoadSceneAsync("Game001");
        //Change the gamestate to TipPause
        // GameManager.Instance.UpdateGameState(GameState.TipPause);
        // Debug.Log("Finished Coroutine at timestamp : " + Time.time);
    }

    public void QuitGame(){
        Application.Quit();
    }
}
