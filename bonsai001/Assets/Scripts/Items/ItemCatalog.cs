using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of all <see cref="ItemDefinition"/>s for the shared <see cref="ItemCatalogPanel"/>.
/// One asset, referenced by buttonClicker; new items are added by dropping definitions into the
/// list. Empty categories simply show no cards — the existing default pot/rock/table still works,
/// so the game runs before any content is authored.
/// </summary>
[CreateAssetMenu(fileName = "ItemCatalog", menuName = "Bonsai/Item Catalog")]
public class ItemCatalog : ScriptableObject
{
    [Tooltip("Every selectable item. Order within a category is the display order.")]
    public List<ItemDefinition> items = new List<ItemDefinition>();

    /// <summary>All non-null items in a category, in list order.</summary>
    public List<ItemDefinition> ByCategory(ItemCategory category)
    {
        var result = new List<ItemDefinition>();
        if (items == null) return result;
        foreach (var it in items)
            if (it != null && it.category == category) result.Add(it);
        return result;
    }

    /// <summary>Whether any authored item exists in a category (gates showing the catalog at all).</summary>
    public bool HasAny(ItemCategory category)
    {
        if (items == null) return false;
        foreach (var it in items)
            if (it != null && it.category == category) return true;
        return false;
    }
}
