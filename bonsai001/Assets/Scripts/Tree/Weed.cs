using UnityEngine;

public enum WeedType
{
    Grass,      // clumping grass — common, shallow roots, moderate rip chance
    Clover,     // low rosette — common, very shallow, low rip chance
    Dandelion,  // tap-rooted — persistent, high rip chance, harder to pull
    Thistle,    // deep tap root — persistent, hardest to pull, most damaging
}

/// <summary>
/// Data and visual state for a single weed in the bonsai pot.
/// Spawned and tracked by WeedManager; interaction driven by WeedPuller.
/// </summary>
public class Weed : MonoBehaviour
{
    public WeedType weedType;

    /// <summary>Roots still in ground — top was torn off on a previous pull attempt.</summary>
    public bool isRipped;

    /// <summary>
    /// Normalised screen-height fraction of upward drag needed to fully extract.
    /// e.g. 0.05 = 5% of screen height ≈ 55 px on 1080p.
    /// Multiplied by 1.8 each time the weed is ripped.
    /// </summary>
    public float forceRequired;

    /// <summary>0–1 probability of ripping rather than clean-pulling on this attempt.</summary>
    public float ripChance;

    /// <summary>World position the weed rests at when not being pulled.</summary>
    [HideInInspector] public Vector3 restPosition;
}
