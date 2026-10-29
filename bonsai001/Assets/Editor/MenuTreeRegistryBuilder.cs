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
/// <para>Runs before every build automatically, and from <b>Bonsai ▸ Rebuild Menu Tree Registry</b>.
/// Play mode in the editor doesn't need it; keys resolve through the AssetDatabase there.</para>
/// </summary>
public class MenuTreeRegistryBuilder : IPreprocessBuildWithReport
{
    const string OutPath = "Assets/Resources/" + MenuTreeRegistry.ResourcePath + ".asset";

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

        var entries = new List<MenuTreeRegistry.Entry>();
        var seen    = new HashSet<string>();
        foreach (string path in AssetDatabase.GetDependencies(roots.ToArray(), true).Distinct())
        {
            if (path == OutPath || path.EndsWith(".cs") || path.EndsWith(".unity")) continue;
            string guid = AssetDatabase.AssetPathToGUID(path);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(o is Mesh) && !(o is Texture)) continue;
                string key = guid + ":" + o.name;
                if (seen.Add(key)) entries.Add(new MenuTreeRegistry.Entry { key = key, asset = o });
            }
        }

        var reg = AssetDatabase.LoadAssetAtPath<MenuTreeRegistry>(OutPath);
        if (reg == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            reg = ScriptableObject.CreateInstance<MenuTreeRegistry>();
            AssetDatabase.CreateAsset(reg, OutPath);
        }
        reg.entries = entries;
        EditorUtility.SetDirty(reg);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MenuTreeRegistry] {entries.Count} meshes and textures registered → {OutPath}");
    }
}
