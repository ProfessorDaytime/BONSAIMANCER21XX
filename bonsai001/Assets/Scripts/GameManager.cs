using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameState state;

    public static event Action<GameState> OnGameStateChanged;

    public static int waterings = -1;

    void Awake(){
        Instance = this;
        UpdateGameState(GameState.Water);
    }

    void Start(){
        
    }

    public void UpdateGameState(GameState newState){
        state = newState;

        Debug.Log("GAME STATE: " + newState);

        switch(newState){
            case GameState.Menu:
                break;
            case GameState.TipPause:
                break;
            case GameState.GamePause:
                break;
            case GameState.Idle:
                break;
            case GameState.Water:
                HandleWaterState();
                break;
            case GameState.BranchGrow:
                break;
            case GameState.LeafGrow:
                break;
            case GameState.LeafFall:
                break;
            case GameState.Pruning:
                break;
            case GameState.Shaping:
                break;
            default: 
                throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
        }

        OnGameStateChanged?.Invoke(newState);
    }
    

    void HandleWaterState(){
        if(waterings < 0){
            Debug.Log("Waterings is -1");
        }
    }






}


public enum GameState {
    Menu,
    TipPause,
    GamePause,
    Idle,
    Water,
    BranchGrow,
    LeafGrow,
    LeafFall,
    Pruning,
    Shaping
}