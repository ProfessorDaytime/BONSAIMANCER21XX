using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the weather looks like (2026-10-30). <see cref="WeatherManager"/> rolls the weather and the
/// tree feels it; this shows it: rain falling over the tree — heavier in a storm, and only around
/// the tree while it's under cover — sunlight dimmed and shadows softened by cloud, a warm cast in
/// a heat spell, and lightning in a storm. WeatherManager adds it; changes ease in over seconds.
///
/// It scales the scene's directional lights from the values they had when it started, so other
/// code shouldn't set those lights' intensity, colour or shadow strength while it runs.
/// <see cref="ClearSky"/> gives the starting values back, so a menu-tree capture made in a storm
/// isn't saved dark.
/// </summary>
public class WeatherVisuals : MonoBehaviour
{
    struct Look
    {
        public float light, shadow, rain;   // sun intensity ×, shadow strength ×, drops per second
        public Color tint;
    }

    static Look LookOf(WeatherState s) => s switch
    {
        WeatherState.Overcast => new Look { light = 0.72f, shadow = 0.45f, rain = 0f,    tint = new Color(0.92f, 0.95f, 1f) },
        WeatherState.Rain     => new Look { light = 0.60f, shadow = 0.35f, rain = 2400f, tint = new Color(0.88f, 0.92f, 1f) },
        WeatherState.Storm    => new Look { light = 0.45f, shadow = 0.30f, rain = 6600f, tint = new Color(0.85f, 0.90f, 1f) },
        WeatherState.Heat     => new Look { light = 1.12f, shadow = 1f,    rain = 0f,    tint = new Color(1f, 0.93f, 0.82f) },
        _                     => new Look { light = 1f,    shadow = 1f,    rain = 0f,    tint = Color.white },
    };

    const float RainHeight   = 4f;      // drops start this far above the tree's base…
    const float RainDepth    = 0.6f;    // …and vanish this far below it
    const float RainRadius   = 5f;      // wide enough to fill the view zoomed out (2026-10-30)
    const float RainSpeed    = 7f;      // units per second
    const float ShelterGap   = 0.5f;    // under cover, rain falls only in the outer half
    // Weather eases in and out over half a second, or a tenth of an in-game day if that's sooner:
    // at Fast speed a whole rain spell passes in under a second, and a 4-second ease kept it
    // raining through the dry spells between (play test 2026-10-31).
    const float EaseSeconds  = 0.5f;
    const float EaseDays     = 0.1f;

    class Lit { public Light light; public float intensity, shadow; public Color color; }
    static readonly List<Lit> lit = new List<Lit>();

    ParticleSystem rain;
    TreeSkeleton   tree;
    float lightNow = 1f, shadowNow = 1f, rainNow;
    Color tintNow  = Color.white;
    float nextFlash = -1f, flashStart = -10f;

    void Start()
    {
        lit.Clear();
        foreach (var l in FindObjectsByType<Light>())
            if (l.type == LightType.Directional && l.isActiveAndEnabled)
                lit.Add(new Lit { light = l, intensity = l.intensity, shadow = l.shadowStrength, color = l.color });
        rain = BuildRain();
    }

    void OnDestroy()
    {
        foreach (var e in lit)
            if (e.light != null) { e.light.intensity = e.intensity; e.light.shadowStrength = e.shadow; e.light.color = e.color; }
        lit.Clear();
    }

    /// <summary>A directional light's values without the weather, for anything that records the
    /// scene's lighting. Lights this doesn't drive are left as they are.</summary>
    public static void ClearSky(Light l, ref Color color, ref float intensity, ref float shadowStrength)
    {
        foreach (var e in lit)
            if (e.light == l) { color = e.color; intensity = e.intensity; shadowStrength = e.shadow; return; }
    }

    void Update()
    {
        var wm = WeatherManager.Instance;
        if (wm == null) return;

        var   look = LookOf(wm.state);
        float k    = 1f - Mathf.Exp(-3f * Mathf.Max(Time.unscaledDeltaTime / EaseSeconds,
                                                   GameManager.InGameDaysThisFrame / EaseDays));
        lightNow   = Mathf.Lerp(lightNow,  look.light,  k);
        shadowNow  = Mathf.Lerp(shadowNow, look.shadow, k);
        rainNow    = Mathf.Lerp(rainNow,   look.rain,   k);
        tintNow    = Color.Lerp(tintNow,   look.tint,   k);

        float flash = Lightning(wm.state == WeatherState.Storm);
        foreach (var e in lit)
        {
            if (e.light == null) continue;
            e.light.intensity      = e.intensity * (lightNow + flash);
            e.light.shadowStrength = e.shadow * shadowNow;
            e.light.color          = e.color * tintNow;
        }
        UpdateRain();
    }

    /// <summary>Extra sunlight from lightning: in a storm, a bright double flash every few seconds.</summary>
    float Lightning(bool storm)
    {
        float t = Time.time;
        if (!storm) { nextFlash = -1f; return 0f; }
        if (nextFlash < 0f) nextFlash = t + Random.Range(3f, 10f);
        if (t >= nextFlash) { flashStart = t; nextFlash = t + Random.Range(4f, 14f); }
        float a = t - flashStart;
        return a < 0.07f ? 2.5f : a > 0.15f && a < 0.25f ? 1.6f : 0f;
    }

    void UpdateRain()
    {
        if (rain == null) return;
        if (tree == null) tree = FindFirstObjectByType<TreeSkeleton>();
        if (tree != null) rain.transform.position = tree.transform.position + Vector3.up * RainHeight;

        var emission = rain.emission;
        emission.rateOverTime = rainNow < 50f ? 0f : rainNow;   // cut the last thin drizzle of the ease
        var shape = rain.shape;
        shape.radiusThickness = WeatherManager.Sheltered ? ShelterGap : 1f;
    }

    /// <summary>The rain: streaks from a flat disc above the tree, falling straight down.</summary>
    ParticleSystem BuildRain()
    {
        var shader = Shader.Find("Custom/RainStreak");
        if (shader == null) { Debug.LogWarning("[Weather] RainStreak shader missing — no rain shown."); return null; }

        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop            = true;
        main.duration        = 1f;
        main.startLifetime   = (RainHeight + RainDepth) / RainSpeed;
        main.startSpeed      = 0f;
        main.startSize       = 0.01f;
        main.startColor      = new Color(0.78f, 0.84f, 0.95f, 0.45f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 6000;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType       = ParticleSystemShapeType.Circle;
        shape.radius          = RainRadius;
        shape.rotation        = new Vector3(90f, 0f, 0f);   // the disc lies flat
        shape.radiusThickness = 1f;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space   = ParticleSystemSimulationSpace.World;
        velocity.x       = new ParticleSystem.MinMaxCurve(0f);
        velocity.y       = new ParticleSystem.MinMaxCurve(-RainSpeed);
        velocity.z       = new ParticleSystem.MinMaxCurve(0f);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode        = ParticleSystemRenderMode.Stretch;
        r.velocityScale     = 0.03f;   // streak length ≈ speed × this
        r.lengthScale       = 1f;
        r.sharedMaterial    = new Material(shader);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;

        ps.Play();
        return ps;
    }
}
