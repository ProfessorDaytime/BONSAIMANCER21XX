using UnityEngine;

public enum WeatherState { Clear, Overcast, Rain, Storm, Heat }

/// <summary>
/// Seasonal weather that the tree actually feels.
///
/// Drives three things that already exist rather than inventing parallel systems:
///   • <see cref="WindManager"/> strength — which the stability system reads as wind LOAD, the
///     only force that can genuinely tip a potted tree (its own weight cannot: measured
///     2026-08-24, ~2 kg of soil against a ~170 g tree).
///   • Soil moisture — rain waters the tree for free; sun, heat and wind dry it faster. Wind
///     drying is a classic bonsai killer and is why a breezy clear day is harder on a tree than
///     a still one.
///   • Waterlogging — prolonged rain on poorly-draining soil raises saturation, so the drainage
///     holes and soil mix matter in weather they never mattered in before.
///
/// The interesting loop this creates: a storm brings wind (which threatens to tip the tree) AND
/// rain (which soaks the soil, adding ballast that resists tipping). So weather both threatens and
/// protects, and the genuinely dangerous case is a DRY pot in high wind — exactly what growers
/// guard against. Deciduous trees also drop their sail in autumn, so a winter gale is survivable
/// where a summer one would not be.
/// </summary>
public class WeatherManager : MonoBehaviour
{
    public static WeatherManager Instance { get; private set; }

    [Header("Weather")]
    [Tooltip("Current conditions. Set manually with Force Weather for testing.")]
    public WeatherState state = WeatherState.Clear;

    [Tooltip("Hold the state above instead of rolling new weather — for testing.")]
    public bool forceWeather = false;

    [Tooltip("Average in-game days a weather state lasts before rerolling.")]
    public float averageSpellDays = 4f;

    [Header("Effects")]
    [Tooltip("Moisture restored per in-game day of rain. Storms deliver more.")]
    public float rainMoisturePerDay = 0.55f;

    [Tooltip("Extra soil drying multiplier in heat, on top of the normal drain.")]
    public float heatDryMultiplier = 2.2f;

    [Tooltip("How much wind adds to drying. Wind strips moisture faster than sun alone — a very " +
             "common way potted trees are lost.")]
    public float windDryMultiplier = 1.6f;

    [Tooltip("Saturation gained per in-game day of rain when drainage cannot keep up.")]
    public float rainSaturationPerDay = 0.12f;

    // Wind strength each state settles WindManager toward.
    static float WindFor(WeatherState s) => s switch
    {
        WeatherState.Clear    => 0.18f,
        WeatherState.Overcast => 0.30f,
        WeatherState.Rain     => 0.45f,
        WeatherState.Storm    => 1.00f,
        WeatherState.Heat     => 0.12f,
        _                     => 0.25f,
    };

    /// <summary>Drying multiplier this weather applies to soil-moisture drain.</summary>
    public float DryingMultiplier
    {
        get
        {
            float m = state switch
            {
                WeatherState.Heat     => heatDryMultiplier,
                WeatherState.Clear    => 1f,
                WeatherState.Overcast => 0.6f,
                WeatherState.Rain     => 0.25f,
                WeatherState.Storm    => 0.4f,
                _                     => 1f,
            };
            // Wind pulls moisture out regardless of sky — additive on top of the sky term.
            var wind = WindManager.Instance;
            if (wind != null) m *= Mathf.Lerp(1f, windDryMultiplier, wind.strength);
            return m;
        }
    }

    public bool IsRaining => state == WeatherState.Rain || state == WeatherState.Storm;

    float spellDaysLeft;
    int   lastDay = -1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        spellDaysLeft = averageSpellDays;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        // Settle the wind toward this weather's level rather than snapping, so gusts stay smooth.
        var wind = WindManager.Instance;
        if (wind != null)
            wind.strength = Mathf.MoveTowards(wind.strength, WindFor(state), 0.25f * Time.deltaTime);

        // Advance the spell on in-game day boundaries so weather keeps pace at any timescale.
        if (GameManager.day == lastDay) return;
        lastDay = GameManager.day;

        if (forceWeather) return;

        spellDaysLeft -= 1f;
        if (spellDaysLeft > 0f) return;

        state = RollWeather(GameManager.month);
        spellDaysLeft = Mathf.Max(1f, averageSpellDays * Random.Range(0.5f, 1.6f));
        Debug.Log($"[Weather] → {state} for ~{spellDaysLeft:F0}d | month={GameManager.month} " +
                  $"wind→{WindFor(state):F2}");
    }

    /// <summary>Seasonally weighted roll. Summer brings heat and thunderstorms, autumn and winter
    /// bring the wet and the gales, spring is changeable.</summary>
    static WeatherState RollWeather(int month)
    {
        float clear, overcast, rain, storm, heat;

        if (month >= 3 && month <= 5)        // spring — changeable
            { clear = 30f; overcast = 30f; rain = 30f; storm = 8f;  heat = 2f;  }
        else if (month >= 6 && month <= 8)   // summer — hot, occasional thunderstorm
            { clear = 40f; overcast = 15f; rain = 15f; storm = 10f; heat = 20f; }
        else if (month >= 9 && month <= 11)  // autumn — wet and windy
            { clear = 20f; overcast = 30f; rain = 32f; storm = 16f; heat = 2f;  }
        else                                  // winter — grey, gales
            { clear = 18f; overcast = 38f; rain = 26f; storm = 18f; heat = 0f;  }

        float total = clear + overcast + rain + storm + heat;
        float r = Random.value * total;

        if ((r -= clear)    < 0f) return WeatherState.Clear;
        if ((r -= overcast) < 0f) return WeatherState.Overcast;
        if ((r -= rain)     < 0f) return WeatherState.Rain;
        if ((r -= storm)    < 0f) return WeatherState.Storm;
        return WeatherState.Heat;
    }

    /// <summary>Rain applied for a slice of in-game time. Called from the tree's grow tick so it
    /// rides the same fixed-step clock as the moisture drain it opposes.</summary>
    public void ApplyRain(TreeSkeleton tree, float inGameDays)
    {
        if (!IsRaining || tree == null || inGameDays <= 0f) return;

        float amount = rainMoisturePerDay * inGameDays * (state == WeatherState.Storm ? 1.5f : 1f);
        tree.soilMoisture = Mathf.Min(1f, tree.soilMoisture + amount);

        // Rain that the soil cannot drain away pools — poor drainage now has a weather cost.
        var soil = tree.GetComponent<PotSoil>();
        if (soil != null && tree.soilMoisture >= 0.98f)
        {
            float drainShort = Mathf.Clamp01(1f - soil.EffectiveDrainageRate);
            soil.saturationLevel = Mathf.Min(1f,
                soil.saturationLevel + rainSaturationPerDay * drainShort * inGameDays);
        }
    }
}
