using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lets a saved menu tree point at project assets — the pot, table, rock and decoration models and
/// the textures materials use — that a player build can't read back as data (PLAN R3, player-saved
/// menu trees, 2026-10-29).
///
/// <para>A snapshot names an asset by a stable key, <c>"guid:name"</c>, which survives rebuilds and
/// game updates. In the editor the key resolves through the AssetDatabase directly. In a build it
/// resolves through the list in <c>Resources/MenuTreeRegistry</c>, which
/// <c>Assets/Editor/MenuTreeRegistryBuilder.cs</c> writes before every build from the assets the
/// build already contains, so it adds nothing new to the build.</para>
/// </summary>
public class MenuTreeRegistry : ScriptableObject
{
    public const string ResourcePath = "MenuTreeRegistry";

    [System.Serializable]
    public struct Entry
    {
        public string key;
        public Object asset;
        [Tooltip("For an unreadable model mesh: a readable copy, so saved trees can merge their " +
                 "leaves, flowers and pots into a few meshes instead of one object each.")]
        public Mesh   readableTwin;
        public string sourceHash;   // the model's import hash when the twin was made
    }

    public List<Entry> entries = new List<Entry>();

    static MenuTreeRegistry loaded;
    static Dictionary<string, Object>  byKey;
    static Dictionary<Object, string>  byAsset;
    static Dictionary<string, Mesh>    twins;

    static void Ensure()
    {
        if (byKey != null) return;
        byKey   = new Dictionary<string, Object>();
        byAsset = new Dictionary<Object, string>();
        twins   = new Dictionary<string, Mesh>();
        loaded  = Resources.Load<MenuTreeRegistry>(ResourcePath);
        if (loaded == null) return;
        foreach (var e in loaded.entries)
        {
            if (string.IsNullOrEmpty(e.key)) continue;
            if (e.readableTwin != null) twins[e.key] = e.readableTwin;
            if (e.asset == null) continue;
            byKey[e.key]     = e.asset;
            byAsset[e.asset] = e.key;
        }
    }

    /// <summary>A readable copy of the model mesh a key names, or null if the registry has none
    /// (in the editor, until <b>Bonsai ▸ Rebuild Menu Tree Registry</b> has run).</summary>
    public static Mesh ReadableTwin(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        Ensure();
        return twins.TryGetValue(key, out var m) ? m : null;
    }

    /// <summary>The stable key for a project asset, or null for a runtime-made object.</summary>
    public static string KeyOf(Object asset)
    {
        if (asset == null) return null;
#if UNITY_EDITOR
        string path = UnityEditor.AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path)) return null;
        return UnityEditor.AssetDatabase.AssetPathToGUID(path) + ":" + asset.name;
#else
        Ensure();
        return byAsset.TryGetValue(asset, out var k) ? k : null;
#endif
    }

    /// <summary>The asset a key names, or null if it no longer exists.</summary>
    public static T Resolve<T>(string key) where T : Object
    {
        if (string.IsNullOrEmpty(key)) return null;
#if UNITY_EDITOR
        int colon = key.IndexOf(':');
        if (colon < 0) return null;
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(key.Substring(0, colon));
        string name = key.Substring(colon + 1);
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is T t && o.name == name) return t;
        return null;
#else
        Ensure();
        return byKey.TryGetValue(key, out var o) ? o as T : null;
#endif
    }
}
