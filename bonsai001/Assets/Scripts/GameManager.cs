using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameState state;

    public static event Action<GameState> OnGameStateChanged;
    /// <summary>Fired each time the calendar month changes. Subscribers receive the new month (1–12).</summary>
    public static event Action<int> OnMonthChanged;

    public static int waterings = -1;

    public static int branches = 0;
    public static int newLeaves = 0;
    public static bool quickWinter   = true;

    public static bool canLeaf       = false;
    public static bool canTrim       = false;
    public static bool canWire       = false;
    public static bool canRemoveWire = false;
    public static bool canPaste      = false;
    public static bool canRootWork   = false;
    public static bool canRootRake   = false;
    public static bool canAirLayer      = false;
    public static bool canPinch         = false;
    public static bool canDefoliate     = false;
    public static bool canGraft         = false;
    public static bool canPromote       = false;
    public static float selectionRadius = 0.5f;

    // Saved state to restore when exiting RootPrune mode.
    static GameState preRootPruneState = GameState.Idle;

    // Saved state to restore when unpausing.
    static GameState prePauseState = GameState.Idle;

    // Saved state to restore when dismissing a tooltip.
    static GameState preTipPauseState = GameState.Idle;

    /// <summary>Title of the currently-showing first-use tooltip. Empty when no tooltip is active.</summary>
    public static string TooltipTitle { get; private set; } = "";
    /// <summary>Body text of the currently-showing first-use tooltip.</summary>
    public static string TooltipBody  { get; private set; } = "";

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
                case 6:  return 0.4f;   // June    — first hardening, noticeably slower
                case 7:  return 0.1f;   // July    — nearly stopped
                default: return 0.0f;   // Aug–Feb — dormant
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
    public static float TIMESCALE = 10f;   // set from TIMESCALE_MED after PlayerPrefs load
    // Player-configurable speed ratios — loaded from PlayerPrefs, defaulting to these values.
    public static float TIMESCALE_FAST = 200f;
    public static float TIMESCALE_MED  = 10f;
    public static float TIMESCALE_SLOW = 0.5f;
    /// Slowest allowed timescale: 1 game minute per real second (TIMESCALE = 1/60 game-hrs/sec).
    public const float TIMESCALE_MIN = 1f / 60f;

    /// <summary>Persist the three speed ratios to PlayerPrefs.</summary>
    public static void SaveTimescalePrefs()
    {
        PlayerPrefs.SetFloat("ts_slow", TIMESCALE_SLOW);
        PlayerPrefs.SetFloat("ts_med",  TIMESCALE_MED);
        PlayerPrefs.SetFloat("ts_fast", TIMESCALE_FAST);
        PlayerPrefs.Save();
    }

    static void LoadTimescalePrefs()
    {
        TIMESCALE_SLOW = PlayerPrefs.GetFloat("ts_slow", 0.5f);
        TIMESCALE_MED  = PlayerPrefs.GetFloat("ts_med",  10f);
        TIMESCALE_FAST = PlayerPrefs.GetFloat("ts_fast", 200f);
        // Enforce ordering
        TIMESCALE_SLOW = Mathf.Clamp(TIMESCALE_SLOW, TIMESCALE_MIN, TIMESCALE_MED - 0.1f);
        TIMESCALE_MED  = Mathf.Clamp(TIMESCALE_MED,  TIMESCALE_SLOW + 0.1f, TIMESCALE_FAST - 1f);
        TIMESCALE_FAST = Mathf.Clamp(TIMESCALE_FAST, TIMESCALE_MED  + 1f,   500f);
        TIMESCALE = TIMESCALE_MED;
    }

    public enum SpeedMode { Slow, Med, Fast }
    public static SpeedMode CurrentSpeed { get; private set; } = SpeedMode.Med;
    /// Back-compat: true when Slow mode is active.
    public static bool IsSlowSpeed => CurrentSpeed == SpeedMode.Slow;

    string monthName = "March";

    public static int day, month, year;
    public static float hour;

    // ── Real calendar ─────────────────────────────────────────────────────────
    static readonly int[] DaysInMonthTable = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    public static bool IsLeapYear(int y) => (y % 4 == 0) && (y % 100 != 0 || y % 400 == 0);
    public static int DaysInMonth(int m, int y) => (m == 2 && IsLeapYear(y)) ? 29 : DaysInMonthTable[m - 1];

    /// <summary>Day of year using real month lengths (1–365/366).</summary>
    public static int dayOfYear
    {
        get
        {
            int total = 0;
            for (int m = 1; m < month; m++) total += DaysInMonth(m, year);
            return total + day;
        }
    }

    // ── Scheduled care events ─────────────────────────────────────────────────
    public static List<ScheduledEvent> schedule = new List<ScheduledEvent>();
    static int lastCheckedDay = -1;

    static int TimeOfDayHour(TimeOfDay t) => t switch
    {
        TimeOfDay.Morning => 7,
        TimeOfDay.Midday  => 12,
        TimeOfDay.Night   => 21,
        _                 => 7,
    };

    public static bool IsInSeason(Season s, int m) => s switch
    {
        Season.Spring  => m >= 3 && m <= 5,
        Season.Summer  => m >= 6 && m <= 8,
        Season.Autumn  => m >= 9 && m <= 10,
        Season.Winter  => m >= 11 || m <= 2,
        _              => true,
    };

    static bool EventFiresToday(ScheduledEvent ev, int m, int d, int y, int doy)
    {
        if (ev.repeat != RepeatMode.Once && ev.season != Season.AllYear && !IsInSeason(ev.season, m))
            return false;

        int originDoy = 0;
        for (int i = 1; i < ev.month; i++) originDoy += DaysInMonth(i, y);
        originDoy += ev.day;

        switch (ev.repeat)
        {
            case RepeatMode.Once:
                return ev.month == m && ev.day == d;
            case RepeatMode.EveryNDays:
            {
                int diff = doy - originDoy;
                int interval = ev.repeatInterval;
                return interval > 0 && ((diff % interval + interval) % interval) == 0;
            }
            case RepeatMode.EveryNWeeks:
            {
                int diff = doy - originDoy;
                int interval = ev.repeatInterval * 7;
                return interval > 0 && ((diff % interval + interval) % interval) == 0;
            }
            default: return false;
        }
    }

    /// <summary>Returns true if the given event fires on the specified calendar date.</summary>
    public static bool EventFiresOnDate(ScheduledEvent ev, int m, int d, int y)
    {
        if (!ev.enabled) return false;
        int doy = 0;
        for (int i = 1; i < m; i++) doy += DaysInMonth(i, y);
        doy += d;
        return EventFiresToday(ev, m, d, y, doy);
    }

    void CheckScheduledEvents()
    {
        if (lastCheckedDay == day) return;
        lastCheckedDay = day;

        int doy = dayOfYear;
        var sk = FindAnyObjectByType<TreeSkeleton>();
        foreach (var ev in schedule)
        {
            if (!ev.enabled) continue;
            if (!EventFiresToday(ev, month, day, year, doy)) continue;
            switch (ev.type)
            {
                case ScheduledEventType.Water:      sk?.Water(); break;
                case ScheduledEventType.Fertilize:  sk?.Fertilize(); break;
            }
        }
    }

    static int lastCalendarMinute = -1;

    public Text calendar;

    SpriteRenderer rend;
    Light sky;



    void Awake(){
        Instance = this;
        month = 2;
        day   = 27;
        year  = 2026;
        hour  = 12f;
        LoadTimescalePrefs();
        QualitySettings.antiAliasing = PlayerPrefs.GetInt("antiAliasing", 1) > 0 ? 4 : 0;
        // If any named saves exist, show the load menu; otherwise go straight to species pick.
        UpdateGameState(SaveManager.HasAnySave() ? GameState.LoadMenu : GameState.SpeciesSelect);
    }

    void Start(){
        rend = cityDay.GetComponent<SpriteRenderer>();
        sky  = skyLight.GetComponent<Light>();
    }
    
    void Update(){
        if (Keyboard.current != null && Keyboard.current.rightArrowKey.isPressed) TIMESCALE = Mathf.Min(TIMESCALE + 50f * Time.deltaTime, 400f);
        if (Keyboard.current != null && Keyboard.current.leftArrowKey.isPressed)  TIMESCALE = Mathf.Max(TIMESCALE - 50f * Time.deltaTime, TIMESCALE_MIN);

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

        // Refresh calendar whenever the displayed minute changes
        int curMinute = Mathf.FloorToInt((hour % 1f) * 60f);
        if (curMinute != lastCalendarMinute) { lastCalendarMinute = curMinute; TextCallFunction(); }

        

        
        
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



        if (hour >= 24f)
        {
            day++;
            hour = 0f;
            CheckScheduledEvents();
            TextCallFunction();
        }
        if (day > DaysInMonth(month, year))
        {
            month++;
            day = 1;
            SetMonthText();
            TextCallFunction();
        }
        if (month > 12)
        {
            month = 1;
            year++;
            SetMonthText();
            TextCallFunction();
        }



    }

    public void TextCallFunction(){
        int h = Mathf.FloorToInt(hour) % 24;
        int m = Mathf.FloorToInt((hour % 1f) * 60f);
        string toolName = ActiveToolLabel();
        calendar.text = string.IsNullOrEmpty(toolName)
            ? $"{monthName} {day}, {year}  {h:D2}:{m:D2}"
            : $"{monthName} {day}, {year}  {h:D2}:{m:D2}  ·  {toolName}";
    }

    static string ActiveToolLabel()
    {
        var tool = ToolManager.Instance?.ActiveTool ?? ToolType.None;
        return tool switch
        {
            ToolType.Shears        => "Shears",
            ToolType.SmallClippers => "Small Clippers",
            ToolType.BigClippers   => "Big Clippers",
            ToolType.Saw           => "Saw",
            ToolType.Wire          => "Wire",
            ToolType.RemoveWire    => "Remove Wire",
            ToolType.Paste         => "Paste",
            ToolType.AirLayer      => "Air Layer",
            ToolType.Pinch         => "Pinch",
            ToolType.Defoliate     => "Defoliate",
            ToolType.Graft         => "Graft",
            _                      => string.Empty,
        };
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

        OnMonthChanged?.Invoke(month);
    }

    public void UpdateGameState(GameState newState){
        if (state == newState) return;   // already in this state; don't re-fire listeners

        state = newState;

        // Root interaction is active in RootPrune and its sub-states.
        canRootWork = (newState == GameState.RootPrune  ||
                       newState == GameState.RockPlace  ||
                       newState == GameState.TreeOrient);

        // Rake mode is its own interaction block (handled separately in TreeInteraction).
        canRootRake = (newState == GameState.RootRake);

        Debug.Log("GAME STATE: " + newState);

        switch(newState){
            case GameState.Menu:
                break;
            case GameState.SpeciesSelect:
                break;
            case GameState.TipPause:
                break;
            case GameState.GamePause:
                break;
            case GameState.Idle:
                Time.timeScale = 1f;
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
            case GameState.RootRake:
                // Rake mini-game — tree stays lifted; RootRakeManager drives the interaction.
                break;
            case GameState.RockPlace:
                break;
            case GameState.TreeOrient:
                break;
            case GameState.TreeDead:
                Time.timeScale = 0f;   // freeze — game over
                break;
            case GameState.AirLayerSever:
                Time.timeScale = 0f;   // freeze while player decides
                break;
            case GameState.LoadMenu:
                Time.timeScale = 0f;
                break;
            case GameState.CalendarOpen:
                Time.timeScale = 0f;   // freeze while calendar is open
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
        Debug.Log("[Root] Entering RootPrune/Repot mode");
        WeedManager.Instance?.PullAllWeeds();
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
        s == GameState.TreeOrient ||
        s == GameState.RootRake;

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
            Debug.Log("[GM] Firing OnRockOrientConfirmed frame=" + Time.frameCount + " subscribers=" + (OnRockOrientConfirmed == null ? 0 : OnRockOrientConfirmed.GetInvocationList().Length));
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

    /// <summary>
    /// Toggles the game pause. Freezes Time.timeScale (stops all deltaTime-based
    /// systems) and saves/restores the active game state.
    /// </summary>
    /// <summary>Cycles Slow → Med → Fast → Slow.</summary>
    public void ToggleSpeed()
    {
        SetSpeedMode(CurrentSpeed switch
        {
            SpeedMode.Slow => SpeedMode.Med,
            SpeedMode.Med  => SpeedMode.Fast,
            _              => SpeedMode.Slow,
        });
    }

    /// <summary>Set speed mode explicitly. Back-compat bool overload: true = Slow.</summary>
    public void SetSpeed(bool slow) => SetSpeedMode(slow ? SpeedMode.Slow : SpeedMode.Fast);

    public void SetSpeedMode(SpeedMode mode)
    {
        CurrentSpeed = mode;
        TIMESCALE    = mode switch
        {
            SpeedMode.Slow => TIMESCALE_SLOW,
            SpeedMode.Med  => TIMESCALE_MED,
            _              => TIMESCALE_FAST,
        };
        Debug.Log($"[Time] Speed → {mode} TIMESCALE={TIMESCALE} | year={year} month={month}");
    }

    public void TogglePause()
    {
        if (state == GameState.GamePause)
        {
            Time.timeScale = 1f;
            UpdateGameState(prePauseState);
        }
        else
        {
            prePauseState  = state;
            Time.timeScale = 0f;
            UpdateGameState(GameState.GamePause);
        }
    }

    /// <summary>
    /// Returns the correct time-ticking GameState for the given calendar month.
    /// Call this after loading a save to restore the right state instead of Idle.
    /// </summary>
    public GameState StateForMonth(int m)
    {
        if (m >= 3 && m <= 8) return GameState.BranchGrow;
        if (m == 10)          return GameState.LeafFall;
        return GameState.TimeGo;   // Sep, Nov, Dec, Jan, Feb
    }

    /// <summary>
    /// Show a first-use informational tooltip overlay. Enters TipPause so the HUD is blocked.
    /// The caller can still select tools before calling this — the tooltip is purely advisory.
    /// </summary>
    public void ShowTooltip(string title, string body)
    {
        if (state == GameState.TipPause) return;   // already showing something
        preTipPauseState = state;
        TooltipTitle     = title;
        TooltipBody      = body;
        UpdateGameState(GameState.TipPause);
    }

    /// <summary>
    /// Dismiss the current tooltip and restore the state that was active before it appeared.
    /// Use this for the first-use tooltip dismiss button. The existing ToolTip.StateIdle()
    /// continues to handle the initial species-select tip (goes to Idle).
    /// </summary>
    public void ExitTipPause()
    {
        if (state != GameState.TipPause) return;
        TooltipTitle = "";
        TooltipBody  = "";
        GameState restore = (preTipPauseState == GameState.TipPause ||
                             preTipPauseState == GameState.SpeciesSelect)
            ? GameState.Idle : preTipPauseState;
        UpdateGameState(restore);
    }

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
    SpeciesSelect,  // pre-game species picker
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
    RootRake,     // rake mini-game: soil ball visible, player rakes to reveal roots before repot
    RockPlace,    // rock grabbed and being positioned in 3D space
    TreeOrient,   // tree transform being rotated onto the placed rock
    TreeDead,      // tree has died; gameplay halted
    AirLayerSever, // player is confirming an air-layer severance
    LoadMenu,      // browsing saved games at launch or from pause menu
    CalendarOpen,  // calendar overlay open; time paused
}

// ── Scheduled care system ─────────────────────────────────────────────────────

public enum ScheduledEventType { Water, Fertilize }
public enum ScheduledEventAmount { Light, Medium, Heavy }
public enum RepeatMode { Once, EveryNDays, EveryNWeeks }
public enum Season { Spring, Summer, Autumn, Winter, AllYear }
public enum TimeOfDay { Morning, Midday, Night }

[System.Serializable]
public class ScheduledEvent
{
    public string               id;
    public ScheduledEventType   type;
    public ScheduledEventAmount amount;
    public int                  fertType;    // index into fertilizer type list
    public int                  month;       // anchor month (1–12)
    public int                  day;         // anchor day
    public RepeatMode           repeat;
    public int                  repeatInterval;  // N for EveryNDays / EveryNWeeks
    public Season               season;
    public TimeOfDay            timeOfDay;
    public bool                 enabled = true;
}