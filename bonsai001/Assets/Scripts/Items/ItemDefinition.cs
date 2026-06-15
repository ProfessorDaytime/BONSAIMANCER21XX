using UnityEngine;

/// <summary>Catalog category. Drives which entry point surfaces an item and which apply-handler
/// runs when it is chosen. Appended categories keep their int order for serialization.</summary>
public enum ItemCategory { Pot, Rock, GroundCover, Table, Decoration, Background, Music, UiTheme }

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

    [Header("Unlock (progression)")]
    [Tooltip("If false the card stays LOCKED until the progression system grants ownership of this " +
             "item (a milestone reward). If true, the item is available — free if cost is 0, or " +
             "purchasable for `cost` Aesthetic Points.")]
    public bool unlockedByDefault = true;
    [Tooltip("Identifier a milestone grants to unlock this item. Empty = keyed by the asset name.")]
    public string unlockId = "";

    [Header("Shop")]
    [Tooltip("Aesthetic Points price. 0 = free (available immediately when unlockedByDefault). " +
             "Ignored if no ProgressionManager is in the scene (catalog still works without the economy).")]
    public int cost = 0;

    [Header("Background category (BackgroundManager)")]
    [Tooltip("Background category only: skybox material to apply. Null = use swatchColor as a solid " +
             "backdrop instead. The `prefab` field (above) can hold optional scenery to place around the tree.")]
    public Material skyboxMaterial;
    [Tooltip("Background category only: when applied, also drives the camera's solid backdrop colour " +
             "(reuses swatchColor) and the scene ambient tint when no skybox is set.")]
    public Color ambientColor = new Color(0.5f, 0.5f, 0.5f);

    [Header("UI Theme category (UiTheme)")]
    [Tooltip("UiTheme category only: id of the code-defined palette to apply (forest / charcoal / " +
             "night / sakura / parchment). See UiTheme.cs.")]
    public string themeId = "";

    [Header("Music category (MusicManager)")]
    [Tooltip("Music category only: the looping track played while this item is equipped. " +
             "Null = silence (a valid 'no music' choice).")]
    public AudioClip audioClip;

    /// <summary>Display name fallback so a card always has a label.</summary>
    public string Label => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    /// <summary>Stable ownership/unlock key — the authored unlockId, or the asset name as fallback.</summary>
    public string Id => string.IsNullOrWhiteSpace(unlockId) ? name : unlockId;
}
