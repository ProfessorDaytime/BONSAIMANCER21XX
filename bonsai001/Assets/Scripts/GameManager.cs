using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameState state;

    public static event Action<GameState> OnGameStateChanged;

    public static int waterings = -1;

    public static int branches = 0;
    public static int newLeaves = 0;
    public static bool canLeaf = false;
    public static bool canTrim = false;

    [SerializeField]
    GameObject cityDay;
    [SerializeField]
    GameObject skyLight;

    //Time Stuff
    public static float TIMESCALE = 5f;
    // Text hourText;
    // Text dayText;
    // Text monthText;
    // Text yearText;
    string monthName = "April";

    public static int day, month, year;
    public static float hour;

    public Text calendar;

    SpriteRenderer rend;
    Light sky;



    void Awake(){
        Instance = this;
        UpdateGameState(GameState.TipPause);
    }

    void Start(){


        month = 4;
        day = 26;
        year = 2123;
        hour = 12f;

        rend = cityDay.GetComponent<SpriteRenderer>();
        sky = skyLight.GetComponent<Light>();


    }
    
    void Update(){
        if(state == GameState.BranchGrow || state == GameState.LeafGrow || state == GameState.LeafFall || state == GameState.TimeGo){
            CalculateTime();

            if(state == GameState.BranchGrow){
                if(branches % 3 == 0 && branches != 0 && canLeaf){
                    
                    Debug.Log("Can Leaf");
                    // UpdateGameState(GameState.LeafGrow);
                    // canLeaf = false;

                }
            }

        }

        Debug.Log(state);
        
    }

   

    void CalculateTime(){
        hour += Time.deltaTime * TIMESCALE;

        

        
        
        if(hour >= 6f && hour < 20f){
            rend.color = new Color (1,1,1,1);
            sky.intensity = 1f;
        }
        else if(hour < 5f || hour >= 21f){
            rend.color = new Color (1,1,1,0);
            sky.intensity = 0f;
        }
        else if (hour > 5f && hour < 6f){
            Debug.Log("Between 5 and 6 am");
            rend.color = new Color (1,1,1, hour - 5f);
            sky.intensity = hour - 5f;
        }
        else if (hour > 20f && hour < 21f){
            Debug.Log("Between 8 and 9 pm");
            rend.color = new Color (1,1,1,1 - (hour - 20));
            sky.intensity = 1 - (hour - 20);
        }
        
        // if(hour > 12f && hour < 13f){
        //     // sky.intensity = hour - 12 + 1;
        // } 
        // else if (hour > 13f && hour < 14f){
        //     // sky.intensity = 2 - hour - 13;
        // }



        if(hour >= 24f){
            day++;
            hour = 0f;
            TextCallFunction();

        }
        if(day > 28){

            month++;
            day = 1;
            SetMonthText();
            TextCallFunction();

        }
        if(month > 12){

            month = 1;
            year++;
            TextCallFunction();

        }



    }

    void TextCallFunction(){
        // dayText.text = "" + day;
        // hourText = "" + hour;
        // yearText = "" + year;
    
        

        calendar.text = monthName + " " + day + ", " + year;
    }

    void SetMonthText(){
        switch(month){
            case 1:
                monthName = "January";
                break;
            case 2:
                monthName = "February";
                break;
            case 3:
                monthName = "March";
                break;
            case 4:
                monthName = "April";
                UpdateGameState(GameState.BranchGrow);
                AudioManager.Instance.PlayMusic("SpringSong");
                break;
            case 5:
                monthName = "May";
                break;
            case 6:
                monthName = "June";
                break;
            case 7:
                monthName = "July";
                break;
            case 8:
                monthName = "August";
                break;
            case 9:
                monthName = "September";
                UpdateGameState(GameState.TimeGo);
                break;
            case 10:
                monthName = "October";
                UpdateGameState(GameState.LeafFall);
                AudioManager.Instance.PlayMusic("WinterSong");
                break;
            case 11:
                monthName = "November";
                break;
            case 12:
                monthName = "December";
                break;
        }
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
            case GameState.TimeGo:
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
    TimeGo,
    LeafFall,
    Pruning,
    Shaping
}