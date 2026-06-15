using System.Collections;
using UnityEngine;

/// <summary>
/// Plays the equipped Music cosmetic — a looping background track. A Music
/// <see cref="ItemDefinition"/> carries an <c>audioClip</c>; equipping it crossfades to that track
/// (a null clip = silence, a valid "no music" choice). Driven by <see cref="CustomizeManager"/>
/// (buy/equip + restore on load). Add this component to the scene; it creates its own AudioSource.
///
/// Tracks are author-supplied: drop AudioClips onto the Music ItemDefinition assets.
/// See Docs/PROGRESSION_DESIGN.md §7.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [SerializeField, Range(0f, 1f)] float volume      = 0.55f;
    [SerializeField]                float fadeSeconds = 0.6f;

    AudioSource source;
    AudioClip   currentClip;
    Coroutine   fade;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.loop        = true;
        source.playOnAwake = false;
        source.volume      = volume;
    }

    /// <summary>Equip a Music item (null or null-clip = fade to silence).</summary>
    public void Apply(ItemDefinition def)
    {
        AudioClip clip = def != null ? def.audioClip : null;
        if (clip == currentClip) return;
        currentClip = clip;

        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(Swap(clip));
    }

    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        if (source != null && fade == null) source.volume = volume;
    }

    IEnumerator Swap(AudioClip clip)
    {
        yield return FadeTo(0f);
        source.Stop();

        if (clip != null)
        {
            source.clip = clip;
            source.Play();
            yield return FadeTo(volume);
        }
        fade = null;
    }

    IEnumerator FadeTo(float target)
    {
        float start = source.volume;
        float dur   = Mathf.Max(0.01f, fadeSeconds);
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            source.volume = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        source.volume = target;
    }
}
