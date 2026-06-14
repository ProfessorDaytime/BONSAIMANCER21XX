using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor helper that generates a populated default <see cref="ItemCatalog"/> (pots + rocks) so the
/// item-selection menus have content without hand-authoring assets. Mirrors StyleDefinitionCreator.
/// Run via <b>Bonsai → Create Default Item Catalog</b>, then assign the catalog to buttonClicker's
/// "Item Catalog" field. Re-running creates a fresh uniquely-named catalog (won't clobber edits).
/// </summary>
public static class ItemCatalogCreator
{
    const string Dir = "Assets/Items";

    [MenuItem("Bonsai/Create Default Item Catalog")]
    public static void CreateDefaultCatalog()
    {
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets", "Items");

        var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        catalog.items = new List<ItemDefinition>();

        // Pots — one per footprint size (maps to PotSoil.PotSize).
        AddPot(catalog, "Mame Pot",      "Tiny round pot for shohin / mame trees.",        PotSoil.PotSize.XS,   new Color(0.40f, 0.26f, 0.20f));
        AddPot(catalog, "Glazed Oval",   "Small glazed oval — good for informal styles.",  PotSoil.PotSize.S,    new Color(0.30f, 0.42f, 0.45f));
        AddPot(catalog, "Unglazed Rect", "Classic unglazed rectangle, mid size.",          PotSoil.PotSize.M,    new Color(0.45f, 0.34f, 0.28f));
        AddPot(catalog, "Deep Cascade",  "Tall pot for cascade / semi-cascade trees.",     PotSoil.PotSize.L,    new Color(0.33f, 0.30f, 0.32f));
        AddPot(catalog, "Grow Box",      "Large training box for trunk thickening.",       PotSoil.PotSize.XL,   new Color(0.40f, 0.36f, 0.24f));
        AddPot(catalog, "Slab",          "Flat slab for forest / root-on-rock plantings.", PotSoil.PotSize.Slab, new Color(0.36f, 0.36f, 0.36f));

        // Rocks — one per scale (for the rock entry point; maps to RockPlacer.RockSize).
        AddRock(catalog, "River Stone", "Smooth rounded stone.",   RockPlacer.RockSize.S,  new Color(0.50f, 0.48f, 0.44f));
        AddRock(catalog, "Crag",        "Jagged upright crag.",    RockPlacer.RockSize.M,  new Color(0.42f, 0.40f, 0.38f));
        AddRock(catalog, "Cliff",       "Large cliff face.",       RockPlacer.RockSize.L,  new Color(0.38f, 0.36f, 0.34f));
        AddRock(catalog, "Mountain",    "Massive mountain rock.",  RockPlacer.RockSize.XL, new Color(0.34f, 0.33f, 0.32f));

        string catPath = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/ItemCatalog.asset");
        AssetDatabase.CreateAsset(catalog, catPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
        Debug.Log($"[Items] Created default ItemCatalog with {catalog.items.Count} items at {catPath}. " +
                  "Assign it to buttonClicker's 'Item Catalog' field in the Inspector.");
    }

    static void AddPot(ItemCatalog cat, string label, string desc, PotSoil.PotSize size, Color swatch)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name        = "Pot_" + size;
        def.displayName = label;
        def.description = desc;
        def.category    = ItemCategory.Pot;
        def.potSize     = size;
        def.swatchColor = swatch;
        SaveDef(cat, def);
    }

    static void AddRock(ItemCatalog cat, string label, string desc, RockPlacer.RockSize size, Color swatch)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name        = "Rock_" + size;
        def.displayName = label;
        def.description = desc;
        def.category    = ItemCategory.Rock;
        def.rockSize    = size;
        def.swatchColor = swatch;
        SaveDef(cat, def);
    }

    static void SaveDef(ItemCatalog cat, ItemDefinition def)
    {
        string path = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/{def.name}.asset");
        AssetDatabase.CreateAsset(def, path);
        cat.items.Add(def);
    }
}
