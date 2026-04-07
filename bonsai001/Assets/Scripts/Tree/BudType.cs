/// <summary>
/// Controls how a node forks when it branches.
/// Alternate: one continuation + one optional lateral (most trees).
/// Opposite: two symmetric equal forks (Japanese maple, ash, dogwood).
/// </summary>
public enum BudType
{
    Alternate,  // one continuation + optional lateral
    Opposite,   // two symmetric equal forks
}
