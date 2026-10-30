using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The player's saved menu trees, one per playthrough (2026-10-29). Each lives in
/// <c>persistentDataPath/menutrees/&lt;save slot id&gt;/</c> as <c>tree.bin</c> (the
/// <see cref="MenuTreeSnapshot"/>), <c>thumb.png</c> and <c>meta.json</c>.
///
/// <para>Quitting a playthrough overwrites that playthrough's tree. The main menu shows the most
/// recently saved one. At most <see cref="MaxTrees"/> are kept; when that's full, a playthrough
/// that has no tree yet can only add one after the player deletes another. Deleting a save keeps
/// its menu tree.</para>
/// </summary>
public static class MenuTreeLibrary
{
    public const int MaxTrees = 50;

    [Serializable]
    public class Meta
    {
        public string slotId;
        public string treeName;
        public string speciesName;
        public int    treeAge;
        public string savedUtc;   // ISO 8601, sorts newest first as a string
        public bool   favorite;   // marked in the greenhouse; what favourites do is still open
        // For the greenhouse season slider (2026-10-29). Trees saved before then have none: alpha 0.
        public Color  autumnColor;
        public bool   evergreen;
    }

    const string PinnedKey = "MenuTreePinned";

    /// <summary>The tree the player chose for the main menu in the greenhouse, or null to show the
    /// most recent. A pin to a deleted tree is ignored.</summary>
    public static string Pinned
    {
        get { string v = PlayerPrefs.GetString(PinnedKey, ""); return Has(v) ? v : null; }
        set
        {
            if (string.IsNullOrEmpty(value)) PlayerPrefs.DeleteKey(PinnedKey);
            else PlayerPrefs.SetString(PinnedKey, value);
            PlayerPrefs.Save();
        }
    }

    public static string Root => Path.Combine(Application.persistentDataPath, "menutrees");

    static string Dir(string slotId)       => Path.Combine(Root, slotId);
    static string TreePath(string slotId)  => Path.Combine(Dir(slotId), "tree.bin");
    static string ThumbPath(string slotId) => Path.Combine(Dir(slotId), "thumb.png");
    static string MetaPath(string slotId)  => Path.Combine(Dir(slotId), "meta.json");

    /// <summary>Every saved tree, newest first.</summary>
    public static List<Meta> List()
    {
        var list = new List<Meta>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.GetDirectories(Root))
        {
            string metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath) || !File.Exists(Path.Combine(dir, "tree.bin"))) continue;
            try
            {
                var m = JsonUtility.FromJson<Meta>(File.ReadAllText(metaPath));
                if (m != null && !string.IsNullOrEmpty(m.slotId)) list.Add(m);
            }
            catch (Exception e) { Debug.LogWarning($"[MenuTree] Bad meta in {dir}: {e.Message}"); }
        }
        list.Sort((a, b) => string.CompareOrdinal(b.savedUtc, a.savedUtc));
        return list;
    }

    public static int  Count => List().Count;
    public static bool Has(string slotId) => !string.IsNullOrEmpty(slotId) && File.Exists(TreePath(slotId));

    /// <summary>True when saving this playthrough's tree needs another deleted first.</summary>
    public static bool NeedsRoomFor(string slotId) => !Has(slotId) && Count >= MaxTrees;

    /// <summary>Captures the live tree as this playthrough's menu tree. Returns false if there was
    /// nothing to capture, the library is full, or writing failed.</summary>
    public static bool SaveCurrent(string slotId, TreeSkeleton skeleton)
    {
        if (string.IsNullOrEmpty(slotId) || skeleton == null || skeleton.root == null) return false;
        if (NeedsRoomFor(slotId))
        {
            Debug.Log($"[MenuTree] Library full ({MaxTrees}); not saving a tree for {slotId}.");
            return false;
        }
        try
        {
            byte[] thumb = MenuTreeCapture.Thumbnail();
            var snap = MenuTreeCapture.Capture(skeleton);
            if (snap == null) return false;

            Directory.CreateDirectory(Dir(slotId));
            snap.Write(TreePath(slotId));
            if (thumb != null) File.WriteAllBytes(ThumbPath(slotId), thumb);
            var meta = new Meta
            {
                favorite    = ReadMeta(slotId)?.favorite ?? false,   // a re-save keeps the mark
                slotId      = slotId,
                treeName    = skeleton.treeName,
                speciesName = skeleton.SpeciesName,
                treeAge     = GameManager.year - skeleton.SaveStartYear,
                autumnColor = skeleton.species != null ? skeleton.species.leafAutumnColor : default,
                evergreen   = skeleton.species != null && skeleton.species.evergreen,
                savedUtc    = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(MetaPath(slotId), JsonUtility.ToJson(meta, true));
            Debug.Log($"[MenuTree] Saved menu tree for {slotId} ({new FileInfo(TreePath(slotId)).Length / 1024} KB).");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MenuTree] Couldn't save the menu tree: {e}");
            return false;
        }
    }

    static Meta ReadMeta(string slotId)
    {
        try { return File.Exists(MetaPath(slotId)) ? JsonUtility.FromJson<Meta>(File.ReadAllText(MetaPath(slotId))) : null; }
        catch { return null; }
    }

    public static void SetFavorite(string slotId, bool favorite)
    {
        var m = ReadMeta(slotId);
        if (m == null) return;
        m.favorite = favorite;
        File.WriteAllText(MetaPath(slotId), JsonUtility.ToJson(m, true));
    }

    public static MenuTreeSnapshot Load(string slotId) =>
        string.IsNullOrEmpty(slotId) ? null : MenuTreeSnapshot.Read(TreePath(slotId));

    /// <summary>What the main menu shows: the pinned tree, else the most recent.</summary>
    public static MenuTreeSnapshot LoadForMenu()
    {
        var pinned = Pinned;
        if (pinned != null)
        {
            var s = Load(pinned);
            if (s != null) return s;
        }
        return LoadMostRecent();
    }

    public static void Delete(string slotId)
    {
        if (string.IsNullOrEmpty(slotId)) return;
        if (PlayerPrefs.GetString(PinnedKey, "") == slotId) Pinned = null;
        try
        {
            if (Directory.Exists(Dir(slotId))) Directory.Delete(Dir(slotId), true);
            Debug.Log($"[MenuTree] Deleted menu tree {slotId}.");
        }
        catch (Exception e) { Debug.LogWarning($"[MenuTree] Couldn't delete {slotId}: {e.Message}"); }
    }

    /// <summary>The most recently saved tree that still reads, or null.</summary>
    public static MenuTreeSnapshot LoadMostRecent()
    {
        foreach (var m in List())
        {
            var s = MenuTreeSnapshot.Read(TreePath(m.slotId));
            if (s != null) return s;
        }
        return null;
    }

    /// <summary>The gallery picture for a tree, or null. The caller destroys it.</summary>
    public static Texture2D LoadThumbnail(string slotId)
    {
        string p = ThumbPath(slotId);
        if (!File.Exists(p)) return null;
        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(File.ReadAllBytes(p))) { UnityEngine.Object.Destroy(tex); return null; }
        return tex;
    }
}
