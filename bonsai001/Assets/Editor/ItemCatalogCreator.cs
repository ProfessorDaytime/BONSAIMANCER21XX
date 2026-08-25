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

    /// <summary>Fills every PotSoil in the open scene with the authored per-size pot meshes,
    /// matched by PotSize name, so the six slots don't need hand-assigning.</summary>
    [MenuItem("Bonsai/Assign Pot Meshes To Scene")]
    public static void AssignPotMeshes()
    {
        string[] meshNames = { "XS Plastic Pot", "S Plastic Pot", "M Plastic Pot",
                               "L Plastic Pot", "XL Plastic Pot", "Slab 01 Plastic Pot" };

        var models = new GameObject[meshNames.Length];
        for (int i = 0; i < meshNames.Length; i++)
        {
            models[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/{meshNames[i]}.fbx");
            if (models[i] == null)
                Debug.LogWarning($"[Items] Pot mesh not found: {MeshDir}/{meshNames[i]}.fbx");
        }

        var soils = Object.FindObjectsByType<PotSoil>(FindObjectsSortMode.None);
        foreach (var soil in soils)
        {
            Undo.RecordObject(soil, "Assign Pot Meshes");
            soil.sizePotPrefabs = models;
            soil.usePlaceholderPotMesh = false;
            EditorUtility.SetDirty(soil);
        }
        Debug.Log($"[Items] Assigned {meshNames.Length} pot meshes to {soils.Length} PotSoil " +
                  "component(s) and turned off the procedural placeholder.");
    }

    /// <summary>Prints the drainage-hole layout for every pot size in WORLD units, ready to model
    /// in Blender. Modelling to these numbers means escaped roots emerge from real holes with no
    /// per-pot marker objects needed.</summary>
    [MenuItem("Bonsai/Print Drainage Hole Layout")]
    public static void PrintHoleLayout()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Items] Drainage-hole layout (world units — model these in Blender).");
        sb.AppendLine("Pattern per pot: 1 centre hole + 4 on a ring at 90°, offset 45°.");
        sb.AppendLine($"{"Size",-6}{"floorR",10}{"centreHole ⌀",15}{"ringDist",10}{"ringHole ⌀",13}");
        foreach (PotSoil.PotSize s in System.Enum.GetValues(typeof(PotSoil.PotSize)))
        {
            var (floorR, centreR, ringDist, ringR) = PotSoil.HoleWorldLayout(s);
            sb.AppendLine($"{s,-6}{floorR,10:F4}{centreR * 2f,15:F4}{ringDist,10:F4}{ringR * 2f,13:F4}");
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem("Bonsai/Create Default Item Catalog")]
    public static void CreateDefaultCatalog()
    {
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets", "Items");

        var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        catalog.items = new List<ItemDefinition>();

        // Pots — one per footprint size (maps to PotSoil.PotSize). Cost = Aesthetic Points; the
        // classic mid pot is free, the rest are cosmetic upgrades you save up for.
        AddPot(catalog, "Unglazed Rect", "Classic unglazed rectangle, mid size.",          PotSoil.PotSize.M,      0, new Color(0.45f, 0.34f, 0.28f), "M Plastic Pot");
        AddPot(catalog, "Mame Pot",      "Tiny round pot for shohin / mame trees.",        PotSoil.PotSize.XS,    80, new Color(0.40f, 0.26f, 0.20f), "XS Plastic Pot");
        AddPot(catalog, "Glazed Oval",   "Small glazed oval — good for informal styles.",  PotSoil.PotSize.S,    120, new Color(0.30f, 0.42f, 0.45f), "S Plastic Pot");
        AddPot(catalog, "Grow Box",      "Large training box for trunk thickening.",       PotSoil.PotSize.XL,   100, new Color(0.40f, 0.36f, 0.24f), "XL Plastic Pot");
        AddPot(catalog, "Deep Cascade",  "Tall pot for cascade / semi-cascade trees.",     PotSoil.PotSize.L,    150, new Color(0.33f, 0.30f, 0.32f), "L Plastic Pot");
        AddPot(catalog, "Slab",          "Flat slab for forest / root-on-rock plantings.", PotSoil.PotSize.Slab, 200, new Color(0.36f, 0.36f, 0.36f), "Slab 01 Plastic Pot");

        // Rocks — one per scale (for the rock entry point; maps to RockPlacer.RockSize).
        AddRock(catalog, "River Stone", "Smooth rounded stone.",   RockPlacer.RockSize.S,    0, new Color(0.50f, 0.48f, 0.44f));
        AddRock(catalog, "Crag",        "Jagged upright crag.",    RockPlacer.RockSize.M,  120, new Color(0.42f, 0.40f, 0.38f));
        AddRock(catalog, "Cliff",       "Large cliff face.",       RockPlacer.RockSize.L,  200, new Color(0.38f, 0.36f, 0.34f));
        AddRock(catalog, "Mountain",    "Massive mountain rock.",  RockPlacer.RockSize.XL, 300, new Color(0.34f, 0.33f, 0.32f));

        // Backgrounds — solid-colour backdrops (no art needed). Apply swaps the camera backdrop
        // + ambient tint via BackgroundManager. The default Studio is free.
        AddBackground(catalog, "Studio Teal", "The classic plain backdrop.",   0, new Color(0.18f, 0.34f, 0.36f), new Color(0.55f, 0.60f, 0.60f));
        AddBackground(catalog, "Night",       "A deep blue dusk.",           120, new Color(0.05f, 0.07f, 0.13f), new Color(0.25f, 0.28f, 0.38f));
        AddBackground(catalog, "Sunset",      "Warm amber glow.",            150, new Color(0.42f, 0.22f, 0.14f), new Color(0.70f, 0.50f, 0.35f));
        AddBackground(catalog, "Sakura Dawn", "Soft pink morning light.",    180, new Color(0.46f, 0.32f, 0.38f), new Color(0.78f, 0.62f, 0.66f));
        AddBackground(catalog, "Mist",        "Pale grey fog.",              140, new Color(0.62f, 0.66f, 0.66f), new Color(0.80f, 0.82f, 0.82f));

        // UI Themes — reskin the progression/shop overlays + AP HUD (UiTheme palettes). Forest is free.
        AddTheme(catalog, "Forest",    "forest",    "The default warm-green theme.",   0, new Color(0.898f, 0.702f, 0.086f));
        AddTheme(catalog, "Charcoal",  "charcoal",  "Cool neutral dark slate.",      100, new Color(0.58f, 0.72f, 0.86f));
        AddTheme(catalog, "Night",     "night",     "Deep blue, sky-blue accents.",  120, new Color(0.45f, 0.70f, 0.95f));
        AddTheme(catalog, "Sakura",    "sakura",    "Soft plum with pink accents.",  150, new Color(0.95f, 0.55f, 0.70f));
        AddTheme(catalog, "Parchment", "parchment", "Warm paper, brown ink.",        150, new Color(0.55f, 0.40f, 0.15f));

        // Music — looping tracks. Silence is free; the rest are slots: drop an AudioClip onto each
        // generated Music_* asset's "Audio Clip" field (a null clip just plays silence).
        AddMusic(catalog, "Silence",      "No music.",                       0, new Color(0.30f, 0.30f, 0.32f));
        AddMusic(catalog, "Zen Garden",   "Calm ambient pads.",            100, new Color(0.35f, 0.55f, 0.55f));
        AddMusic(catalog, "Koto",         "Traditional Japanese strings.", 150, new Color(0.55f, 0.40f, 0.30f));
        AddMusic(catalog, "Rainfall",     "Gentle rain.",                  120, new Color(0.40f, 0.50f, 0.70f));
        AddMusic(catalog, "Forest Birds", "Dawn chorus.",                  120, new Color(0.40f, 0.60f, 0.35f));

        // Decorations — an accent placed beside the tree (DecorationManager). None is free; Stone
        // Lantern is built-in/procedural; the rest are slots: drop a prefab on each Decor_* asset.
        AddDecoration(catalog, "None",         "No decoration.",            0, "",        new Color(0.30f, 0.30f, 0.32f));
        AddDecoration(catalog, "Stone Lantern","A small stone tōrō lantern.",100, "lantern", new Color(0.55f, 0.55f, 0.52f));
        AddDecoration(catalog, "Figurine",     "Drop in a figurine model.", 120, "",        new Color(0.50f, 0.42f, 0.35f));
        AddDecoration(catalog, "Accent Stone", "Drop in a stone model.",     90, "",        new Color(0.45f, 0.45f, 0.42f));

        string catPath = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/ItemCatalog.asset");
        AssetDatabase.CreateAsset(catalog, catPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
        Debug.Log($"[Items] Created default ItemCatalog with {catalog.items.Count} items at {catPath}. " +
                  "Assign it to buttonClicker's 'Item Catalog' field in the Inspector.");
    }

    const string MeshDir = "Assets/Prefabs/Meshes";

    static void AddPot(ItemCatalog cat, string label, string desc, PotSoil.PotSize size, int cost,
                       Color swatch, string meshName)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name        = "Pot_" + size;
        def.displayName = label;
        def.description = desc;
        def.category    = ItemCategory.Pot;
        def.potSize     = size;
        def.cost        = cost;
        def.swatchColor = swatch;
        def.prefab      = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/{meshName}.fbx");
        if (def.prefab == null)
            Debug.LogWarning($"[Items] Pot mesh not found: {MeshDir}/{meshName}.fbx — " +
                             $"{def.name} will fall back to the procedural placeholder.");
        SaveDef(cat, def);
    }

    static void AddRock(ItemCatalog cat, string label, string desc, RockPlacer.RockSize size, int cost, Color swatch)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name        = "Rock_" + size;
        def.displayName = label;
        def.description = desc;
        def.category    = ItemCategory.Rock;
        def.rockSize    = size;
        def.cost        = cost;
        def.swatchColor = swatch;
        SaveDef(cat, def);
    }

    static void AddBackground(ItemCatalog cat, string label, string desc, int cost, Color backdrop, Color ambient)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name         = "Bg_" + label.Replace(" ", "");
        def.displayName  = label;
        def.description  = desc;
        def.category     = ItemCategory.Background;
        def.cost         = cost;
        def.swatchColor  = backdrop;   // doubles as the solid backdrop colour
        def.ambientColor = ambient;
        SaveDef(cat, def);
    }

    static void AddTheme(ItemCatalog cat, string label, string themeId, string desc, int cost, Color accent)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name        = "Theme_" + label.Replace(" ", "");
        def.displayName = label;
        def.description = desc;
        def.category    = ItemCategory.UiTheme;
        def.cost        = cost;
        def.themeId     = themeId;
        def.swatchColor = accent;   // card preview = the theme's accent colour
        SaveDef(cat, def);
    }

    static void AddMusic(ItemCatalog cat, string label, string desc, int cost, Color swatch)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name        = "Music_" + label.Replace(" ", "");
        def.displayName = label;
        def.description = desc;
        def.category    = ItemCategory.Music;
        def.cost        = cost;
        def.swatchColor = swatch;
        // audioClip left null — author drops an AudioClip onto the generated asset.
        SaveDef(cat, def);
    }

    static void AddDecoration(ItemCatalog cat, string label, string desc, int cost, string proceduralId, Color swatch)
    {
        var def = ScriptableObject.CreateInstance<ItemDefinition>();
        def.name         = "Decor_" + label.Replace(" ", "");
        def.displayName  = label;
        def.description  = desc;
        def.category     = ItemCategory.Decoration;
        def.cost         = cost;
        def.proceduralId = proceduralId;   // "lantern" = built-in; else drop a prefab onto the asset
        def.swatchColor  = swatch;
        SaveDef(cat, def);
    }

    static void SaveDef(ItemCatalog cat, ItemDefinition def)
    {
        string path = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/{def.name}.asset");
        AssetDatabase.CreateAsset(def, path);
        cat.items.Add(def);
    }
}
