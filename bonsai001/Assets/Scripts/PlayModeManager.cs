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

    [NonSerialized] public bool suppressed;     // re-armed after idle expires
}

[Serializable]
public class PlayMode
{
    public string name;
    public bool   isBuiltIn;
    public GameManager.SpeedMode defaultSpeed;
    public bool   autoWater;
    public bool   autoFertilize;
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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
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
        bool anyInput =
            (Mouse.current != null &&
             (Mouse.current.leftButton.wasPressedThisFrame  ||
              Mouse.current.rightButton.wasPressedThisFrame ||
              Mouse.current.scroll.ReadValue().sqrMagnitude > 0.01f)) ||
            (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);

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

        // Un-suppress rules whose idle threshold has elapsed
        foreach (var rule in mode.rules)
        {
            if (!rule.enabled || !rule.suppressed || !rule.idleResumeEnabled) continue;
            bool idleReal   = rule.idleResumeRealSeconds > 0 &&
                              (realNow - lastInputRealTime) >= rule.idleResumeRealSeconds;
            bool idleIngame = rule.idleResumeInGameDays  > 0 &&
                              (inGameNow - lastInputInGameDay) >= rule.idleResumeInGameDays;
            if (idleReal || idleIngame) rule.suppressed = false;
        }

        // Lowest speed wins
        var gm = GameManager.Instance;
        if (gm == null) return;

        GameManager.SpeedMode resolved = mode.defaultSpeed;
        foreach (var rule in mode.rules)
        {
            if (!rule.enabled || rule.suppressed) continue;
            if (!IsTriggerActive(rule)) continue;
            if ((int)rule.targetSpeed < (int)resolved)
                resolved = rule.targetSpeed;
        }

        if (GameManager.CurrentSpeed != resolved)
            gm.SetSpeedMode(resolved);

        // Sync auto-care flags to skeleton
        var sk = skeleton != null ? skeleton : FindFirstObjectByType<TreeSkeleton>();
        if (sk != null)
        {
            sk.autoWaterEnabled    = mode.autoWater;
            sk.autoFertilizeEnabled = mode.autoFertilize;
        }

        // Idle orbit
        if (mode.idleOrbit && mode.idleOrbitDelaySecs > 0)
            IdleOrbitActive = (realNow - lastInputRealTime) >= mode.idleOrbitDelaySecs;
        else
            IdleOrbitActive = false;

        // Clear one-shot events consumed this frame
        wireGoldFired = false;
    }

    // ── Trigger evaluation ────────────────────────────────────────────────────

    bool IsTriggerActive(SpeedRule rule)
    {
        var sk = skeleton != null ? skeleton : FindFirstObjectByType<TreeSkeleton>();

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

    public void SetActiveMode(int index)
    {
        activeModeIndex = Mathf.Clamp(index, 0, modes.Count - 1);
        wireGoldFired   = false;
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

    public void SaveModes()
    {
        PlayerPrefs.SetString("playModes", JsonUtility.ToJson(new PlayModeList { modes = modes }));
        PlayerPrefs.SetInt("activeModeIndex", activeModeIndex);
        PlayerPrefs.Save();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    // Bump this when CreateDefaultModes() changes so saved prefs auto-reset.
    const int DEFAULTS_VERSION = 2;

    void LoadOrCreateDefaultModes()
    {
        string json = PlayerPrefs.GetString("playModes", "");
        int savedVersion = PlayerPrefs.GetInt("playModesVersion", 1);
        if (!string.IsNullOrEmpty(json) && savedVersion == DEFAULTS_VERSION)
        {
            try
            {
                var saved = JsonUtility.FromJson<PlayModeList>(json);
                if (saved?.modes != null && saved.modes.Count > 0)
                {
                    modes = saved.modes;
                    activeModeIndex = Mathf.Clamp(
                        PlayerPrefs.GetInt("activeModeIndex", 0), 0, modes.Count - 1);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayMode] Failed to load saved modes: {e.Message}. Using defaults.");
            }
        }
        CreateDefaultModes();
        PlayerPrefs.SetInt("playModesVersion", DEFAULTS_VERSION);
        PlayerPrefs.Save();
    }

    void CreateDefaultModes()
    {
        modes.Clear();

        modes.Add(new PlayMode
        {
            name = "Screensaver", isBuiltIn = true,
            defaultSpeed = GameManager.SpeedMode.Fast,
            autoWater = true, autoFertilize = true,
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
            name = "Active Play", isBuiltIn = true,
            defaultSpeed = GameManager.SpeedMode.Med,
            autoWater = false, autoFertilize = false,
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
            name = "Hands-Off", isBuiltIn = true,
            defaultSpeed = GameManager.SpeedMode.Fast,
            autoWater = true, autoFertilize = true,
            idleOrbit = false, idleOrbitDelaySecs = 0f,
            rules = new List<SpeedRule>
            {
                new SpeedRule { enabled=false, trigger=SpeedRuleTrigger.TreeInDanger, triggerParam=0, targetSpeed=GameManager.SpeedMode.Slow, idleResumeEnabled=true, idleResumeRealSeconds=60 },
            }
        });

        modes.Add(new PlayMode
        {
            name = "Focused", isBuiltIn = true,
            defaultSpeed = GameManager.SpeedMode.Slow,
            autoWater = false, autoFertilize = false,
            idleOrbit = false, idleOrbitDelaySecs = 0f,
            rules = new List<SpeedRule>()
        });

        activeModeIndex = 0;
    }

    static float CurrentInGameDay() => GameManager.dayOfYear + GameManager.year * 366f;

    [Serializable]
    class PlayModeList { public List<PlayMode> modes; }
}
