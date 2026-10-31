using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which models replace each flower and fruit part (2026-10-30), read by <see cref="FlowerParts"/>
/// from Resources/FlowerPartLibrary. Bonsai ▸ Set Up Imported Models fills it from the
/// part_&lt;name&gt;_sNN files in Assets/Prefabs/Meshes/FlowerParts; it's plain data, so a mod
/// can add parts the same way.
/// </summary>
public class FlowerPartLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("The part's id, e.g. part_cherry_petal.")]
        public string id;
        [Tooltip("Its models. The game picks one per flower; the first sets the size.")]
        public GameObject[] variants;
    }

    public List<Entry> parts = new List<Entry>();

    public GameObject[] Find(string id)
    {
        foreach (var e in parts)
            if (e.id == id) return e.variants;
        return null;
    }
}
