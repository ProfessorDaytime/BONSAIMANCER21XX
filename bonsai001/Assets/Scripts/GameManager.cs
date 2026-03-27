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
    public static bool quickWinter   = false;

    public static bool canLeaf       = false;
    public static bool canTrim       = false;
    public static bool canWire       = false;
    public static bool canRemoveWire = false;
    public static bool canPaste      = false;
    public static bool canRootWork   = false;
    public static bool canAirLayer     = false;
    public static float selectionRadius = 0f;

    // Saved state to restore when exiting RootPrune mode.
    static GameState preRootPruneState = GameState.Idle;

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
    string monthName = "March";

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


        month = 3;
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

   

    static bool IsWinterMonth(int m) => m == 11 || m == 12 || m == 1 || m == 2;

    void CalculateTime(){
        float winterMult = (quickWinter && IsWinterMonth(month)) ? 2f : 1f;
        hour += Time.deltaTime * TIMESCALE * winterMult;

        

        
        
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
            SetMonthText();
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
        Debug.Log($"[Time] SetMonthText month={month} state={state} year={year}");
        switch(month){
            case 1:
                monthName = "January";
                break;
            case 2:
                monthName = "February";
                break;
            case 3:
                monthName = "March";
                Debug.Log($"[Time] March trigger — state before={state}");
                UpdateGameState(GameState.BranchGrow);
                AudioManager.Instance.PlayMusic("SpringSong");
                Debug.Log($"[Time] March trigger — state after={state}");
                break;
            case 4:
                monthName = "April";
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
                UpdateGameState(GameState.TimeGo);
                break;
            case 12:
                monthName = "December";
                break;
        }
    }

    public void UpdateGameState(GameState newState){
        if (state == newState) return;   // already in this state; don't re-fire listeners

        state = newState;

        // Root interaction is active in RootPrune and its sub-states.
        canRootWork = (newState == GameState.RootPrune  ||
                       newState == GameState.RockPlace  ||
                       newState == GameState.TreeOrient);

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
            case GameState.WireAnimate:
                break;
            case GameState.RootPrune:
                HandleRootPruneState();
                break;
            case GameState.RockPlace:
                break;
            case GameState.TreeOrient:
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

    void HandleRootPruneState()
    {
        Debug.Log("[Root] Entering RootPrune mode");
    }

    /// <summary>
    /// Call from a UI button to enter or exit Root Prune mode.
    /// Saves the current state so it can be restored when exiting.
    /// </summary>
    static bool IsTimeTickingState(GameState s) =>
        s == GameState.BranchGrow || s == GameState.LeafGrow ||
        s == GameState.LeafFall   || s == GameState.TimeGo;

    /// <summary>True while the player is in any root-work sub-state (lift is active).</summary>
    public static bool IsRootLiftActive(GameState s) =>
        s == GameState.RootPrune ||
        s == GameState.RockPlace ||
        s == GameState.TreeOrient;

    /// <summary>
    /// Called by Confirm Orientation button.
    /// If placing rock → move to tree orient step.
    /// If orienting tree → lock in position (no lowering animation) and exit root mode entirely.
    /// </summary>
    public void ConfirmRockOrient()
    {
        if (state == GameState.RockPlace)
        {
            UpdateGameState(GameState.TreeOrient);
        }
        else if (state == GameState.TreeOrient)
        {
            // Fire BEFORE the state change so TreeSkeleton can lock in the new Y
            // before OnGameStateChanged resets liftTarget to 0.
            OnRockOrientConfirmed?.Invoke();

            // Exit root mode directly — skip the RootPrune detour so the tree
            // doesn't lower back to the original ground position.
            GameState restore = IsTimeTickingState(preRootPruneState)
                ? preRootPruneState
                : GameState.LeafFall;
            Debug.Log($"[Rock] ConfirmRockOrient — restoring to {restore} (skipping lower anim)");
            UpdateGameState(restore);
        }
    }

    /// <summary>Fired when the player finalises tree orientation on the rock.</summary>
    public static event System.Action OnRockOrientConfirmed;

    public void ToggleRootPrune()
    {
        if (IsRootLiftActive(state))
        {
            // Exit: restore the state that was active before we entered root mode.
            // If preRootPruneState isn't a time-ticking state, fall back to LeafFall
            // so the calendar continues advancing rather than freezing.
            GameState restore = IsTimeTickingState(preRootPruneState)
                ? preRootPruneState
                : GameState.LeafFall;
            Debug.Log($"[Root] ExitRootPrune — restoring to {restore} (was {preRootPruneState})");
            UpdateGameState(restore);
        }
        else
        {
            preRootPruneState = state;
            Debug.Log($"[Root] EnterRootPrune — saving state={state}");
            UpdateGameState(GameState.RootPrune);
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
    Wiring,
    WireAnimate,  // time frozen; spring-bend animation playing
    RootPrune,    // tree lifted, root mesh visible, root trim/placement active
    RockPlace,    // rock grabbed and being positioned in 3D space
    TreeOrient,   // tree transform being rotated onto the placed rock
}