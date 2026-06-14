using UnityEngine;

/// <summary>Catalog category. Drives which entry point surfaces an item and which apply-handler
/// runs when it is chosen.</summary>
public enum ItemCategory { Pot, Rock, GroundCover, Table }

/// <summary>
/// One selectable/placeable catalog item — a pot, rock, ground-cover patch, or display table.
/// Authored as an asset; the art (prefab + thumbnail) can be dropped in later with no code changes.
/// The shared <see cref="ItemCatalogPanel"/> lists these by category, and per-category apply hooks
/// in buttonClicker turn a chosen definition into a real change (pot swap, rock spawn, table swap,
/// cover patch). A null prefab just falls back to the category's current default mesh, so the
/// framework runs with zero authored content.
/// </summary>
[CreateAssetMenu(fileName = "Item_", menuName = "Bonsai/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "New Item";
    [TextArea] public string description;
    public ItemCategory category = ItemCategory.Pot;

    [Header("Art (optional — drop in later)")]
    [Tooltip("Mesh/prefab swapped in or instantiated when applied. Null = keep the category's " +
             "current default (the existing pot/rock/table mesh).")]
    public GameObject prefab;
    [Tooltip("Card thumbnail. Null = a flat swatch (swatchColor) is shown instead.")]
    public Sprite thumbnail;
    [Tooltip("Accent/placeholder color for the card when no thumbnail is set.")]
    public Color swatchColor = new Color(0.45f, 0.38f, 0.30f);
    public Vector3 baseScale = Vector3.one;

    [Header("Category mapping")]
    [Tooltip("Pot category: which pot-size footprint this maps to.")]
    public PotSoil.PotSize potSize = PotSoil.PotSize.M;
    [Tooltip("Rock category: which rock-size scale this maps to.")]
    public RockPlacer.RockSize rockSize = RockPlacer.RockSize.M;

    [Header("Unlock (placeholder for gamification)")]
    [Tooltip("If false the card shows locked until unlockId is granted by the future progression system.")]
    public bool unlockedByDefault = true;
    [Tooltip("Identifier the gamification system grants to unlock this item. Empty = always available.")]
    public string unlockId = "";

    /// <summary>Display name fallback so a card always has a label.</summary>
    public string Label => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
}
