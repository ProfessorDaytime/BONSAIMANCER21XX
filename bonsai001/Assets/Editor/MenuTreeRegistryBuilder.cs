using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Writes <c>Assets/Resources/MenuTreeRegistry.asset</c> (see <see cref="MenuTreeRegistry"/>): every
/// mesh and texture the build already contains — the scenes in Build Settings plus everything under
/// a Resources folder, with their dependencies — keyed by <c>"guid:name"</c>. A player build uses it
/// to turn a saved menu tree's model and texture references back into assets.
///
/// <para>Small model meshes that aren't Read/Write (leaves, flowers, pots, tables) also get a
/// readable copy in <c>Assets/MenuTreeReadable/</c>, so a saved tree's thousands of leaves can be
/// merged into a few meshes when the menu or greenhouse shows it. The copies are made the same way
/// as in <c>MenuTreeBaker</c> — the model file is copied to a temp folder and the copy imported
/// readable — so the project's own import settings, and the game, are untouched. A copy is reused
/// until its model's import hash changes.</para>
///
/// <para>Runs before every build automatically, and from <b>Bonsai ▸ Rebuild Menu Tree Registry</b>.
/// Run it once in the editor too, so Play mode gets the readable copies.</para>
/// </summary>
public class MenuTreeRegistryBuilder : IPreprocessBuildWithReport
{
    const string OutPath  = "Assets/Resources/" + MenuTreeRegistry.ResourcePath + ".asset";
    const string TwinDir  = "Assets/MenuTreeReadable";
    const string TempDir  = "Assets/_MenuTreeRegistryTemp";
    const int    MaxTwinVerts = 30000;

    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => Rebuild();

    [MenuItem("Bonsai/Rebuild Menu Tree Registry", priority = 112)]
    public static void Rebuild()
    {
        var roots = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled && !string.IsNullOrEmpty(s.path)) roots.Add(s.path);
        foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Replace('\\', '/').Contains("/Resources/") && !AssetDatabase.IsValidFolder(path) && path != OutPath)
                roots.Add(path);
        }

        var reg = AssetDatabase.LoadAssetAtPath<MenuTreeRegistry>(OutPath);
        var oldTwins = new Dictionary<string, MenuTreeRegistry.Entry>();
        if (reg != null)
            foreach (var e in reg.entries)
                if (e.readableTwin != null) oldTwins[e.key] = e;

        var entries   = new List<MenuTreeRegistry.Entry>();
        var seen      = new HashSet<string>();
        var keptTwins = new HashSet<string>();
        int made = 0, reused = 0;
        try
        {
            foreach (string path in AssetDatabase.GetDependencies(roots.ToArray(), true).Distinct())
            {
                if (path == OutPath || path.StartsWith(TwinDir) || path.EndsWith(".cs") || path.EndsWith(".unity")) continue;
                string guid   = AssetDatabase.AssetPathToGUID(path);
                bool   isModel = AssetImporter.GetAtPath(path) is ModelImporter;
                string hash   = isModel ? AssetDatabase.GetAssetDependencyHash(path).ToString() : null;

                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(o is Mesh) && !(o is Texture)) continue;
                    string key = guid + ":" + o.name;
                    if (!seen.Add(key)) continue;
                    var entry = new MenuTreeRegistry.Entry { key = key, asset = o };

                    if (o is Mesh mesh && isModel && !mesh.isReadable && mesh.vertexCount <= MaxTwinVerts)
                    {
                        if (oldTwins.TryGetValue(key, out var old) && old.sourceHash == hash)
                        {
                            entry.readableTwin = old.readableTwin;
                            reused++;
                        }
                        else
                        {
                            entry.readableTwin = MakeTwin(path, mesh, guid);
                            if (entry.readableTwin != null) made++;
                        }
                        entry.sourceHash = hash;
                        if (entry.readableTwin != null) keptTwins.Add(AssetDatabase.GetAssetPath(entry.readableTwin));
                    }
                    entries.Add(entry);
                }
            }
        }
        finally
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        // Drop copies of models no longer in the build.
        if (AssetDatabase.IsValidFolder(TwinDir))
            foreach (var g in AssetDatabase.FindAssets("t:Mesh", new[] { TwinDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!keptTwins.Contains(p)) AssetDatabase.DeleteAsset(p);
            }

        if (reg == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            reg = ScriptableObject.CreateInstance<MenuTreeRegistry>();
            AssetDatabase.CreateAsset(reg, OutPath);
        }
        reg.entries = entries;
        EditorUtility.SetDirty(reg);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MenuTreeRegistry] {entries.Count} meshes and textures registered; readable copies: " +
                  $"{made} made, {reused} reused → {OutPath}");
    }

    /// <summary>A readable copy of a model's mesh, saved under <see cref="TwinDir"/>.</summary>
    static Mesh MakeTwin(string modelPath, Mesh mesh, string guid)
    {
        if (!AssetDatabase.IsValidFolder(TempDir))
            AssetDatabase.CreateFolder("Assets", TempDir.Substring("Assets/".Length));
        string tmp = $"{TempDir}/{guid}{Path.GetExtension(modelPath)}";
        if (AssetDatabase.LoadMainAssetAtPath(tmp) == null)
        {
            AssetDatabase.CopyAsset(modelPath, tmp);
            var imp = (ModelImporter)AssetImporter.GetAtPath(tmp);
            imp.isReadable = true;
            imp.SaveAndReimport();
        }

        Mesh src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(tmp))
            if (o is Mesh m && m.name == mesh.name) { src = m; break; }
        if (src == null) return null;

        if (!AssetDatabase.IsValidFolder(TwinDir))
            AssetDatabase.CreateFolder("Assets", TwinDir.Substring("Assets/".Length));
        var twin = Object.Instantiate(src);
        twin.name = mesh.name;
        string safe = mesh.name;
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        string outPath = $"{TwinDir}/{guid}_{safe}.asset";
        if (AssetDatabase.LoadMainAssetAtPath(outPath) != null) AssetDatabase.DeleteAsset(outPath);
        AssetDatabase.CreateAsset(twin, outPath);
        return twin;
    }
}
