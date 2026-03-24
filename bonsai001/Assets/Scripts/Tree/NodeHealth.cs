/// <summary>
/// Damage types that can reduce a TreeNode's health.
/// Keeping them in one place makes the damage budget easy to tune
/// as more systems are added.
/// </summary>
public enum DamageType
{
    WireBend,       // re-bending already-set wood; one-shot hit
    WireDamage,     // progressive damage while wire is left on too long
    TrimTrauma,     // small hit on pruning; recovers over a season     [reserved]
    Drought,        // slow drain if soil moisture too low              [reserved]
    NutrientLack,   // reduces recovery rate                           [reserved]
    WoundDrain,     // progressive, from open unprotected wounds
}
