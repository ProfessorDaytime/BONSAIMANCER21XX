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
    public static bool canWire = false;
    public static bool canRemoveWire = false;

    /// <summary>
    /// How fast the tree grows this month (0 = dormant, 1 = peak spring growth).
    /// Drives TreeSkeleton.Update() — multiply all growth speeds by this value.
    /// </summary>
    public static float SeasonalGrowthRate
    {
        get
        {
            switch (month)
            {
                case 3:  return 0.3f;   // March   — buds break, slow start
                case 4:  return 1.0f;   // April   — peak growth
                case 5:  return 1.0f;   // May     — peak growth
                case 6:  return 0.6f;   // June    — slowing
                case 7:  return 0.5f;   // July
                case 8:  return 0.4f;   // August  — winding down
                default: return 0.0f;   // Sep–Feb — dormant
            }
        }
    }

    /// <summary>
    /// Leaf hue (0 = green, 1 = full red). Drives Leaf material color each frame.
    /// </summary>
    public static float LeafHue
    {
        get
        {
            switch (month)
            {
                case 9:  return Mathf.Clamp01((day - 1) / 28f) * 0.3f;          // Sep: 0 → 0.3
                case 10: return 0.3f + Mathf.Clamp01((day - 1) / 28f) * 0.7f;   // Oct: 0.3 → 1.0
                case 11: return 1.0f;
                default: return 0.0f;
            }
        }
    }

    [SerializeField]
    GameObject cityDay;
    [SerializeField]
    GameObject skyLight;

    //Time Stuff
    public static float TIMESCALE = 200f;
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
        day = 1;
        year = 2123;
        hour = 12f;

        rend = cityDay.GetComponent<SpriteRenderer>();
        sky = skyLight.GetComponent<Light>();


    }
    
    void Update(){
        if (Input.GetKey(KeyCode.D)) TIMESCALE = Mathf.Min(TIMESCALE + 50f * Time.deltaTime, 400f);
        if (Input.GetKey(KeyCode.A)) TIMESCALE = Mathf.Max(TIMESCALE - 50f * Time.deltaTime, 1f);

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

        // Debug.Log(state);
        
    }

   

    void CalculateTime(){
        hour += Time.deltaTime * TIMESCALE;

        

        
        
        // if(hour >= 6f && hour < 20f){
        //     rend.color = new Color (1,1,1,1);
        //     sky.intensity = 1f;
        // }
        // else if(hour < 5f || hour >= 21f){
        //     rend.color = new Color (1,1,1,0);
        //     sky.intensity = 0f;
        // }
        // else if (hour > 5f && hour < 6f){
        //     rend.color = new Color (1,1,1, hour - 5f);
        //     sky.intensity = hour - 5f;
        // }
        // else if (hour > 20f && hour < 21f){
        //     rend.color = new Color (1,1,1,1 - (hour - 20));
        //     sky.intensity = 1 - (hour - 20);
        // }
        
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

    public void TextCallFunction(){
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
                // Skip winter — jump to February of next year.
                // October (LeafFall) has already played; no reason to sit through Nov–Jan.
                month     = 2;
                year++;
                monthName = "February";
                day       = 1;
                TextCallFunction();
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
            case GameState.Wiring:
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
    Shaping,
    Wiring
}