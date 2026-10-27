using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ── Data model ────────────────────────────────────────────────────────────────

public enum SpeedRuleTrigger
{
    Month, Season,
    MoistureBelow, HealthBelow, NutrientBelow,
    FungalLoadAbove, WeedCountAbove,
    WireSetGold, TreeInDanger,
}

[Serializable]
public class SpeedRule
{
    public bool             enabled = true;
    public SpeedRuleTrigger trigger;
    public float            triggerParam;       // month int / Season cast / threshold
    public GameManager.SpeedMode targetSpeed;
    public bool             idleResumeEnabled;
    public float            idleResumeRealSeconds;
    public float            idleResumeInGameDays;

    // Runtime edge-detection state (2026-10-13). Rules are EDGE-triggered: they fire once when
    // their condition becomes true, then leave the player free to change speed until the
    // condition goes false and becomes true again (next January, the next gold wire, ...).
    [NonSerialized] public bool  initialized;        // first evaluation only records the state
    [NonSerialized] public bool  conditionWasTrue;
    [NonSerialized] public float lastFiredRealTime = -1f;
}

[Serializable]
public class PlayMode
{
    public string name;
    public bool   isBuiltIn;
    public GameManager.SpeedMode defaultSpeed;
    public bool   autoWater;
    public bool   autoFertilize;
    public bool   autoHerbicide;
    public bool   autoFungicide;
    // Whether the AutoStyler may shape the tree (trim / pinch / wire / repot) in this mode.
    // PlayModeManager owns AutoStyler.autoStyleEnabled from this flag, except while Quick Start is
    // generating. Added 2026-10-27: before this, the AutoStyler ran in every mode whenever its
    // component was enabled, including Active Play.
    public bool   autoStyle;
    public bool   idleOrbit;
    public float  idleOrbitDelaySecs;
    public List<SpeedRule> rules = new List<SpeedRule>();
}

// ── Manager ───────────────────────────────────────────────────────────────────

public class PlayModeManager : MonoBehaviour
{
    public static PlayModeManager Instance;

    [SerializeField] TreeSkeleton skeleton;

    public List<PlayMode> modes = new List<PlayMode>();
    public int activeModeIndex;

    public PlayMode ActiveMode =>
        modes.Count > 0 ? modes[Mathf.Clamp(activeModeIndex, 0, modes.Count - 1)] : null;

    public bool IdleOrbitActive { get; private set; }

    float lastInputRealTime;
    float lastInputInGameDay;
    bool  wireGoldFired;
    bool  defaultApplied;   // the active mode's starting speed has been applied

    // Auto-creates the manager if it wasn't placed in the scene.
    //
    // RuntimeInitializeOnLoadMethod runs ONCE per play session, against whichever scene loads
    // first — since R1 that is the main menu, which then got a stray PlayModeManager. This now
    // only creates one in a gameplay scene (one with a GameManager), and re-checks on every scene
    // load. Game001 normally has its own (on the same GameObject as GameManager), so in practice
    // the scene-load path is a fallback.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        CreateIfNeeded();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                              UnityEngine.SceneManagement.LoadSceneMode mode) => CreateIfNeeded();

    static void CreateIfNeeded()
    {
        if (Instance != null) return;
        if (FindAnyObjectByType<PlayModeManager>() != null) return;
        if (FindAnyObjectByType<GameManager>() == null) return;   // not a gameplay scene
        var go = new GameObject("PlayModeManager [auto]");
        go.AddComponent<PlayModeManager>();
        Debug.Log("[PlayMode] Auto-created PlayModeManager — add it to your scene to avoid this.");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LoadOrCreateDefaultModes();
    }

    void OnEnable()
    {
        if (skeleton != null) skeleton.OnWireSetGold += OnWireSetGold;
    }

    void OnDisable()
    {
        if (skeleton != null) skeleton.OnWireSetGold -= OnWireSetGold;
    }

    void OnWireSetGold() => wireGoldFired = true;

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        // Presence = ANY mouse activity. This used to count only presses/scroll/keys,
        // so mouse MOVEMENT and HELD buttons didn't register — hold an orbit drag (or
        // just inspect the tree) past idleOrbitDelaySecs and the idle orbit hijacked
        // the camera mid-drag, ignoring the mouse until the next click. That was the
        // persistent "weird and hard to orbit" (2026-07-04). The idle orbit is a
        // screensaver: it must only engage when the hand is truly off the mouse.
        bool anyInput =
            (Mouse.current != null &&
             (Mouse.current.leftButton.isPressed   ||
              Mouse.current.rightButton.isPressed  ||
              Mouse.current.middleButton.isPressed ||
              Mouse.current.delta.ReadValue().sqrMagnitude > 0.5f ||
              Mouse.current.scroll.ReadValue().sqrMagnitude > 0.01f)) ||
            (Keyboard.current != null && Keyboard.current.anyKey.isPressed);

        if (anyInput)
        {
            lastInputRealTime  = Time.unscaledTime;
            lastInputInGameDay = CurrentInGameDay();
            if (IdleOrbitActive) IdleOrbitActive = false;
        }

        var mode = ActiveMode;
        if (mode == null) return;

        // Don't evaluate rules while calendar is open or game is paused
        var state = GameManager.Instance?.state ?? GameState.Idle;
        if (state == GameState.CalendarOpen || state == GameState.GamePause) return;

        float realNow    = Time.unscaledTime;
        float inGameNow  = CurrentInGameDay();

        var gm = GameManager.Instance;
        if (gm == null) return;

        // The mode's speed is a STARTING speed, applied when the mode becomes active — not
        // re-asserted every frame. Re-asserting it every frame is what used to override the
        // player's own speed choice, and what undid every rule one frame after it fired.
        if (!defaultApplied)
        {
            defaultApplied = true;
            if (GameManager.CurrentSpeed != mode.defaultSpeed) gm.SetSpeedMode(mode.defaultSpeed);
        }

        // Speed rules are EDGE-triggered (2026-10-13). A rule fires once at the moment its
        // condition becomes true — January 1st arrives, a wire turns gold — and sets the speed.
        // After that the player can change speed freely; the rule stays quiet until its condition
        // goes false and becomes true again.
        //
        // Previously every rule was re-applied every frame while its condition held, so
        // "Month = January → Slow" locked the game at Slow for the whole month, and the
        // one-frame "wire set gold" pulse slowed the game for exactly one frame. A `suppressed`
        // flag existed for player overrides but nothing ever set it.
        //
        // Idle resume keeps its meaning: if the condition is still true and the player has
        // interacted since the rule last fired and then gone idle for the configured time, the
        // rule re-applies once. When several rules fire on the same frame, the slowest wins.
        bool fired = false;
        var  target = GameManager.SpeedMode.Fast;
        foreach (var rule in mode.rules)
        {
            if (!rule.enabled) { rule.initialized = false; continue; }

            bool now = IsTriggerActive(rule);

            // First look at a rule (load, mode switch, new or re-enabled rule): record its state
            // without firing, so a condition that is already true doesn't count as "becoming" true.
            if (!rule.initialized)
            {
                rule.initialized      = true;
                rule.conditionWasTrue = now;
                continue;
            }

            bool risingEdge = now && !rule.conditionWasTrue;
            bool idleRefire = now && !risingEdge && rule.idleResumeEnabled &&
                              lastInputRealTime > rule.lastFiredRealTime &&
                              IdleLongEnough(rule, realNow, inGameNow);

            if (risingEdge || idleRefire)
            {
                rule.lastFiredRealTime = realNow;
                if (!fired || (int)rule.targetSpeed < (int)target) target = rule.targetSpeed;
                fired = true;
            }

            rule.conditionWasTrue = now;
        }

        if (fired && GameManager.CurrentSpeed != target)
            gm.SetSpeedMode(target);

        // Sync auto-care flags to skeleton; late-bind skeleton reference if not set in Inspector
        var sk = skeleton != null ? skeleton : FindAnyObjectByType<TreeSkeleton>();
        if (sk != null)
        {
            if (skeleton == null) { skeleton = sk; skeleton.OnWireSetGold += OnWireSetGold; }
            sk.autoWaterEnabled      = mode.autoWater;
            sk.autoFertilizeEnabled  = mode.autoFertilize;
            sk.autoHerbicideEnabled  = mode.autoHerbicide;
            sk.autoFungicideEnabled  = mode.autoFungicide;
        }

        // Auto-style follows the play mode. Quick Start turns the styler on for its fast-forward
        // and never turned it back off, so a Quick-Started tree kept being styled afterwards;
        // leave it alone only while Quick Start is actually generating.
        var styler = AutoStyler.Instance;
        bool quickStarting = QuickStartManager.Instance != null && QuickStartManager.Instance.IsGenerating;
        if (styler != null && !quickStarting && styler.autoStyleEnabled != mode.autoStyle)
            styler.autoStyleEnabled = mode.autoStyle;

        // Idle orbit
        if (mode.idleOrbit && mode.idleOrbitDelaySecs > 0)
            IdleOrbitActive = (realNow - lastInputRealTime) >= mode.idleOrbitDelaySecs;
        else
            IdleOrbitActive = false;

        // Clear one-shot events consumed this frame
        wireGoldFired = false;
    }

    // ── Trigger evaluation ────────────────────────────────────────────────────

    bool IdleLongEnough(SpeedRule rule, float realNow, float inGameNow)
    {
        bool idleReal   = rule.idleResumeRealSeconds > 0 &&
                          (realNow - lastInputRealTime) >= rule.idleResumeRealSeconds;
        bool idleIngame = rule.idleResumeInGameDays  > 0 &&
                          (inGameNow - lastInputInGameDay) >= rule.idleResumeInGameDays;
        return idleReal || idleIngame;
    }

    bool IsTriggerActive(SpeedRule rule)
    {
        var sk = skeleton != null ? skeleton : FindAnyObjectByType<TreeSkeleton>();

        switch (rule.trigger)
        {
            case SpeedRuleTrigger.Month:
                return GameManager.month == Mathf.RoundToInt(rule.triggerParam);

            case SpeedRuleTrigger.Season:
                return GameManager.IsInSeason((Season)(int)rule.triggerParam, GameManager.month);

            case SpeedRuleTrigger.MoistureBelow:
                return sk != null && sk.soilMoisture < rule.triggerParam;

            case SpeedRuleTrigger.HealthBelow:
            {
                if (sk == null) return false;
                float sum = 0f; int n = 0;
                foreach (var node in sk.allNodes)
                    if (!node.isRoot) { sum += node.health; n++; }
                return n > 0 && (sum / n) < rule.triggerParam;
            }

            case SpeedRuleTrigger.NutrientBelow:
                return sk != null && sk.nutrientReserve < rule.triggerParam;

            case SpeedRuleTrigger.FungalLoadAbove:
            {
                if (sk == null) return false;
                float max = 0f;
                foreach (var node in sk.allNodes) max = Mathf.Max(max, node.fungalLoad);
                return max > rule.triggerParam;
            }

            case SpeedRuleTrigger.WeedCountAbove:
                return WeedManager.Instance != null &&
                       WeedManager.Instance.ActiveWeedCount > (int)rule.triggerParam;

            case SpeedRuleTrigger.WireSetGold:
                return wireGoldFired;

            case SpeedRuleTrigger.TreeInDanger:
                return sk != null && sk.treeInDanger;
        }
        return false;
    }

    // ── Mode management ───────────────────────────────────────────────────────

    /// <summary>Stops the active mode's starting speed from being applied (the player just left a
    /// menu, which sets Slow — see <see cref="GameManager.SlowAfterMenu"/>).</summary>
    public void MarkStartingSpeedApplied() => defaultApplied = true;

    public void SetActiveMode(int index)
    {
        activeModeIndex = Mathf.Clamp(index, 0, modes.Count - 1);
        wireGoldFired   = false;

        // A newly chosen mode applies its starting speed, and its rules start from the current
        // conditions without firing.
        defaultApplied = false;
        foreach (var r in modes[activeModeIndex].rules) r.initialized = false;

        SaveModes();
    }

    public void ResetBuiltInModes()
    {
        var customs = modes.FindAll(m => !m.isBuiltIn);
        CreateDefaultModes();
        modes.AddRange(customs);
        activeModeIndex = Mathf.Clamp(activeModeIndex, 0, modes.Count - 1);
        SaveModes();
    }

    /// <summary>Copies the fields a player can edit in the Play Modes tab.</summary>
    static void CopyPlayerEdits(PlayMode from, PlayMode to)
    {
        to.defaultSpeed       = from.defaultSpeed;
        to.autoWater          = from.autoWater;
        to.autoFertilize      = from.autoFertilize;
        to.autoHerbicide      = from.autoHerbicide;
        to.autoFungicide      = from.autoFungicide;
        to.autoStyle          = from.autoStyle;
        to.idleOrbit          = from.idleOrbit;
        to.idleOrbitDelaySecs = from.idleOrbitDelaySecs;
        if (from.rules != null) to.rules = from.rules;
    }

    public void SaveModes()
    {
        PlayerPrefs.SetString("playModes", JsonUtility.ToJson(new PlayModeList { modes = modes }));
        PlayerPrefs.SetInt("activeModeIndex", activeModeIndex);
        PlayerPrefs.Save();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    // Bump this when CreateDefaultModes() changes so saved prefs auto-reset.
    const int DEFAULTS_VERSION = 5;   // 5: autoStyle per mode; player edits to built-ins persist

    void LoadOrCreateDefaultModes()
    {
        // Always rebuild built-in modes from code so defaults are never stale.
        // Custom (user-created) modes are preserved from saved prefs.
        CreateDefaultModes();

        string json = PlayerPrefs.GetString("playModes", "");
        int savedVersion = PlayerPrefs.GetInt("playModesVersion", 0);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var saved = JsonUtility.FromJson<PlayModeList>(json);
                if (saved?.modes != null)
                {
                    foreach (var m in saved.modes)
                    {
                        if (!m.isBuiltIn) { modes.Add(m); continue; }

                        // Built-in modes are defined in code, but the PLAYER's edits to them
                        // (unticking auto-water in Active Play, toggling a rule) are kept, as long
                        // as the code defaults haven't changed since they were saved. Previously
                        // every built-in was rebuilt from code on each launch, so those edits were
                        // silently thrown away next session.
                        if (savedVersion != DEFAULTS_VERSION) continue;
                        var live = modes.Find(x => x.isBuiltIn && x.name == m.name);
                        if (live != null) CopyPlayerEdits(m, live);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayMode] Failed to load saved custom modes: {e.Message}.");
            }
        }

        activeModeIndex = Mathf.Clamp(
            PlayerPrefs.GetInt("activeModeIndex", 0), 0, modes.Count - 1);
        PlayerPrefs.SetInt("playModesVersion", DEFAULTS_VERSION);
        SaveModes();
    }

    void CreateDefaultModes()
    {
        modes.Clear();

        modes.Add(new PlayMode
        {
            name = "Screensaver", isBuiltIn = true, autoStyle = true,
            defaultSpeed = GameManager.SpeedMode.Fast,
            autoWater = true, autoFertilize = true, autoHerbicide = true, autoFungicide = true,
            idleOrbit = true, idleOrbitDelaySecs = 30f,
            rules = new List<SpeedRule>
            {
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.Month,         triggerParam=1,     targetSpeed=GameManager.SpeedMode.Slow, idleResumeEnabled=true, idleResumeRealSeconds=20 },
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.TreeInDanger,  triggerParam=0,     targetSpeed=GameManager.SpeedMode.Slow, idleResumeEnabled=true, idleResumeRealSeconds=20 },
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.MoistureBelow, triggerParam=0.3f,  targetSpeed=GameManager.SpeedMode.Slow, idleResumeEnabled=true, idleResumeRealSeconds=20 },
            }
        });

        modes.Add(new PlayMode
        {
            name = "Active Play", isBuiltIn = true, autoStyle = false,
            defaultSpeed = GameManager.SpeedMode.Med,
            autoWater = true, autoFertilize = true, autoHerbicide = true, autoFungicide = true,
            idleOrbit = false, idleOrbitDelaySecs = 0f,
            rules = new List<SpeedRule>
            {
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.Month,       triggerParam=1,                       targetSpeed=GameManager.SpeedMode.Slow },
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.WireSetGold, triggerParam=0,                       targetSpeed=GameManager.SpeedMode.Slow, idleResumeEnabled=true, idleResumeInGameDays=5 },
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.Season,      triggerParam=(float)Season.Spring,    targetSpeed=GameManager.SpeedMode.Slow },
            }
        });

        modes.Add(new PlayMode
        {
            name = "Hands-Off", isBuiltIn = true, autoStyle = true,
            defaultSpeed = GameManager.SpeedMode.Fast,
            autoWater = true, autoFertilize = true, autoHerbicide = true, autoFungicide = true,
            idleOrbit = false, idleOrbitDelaySecs = 0f,
            rules = new List<SpeedRule>
            {
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.TreeInDanger, triggerParam=0, targetSpeed=GameManager.SpeedMode.Slow, idleResumeEnabled=true, idleResumeRealSeconds=60 },
            }
        });

        modes.Add(new PlayMode
        {
            name = "Focused", isBuiltIn = true, autoStyle = false,
            defaultSpeed = GameManager.SpeedMode.Slow,
            autoWater = true, autoFertilize = true, autoHerbicide = false, autoFungicide = false,
            idleOrbit = false, idleOrbitDelaySecs = 0f,
            rules = new List<SpeedRule>()
        });

        activeModeIndex = 0;
    }

    static float CurrentInGameDay() => GameManager.dayOfYear + GameManager.year * 366f;

    [Serializable]
    class PlayModeList { public List<PlayMode> modes; }
}
