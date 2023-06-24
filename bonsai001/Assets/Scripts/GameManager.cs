using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameState state;

    public static event Action<GameState> OnGameStateChanged;

    void Awake(){
        Instance = this;
    }

    void Start(){
        
    }

    public void UpdateGameState(GameState newState){
        state = newState;

        switch(newState){
            case GameState.Menu:
                break;
            case GameState.TipPause:
                break;
            case GameState.GamePause:
                break;
            case GameState.Water:
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
    



}


public enum GameState {
    Menu,
    TipPause,
    GamePause,
    Water,
    BranchGrow,
    LeafGrow,
    LeafFall,
    Pruning,
    Shaping
}