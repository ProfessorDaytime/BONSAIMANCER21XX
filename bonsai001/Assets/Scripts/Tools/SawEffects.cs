using UnityEngine;

/// <summary>
/// Saw feedback (2026-10-28): a rasp per stroke and sawdust + splinters at the kerf. The branch
/// is already highlighted while sawing, so this is the only feedback the saw needs.
///
/// The stroke sound is synthesised at first use (noise shaped by saw-tooth clicks, band-limited),
/// so no audio asset is required. Add an SFX named "Saw" to the AudioManager to use a recording
/// instead.
/// </summary>
public static class SawEffects
{
    static AudioClip pushClip, pullClip;
    static ParticleSystem dust, splinters;

    static readonly Color DustLight = new Color(0.86f, 0.74f, 0.55f);
    static readonly Color DustDark  = new Color(0.66f, 0.50f, 0.32f);
    static readonly Color Splinter  = new Color(0.78f, 0.62f, 0.40f);

    // ── Sound ─────────────────────────────────────────────────────────────────

    /// <summary>One stroke's rasp. <paramref name="push"/> alternates so strokes don't sound identical.</summary>
    public static void PlayStroke(bool push)
    {
        var am = AudioManager.Instance;
        if (am == null || am.sfxSource == null) return;

        var recorded = am.FindSFX("Saw");
        if (recorded != null) { am.sfxSource.PlayOneShot(recorded, push ? 1f : 0.85f); return; }

        if (pushClip == null) pushClip = BuildStroke("SawPush", 0.26f, 105f, 0.62f, 11);
        if (pullClip == null) pullClip = BuildStroke("SawPull", 0.24f,  85f, 0.50f, 23);
        am.sfxSource.PlayOneShot(push ? pushClip : pullClip, 0.9f);
    }

    static AudioClip BuildStroke(string name, float seconds, float teethPerSec, float brightness, int seed)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        var rng = new System.Random(seed);

        float lp = 0f, hpPrev = 0f, hpOut = 0f;
        float lpA = Mathf.Lerp(0.15f, 0.55f, brightness);   // one-pole low-pass amount
        const float hpA = 0.97f;                              // one-pole high-pass (removes rumble)
        float toothPhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / seconds;

            // Teeth speed up then slow down across the stroke, like a real pull.
            float speed = Mathf.Sin(u * Mathf.PI);
            toothPhase += teethPerSec * (0.6f + 0.6f * speed) / rate;
            float tooth = toothPhase % 1f;
            float click = Mathf.Exp(-tooth * 18f);            // sharp bite at each tooth

            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float s = noise * (0.35f + 0.65f * click);

            lp += lpA * (s - lp);
            hpOut = hpA * (hpOut + lp - hpPrev);
            hpPrev = lp;

            float env = Mathf.Clamp01(u / 0.08f) * Mathf.Clamp01((1f - u) / 0.25f) * (0.5f + 0.5f * speed);
            data[i] = hpOut * env * 0.9f;
        }

        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ── Particles ─────────────────────────────────────────────────────────────

    /// <summary>Sawdust while the blade moves. <paramref name="amount"/> ≈ pixels moved this frame.</summary>
    public static void EmitDust(Vector3 kerf, Vector3 strokeDir, float scale, float amount)
    {
        Ensure();
        int count = Mathf.Clamp(Mathf.RoundToInt(amount * 0.12f), 0, 6);
        for (int i = 0; i < count; i++)
        {
            var p = new ParticleSystem.EmitParams
            {
                position      = kerf + Random.insideUnitSphere * 0.15f * scale,
                velocity      = (strokeDir * Random.Range(0.2f, 0.9f) + Random.insideUnitSphere * 0.35f
                                 + Vector3.down * 0.2f) * scale * 4f,
                startSize     = Random.Range(0.012f, 0.03f) * scale,
                startLifetime = Random.Range(0.7f, 1.4f),
                startColor    = Color.Lerp(DustLight, DustDark, Random.value),
            };
            dust.Emit(p, 1);
        }
    }

    /// <summary>A few splinters flicked out at the end of each stroke.</summary>
    public static void EmitSplinters(Vector3 kerf, Vector3 strokeDir, float scale)
    {
        Ensure();
        int count = Random.Range(2, 5);
        for (int i = 0; i < count; i++)
        {
            var p = new ParticleSystem.EmitParams
            {
                position      = kerf,
                velocity      = (strokeDir * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.6f
                                 + Vector3.up * 0.3f) * scale * 4f,
                startSize     = Random.Range(0.01f, 0.018f) * scale,
                startLifetime = Random.Range(0.6f, 1.1f),
                startColor    = Color.Lerp(Splinter, DustDark, Random.value * 0.5f),
            };
            splinters.Emit(p, 1);
        }
    }

    static void Ensure()
    {
        if (dust == null)      dust      = Build("_SawDust",      ParticleSystemRenderMode.Billboard,        0.35f);
        if (splinters == null) splinters = Build("_SawSplinters", ParticleSystemRenderMode.Stretch, 0.6f);
    }

    static ParticleSystem Build(string name, ParticleSystemRenderMode mode, float gravity)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake     = false;
        main.loop            = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.useUnscaledTime = true;     // same speed at any game speed
        main.maxParticles    = 600;

        var emission = ps.emission;
        emission.enabled = false;        // emitted by hand only
        var shape = ps.shape;
        shape.enabled = false;

        // Fade out over the last part of each particle's life.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        if (mode == ParticleSystemRenderMode.Stretch) { r.lengthScale = 3f; r.velocityScale = 0.02f; }
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        // Vertex-coloured unlit particle shader; Sprites/Default is the always-included fallback.
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        r.sharedMaterial = new Material(sh);

        ps.Play();
        return ps;
    }
}
