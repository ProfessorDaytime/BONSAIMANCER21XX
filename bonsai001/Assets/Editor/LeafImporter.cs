using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bonsai ▸ Import Leaf Model (2026-10-30): makes a leaf FBX a species' leaf in one step — what was
/// done by hand for Japanese Maple Leaf02. It:
/// <list type="bullet">
/// <item>sets the model Read/Write (the spring fold reads its midrib), keeps its own normals, and
/// maps its first material (the blade) to Leaf and a second (the stalk) to Leaf Stem;</item>
/// <item>finds the midrib (<see cref="LeafModel"/>) and builds a prefab in Assets/Prefabs/Leaves: a
/// root carrying Leaf, with the model as a child turned so the blade runs along +Z, its width
/// along Y and its top along +X;</item>
/// <item>makes it the species' main leaf, setting leafModelScale so it comes out the size of the
/// leaf it replaces, or adds it as a variant sized to match the main leaf.</item>
/// </list>
/// Model the leaf lying flat however is natural, with its origin at the stalk's twig end and the
/// blade's material first.
/// </summary>
public class LeafImporter : EditorWindow
{
    const string PrefabDir = "Assets/Prefabs/Leaves";
    const string BladeMat  = "Assets/Materials/Leaf.mat";
    const string StemMat   = "Assets/Materials/Leaf Stem.mat";
    // LeafManager's default leaf in Game001 (FicusLeaf_01_03), worn by species without their own.
    const string DefaultLeafGuid = "3a32fd031a0e3944b971fa315a0c1554";

    GameObject  model;
    TreeSpecies species;
    bool        matchSize = true;
    string      report;
    Vector2     scroll;

    [MenuItem("Bonsai/Import Leaf Model…", priority = 120)]
    static void Open() => GetWindow<LeafImporter>("Import Leaf Model");

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox(
            "Pick a leaf FBX and a species. Model the leaf lying flat, origin at the end of the stalk " +
            "where it meets the twig, blade material first and stalk material second.", MessageType.None);

        model   = (GameObject)EditorGUILayout.ObjectField("Leaf model (FBX)", model, typeof(GameObject), false);
        species = (TreeSpecies)EditorGUILayout.ObjectField("Species", species, typeof(TreeSpecies), false);
        matchSize = EditorGUILayout.Toggle(new GUIContent("Size variants to match",
            "A variant is scaled so its blade is the size of the main leaf's."), matchSize);

        bool isModel = model != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(model)) is ModelImporter;
        if (model != null && !isModel)
            EditorGUILayout.HelpBox("That's not a model file — pick the FBX itself.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(!isModel || species == null))
        {
            if (GUILayout.Button("Make it the species' main leaf"))
                report = Import(model, species, asVariant: false, matchSize);
            using (new EditorGUI.DisabledScope(species != null && species.leafPrefab == null))
                if (GUILayout.Button("Add it as a variant"))
                    report = Import(model, species, asVariant: true, matchSize);
        }
        if (species != null && species.leafPrefab == null)
            EditorGUILayout.HelpBox("This species wears the default leaf. Give it a main leaf before adding variants.",
                                    MessageType.None);

        if (species != null) DrawSpeciesLeaves(species);
        if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    void DrawSpeciesLeaves(TreeSpecies sp)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(sp.speciesName + "'s leaves", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Main", sp.leafPrefab != null ? sp.leafPrefab.name : "(default leaf)");
        EditorGUILayout.LabelField("Leaf scale", sp.leafModelScale.ToString("0.###"));
        var variants = sp.leafVariants ?? new GameObject[0];
        for (int i = 0; i < variants.Length; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Variant " + (i + 1), variants[i] != null ? variants[i].name : "(missing)");
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    Undo.RecordObject(sp, "Remove leaf variant");
                    sp.leafVariants = variants.Where((_, k) => k != i).ToArray();
                    EditorUtility.SetDirty(sp);
                    AssetDatabase.SaveAssets();
                    GUIUtility.ExitGUI();
                }
            }
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    internal static string Import(GameObject model, TreeSpecies sp, bool asVariant, bool matchSize)
    {
        string path = AssetDatabase.GetAssetPath(model);
        if (!PrepareModel(path, out string problem)) return problem;
        model = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        var filters = model.GetComponentsInChildren<MeshFilter>();
        if (filters.Length != 1)
            return $"{model.name} has {filters.Length} meshes; a leaf model should be one object (join the blade and stalk).";
        var mf = filters[0];
        if (!LeafModel.TryMidrib(mf.sharedMesh, out _, out var axis, out var up))
            return $"Couldn't find {model.name}'s midrib. Check it has normals and its blade is the first material.";

        // The child replaces the model root's own rotation and scale, so the mesh's frame within the
        // model (nothing, for a single-object FBX) is all that carries over.
        Matrix4x4 meshToRoot = Matrix4x4.identity;
        for (var t = mf.transform; t != model.transform; t = t.parent)
            meshToRoot = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * meshToRoot;
        axis = meshToRoot.MultiplyVector(axis).normalized;
        up   = meshToRoot.MultiplyVector(up).normalized;
        float rawSpan = mf.sharedMesh.GetSubMesh(0).bounds.size.magnitude * Mathf.Abs(meshToRoot.lossyScale.x);
        if (rawSpan <= 0f) return $"{model.name}'s blade is empty.";

        var main = sp.leafPrefab;
        float childScale = asVariant && matchSize && main != null ? LeafModel.BladeSpan(main) / rawSpan : 1f;
        var prefab = BuildPrefab(model, LeafModel.LayFlat(axis, up), childScale);

        Undo.RecordObject(sp, asVariant ? "Add leaf variant" : "Set main leaf");
        string done;
        if (asVariant)
        {
            var list = (sp.leafVariants ?? new GameObject[0]).ToList();
            if (!list.Contains(prefab)) list.Add(prefab);
            sp.leafVariants = list.ToArray();
            done = $"Added {prefab.name} to {sp.speciesName} as variant {list.IndexOf(prefab) + 1}" +
                   (matchSize ? $", scaled {childScale:0.###}× to match {main.name}." : ".");
        }
        else
        {
            // Keep the leaf the size it was: the old leaf's blade as spawned over the new one's.
            var   old      = main != null ? main : AssetDatabase.LoadAssetAtPath<GameObject>(
                                 AssetDatabase.GUIDToAssetPath(DefaultLeafGuid));
            float oldScale = main != null ? sp.leafModelScale : 1f;
            float oldSpan  = old != null ? LeafModel.BladeSpan(old) * oldScale : 0f;
            if (old != prefab && oldSpan > 0f) sp.leafModelScale = (float)System.Math.Round(oldSpan / rawSpan, 4);
            sp.leafPrefab = prefab;
            done = $"{prefab.name} is now {sp.speciesName}'s main leaf, leaf scale {sp.leafModelScale:0.###}" +
                   (old != null && old != prefab ? $" (the size of {old.name})." : ".");
            int resized = ResizeVariants(sp, matchSize);
            if (resized > 0) done += $" Resized {resized} variant{(resized == 1 ? "" : "s")} to match.";
        }
        EditorUtility.SetDirty(sp);
        AssetDatabase.SaveAssets();
        Debug.Log("[LeafImporter] " + done);
        return done;
    }

    /// <summary>Read/Write on, the model's own normals, blade → Leaf and stalk → Leaf Stem.</summary>
    static bool PrepareModel(string path, out string problem)
    {
        problem = null;
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        imp.isReadable    = true;
        imp.importNormals = ModelImporterNormals.Import;
        if (imp.materialImportMode == ModelImporterMaterialImportMode.None)
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        // Clear old material remaps first, so the renderer shows the file's own material names in
        // submesh order.
        foreach (var id in imp.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
            imp.RemoveRemap(id);
        imp.SaveAndReimport();

        var r = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<Renderer>();
        if (r == null) { problem = "That model has no mesh."; return false; }
        var names = r.sharedMaterials.Select(m => m != null ? m.name : null).ToArray();
        var blade = AssetDatabase.LoadAssetAtPath<Material>(BladeMat);
        var stem  = AssetDatabase.LoadAssetAtPath<Material>(StemMat);
        for (int i = 0; i < names.Length; i++)
            if (names[i] != null)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), names[i]), i == 0 ? blade : stem);
        imp.SaveAndReimport();
        return true;
    }

    /// <summary>A prefab in <see cref="PrefabDir"/> named after the model: a root with Leaf, and
    /// the model as a child turned by <paramref name="layFlat"/>. Re-importing replaces it in place.</summary>
    static GameObject BuildPrefab(GameObject model, Quaternion layFlat, float scale)
    {
        var root = new GameObject(model.name);
        root.AddComponent<Leaf>();
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = layFlat;
        child.transform.localScale    = Vector3.one * scale;

        Directory.CreateDirectory(PrefabDir);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{model.name}.prefab");
        DestroyImmediate(root);
        return prefab;
    }

    /// <summary>After a new main leaf, rescales the importer's variants (those in
    /// <see cref="PrefabDir"/>) so their blades match it again.</summary>
    static int ResizeVariants(TreeSpecies sp, bool matchSize)
    {
        if (!matchSize || sp.leafVariants == null || sp.leafPrefab == null) return 0;
        float target = LeafModel.BladeSpan(sp.leafPrefab);
        int   n      = 0;
        foreach (var v in sp.leafVariants)
        {
            string vp = v != null ? AssetDatabase.GetAssetPath(v) : null;
            if (vp == null || !vp.StartsWith(PrefabDir) || v == sp.leafPrefab) continue;
            float span = LeafModel.BladeSpan(v);
            if (span <= 0f || Mathf.Abs(span - target) < target * 0.001f) continue;
            var contents = PrefabUtility.LoadPrefabContents(vp);
            var child    = contents.transform.childCount > 0 ? contents.transform.GetChild(0) : null;
            if (child != null)
            {
                child.localScale *= target / span;
                PrefabUtility.SaveAsPrefabAsset(contents, vp);
                n++;
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }
        return n;
    }
}
