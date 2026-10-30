using UnityEngine;

/// <summary>
/// On a rebuilt menu-tree leaf part (2026-10-29): how exposed its leaves are, 0 = deep inside the
/// lower canopy, 1 = top and outer edge. Autumn colour starts on the sunniest, outermost leaves
/// and works inward and down; shaded interior leaves turn last and duller. The greenhouse season
/// slider reads this to stagger the colour change and the leaf drop.
/// </summary>
public class MenuTreeLeaf : MonoBehaviour
{
    public float exposure = 0.5f;
}
