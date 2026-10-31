using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bonsai ▸ Set Up Imported Models (2026-10-30): everything exported from Blender into
/// Assets/Prefabs/Meshes, made live in one step.
/// <list type="bullet">
/// <item><b>Leaves/leaf_&lt;species&gt;_sNN:</b> through <see cref="LeafImporter"/>. The lowest
/// number becomes the species' main leaf (unless it already has one of these), the rest variants
/// sized to match it. Already-imported files are skipped.</item>
/// <item><b>FlowerParts/part_&lt;name&gt;_sNN:</b> set readable with their own normals and listed in
/// Resources/FlowerPartLibrary, which <see cref="FlowerParts"/> reads.</item>
/// </list>
/// Safe to run again after adding more files.
/// </summary>
public static class ModelSetup
{
    const string LeafDir     = "Assets/Prefabs/Meshes/Leaves";
    const string PartDir     = "Assets/Prefabs/Meshes/FlowerParts";
    const string PrefabDir   = "Assets/Prefabs/Leaves";
    const string LibraryPath = "Assets/Resources/FlowerPartLibrary.asset";

    static readonly Regex NumberedName = new Regex(@"^(?<id>.+)_s(?<n>\d+)$");

    [MenuItem("Bonsai/Set Up Imported Models", priority = 119)]
    static void Run()
    {
        var report = new StringBuilder();
        SetUpLeaves(report);
        SetUpParts(report);
        FlowerParts.Reset();
        Debug.Log("[ModelSetup]\n" + report);
        EditorUtility.DisplayDialog("Set Up Imported Models", report.ToString(), "OK");
    }

    /// <summary>The .fbx files in <paramref name="dir"/> named &lt;id&gt;_sNN, grouped by id and
    /// sorted by number.</summary>
    static Dictionary<string, List<string>> Numbered(string dir)
    {
        var groups = new Dictionary<string, List<(int n, string path)>>();
        if (!Directory.Exists(dir)) return new Dictionary<string, List<string>>();
        foreach (var file in Directory.GetFiles(dir, "*.fbx"))
        {
            var m = NumberedName.Match(Path.GetFileNameWithoutExtension(file));
            if (!m.Success) continue;
            string id = m.Groups["id"].Value;
            if (!groups.TryGetValue(id, out var list)) groups[id] = list = new List<(int, string)>();
            list.Add((int.Parse(m.Groups["n"].Value), $"{dir}/{Path.GetFileName(file)}"));
        }
        return groups.ToDictionary(g => g.Key, g => g.Value.OrderBy(x => x.n).Select(x => x.path).ToList());
    }

    static void SetUpLeaves(StringBuilder report)
    {
        var species = AssetDatabase.FindAssets("t:TreeSpecies")
            .Select(g => AssetDatabase.LoadAssetAtPath<TreeSpecies>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(s => s != null && !string.IsNullOrEmpty(s.speciesName))
            .GroupBy(s => "leaf_" + s.speciesName.ToLowerInvariant().Replace(' ', '_'))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var group in Numbered(LeafDir))
        {
            if (!species.TryGetValue(group.Key, out var sp))
            {
                report.AppendLine($"No species matches {group.Key}_sNN (expected leaf_<species name>, e.g. leaf_japanese_maple).");
                continue;
            }
            foreach (var path in group.Value)
            {
                string name   = Path.GetFileNameWithoutExtension(path);
                var    model  = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var    prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");
                bool   isMain = sp.leafPrefab != null && sp.leafPrefab == prefab;
                bool   isVar  = sp.leafVariants != null && prefab != null && sp.leafVariants.Contains(prefab);
                if (model == null || isMain || isVar) continue;
                // The species' main leaf is the first of these files, unless it already wears one.
                bool hasOwn = sp.leafPrefab != null && sp.leafPrefab.name.StartsWith(group.Key + "_s");
                report.AppendLine(LeafImporter.Import(model, sp, asVariant: hasOwn, matchSize: true));
            }
        }
    }

    static void SetUpParts(StringBuilder report)
    {
        var groups = Numbered(PartDir);
        foreach (var path in groups.Values.SelectMany(p => p))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null || (imp.isReadable && imp.importNormals == ModelImporterNormals.Import)) continue;
            imp.isReadable    = true;
            imp.importNormals = ModelImporterNormals.Import;
            imp.SaveAndReimport();
        }

        var lib = AssetDatabase.LoadAssetAtPath<FlowerPartLibrary>(LibraryPath);
        if (lib == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            lib = ScriptableObject.CreateInstance<FlowerPartLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }
        Undo.RecordObject(lib, "Set up flower parts");
        // Entries for files in the folder are rebuilt; anything else in the library (a mod's) stays.
        lib.parts.RemoveAll(e => groups.ContainsKey(e.id));
        foreach (var g in groups.OrderBy(g => g.Key))
        {
            var models = g.Value.Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(m => m != null).ToArray();
            lib.parts.Add(new FlowerPartLibrary.Entry { id = g.Key, variants = models });
            report.AppendLine($"{g.Key}: {models.Length} variant{(models.Length == 1 ? "" : "s")}");
        }
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
    }
}
