using UnityEngine;

/// <summary>
/// Marks a cylinder as a pot drainage hole. Place a cylinder at each hole in the pot model — its
/// XZ position is the hole centre and its XZ scale (diameter) the hole size. <see cref="PotSoil"/>
/// reads these markers into drainage-hole discs (root escape + water drainage) and hides the
/// cylinder's renderer in-game (it's only a placement guide). See PLAN "Pot Drainage Holes".
/// </summary>
[DisallowMultipleComponent]
public class DrainageHole : MonoBehaviour
{
    [Tooltip("Hide this cylinder's renderer at runtime — it's only an authoring guide for the hole.")]
    public bool hideInGame = true;

    /// <summary>World-space hole radius from the cylinder's XZ scale (Unity cylinder base radius = 0.5).</summary>
    public float WorldRadius => Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.5f;
}
