/// <summary>
/// Damage types that can reduce a TreeNode's health.
/// Keeping them in one place makes the damage budget easy to tune
/// as more systems are added.
/// </summary>
public enum DamageType
{
    WireBend,       // re-bending already-set wood; one-shot hit
    WireDamage,     // progressive damage while wire is left on too long
    TrimTrauma,     // small hit on pruning; recovers over a season
    Drought,        // slow drain if soil moisture too low              [reserved]
    NutrientLack,   // reduces recovery rate                           [reserved]
    WoundDrain,     // progressive, from open unprotected wounds
    FertilizerBurn,   // over-fertilization burns roots
    FungalInfection,  // fungal disease; worsens with moisture and wounds
    RootRot,          // waterlogged soil suffocates root nodes
    RepotStress,      // temporary health hit from disturbing the root system at repot
    Shading,          // branch receives no light for too many seasons
    JunctionStress,   // heavy child branch strains the attachment node
}
