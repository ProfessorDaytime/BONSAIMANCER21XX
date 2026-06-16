using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The player's **global** progression profile — separate from the per-tree save slots
/// (`SaveManager`). It accumulates across every tree you grow: the Aesthetic Points balance,
/// which tools have been unlocked (Career mode), which cosmetic items are owned, and which
/// milestones / Journal entries have been earned.
///
/// Stored as one JSON file at `persistentDataPath/profile.json`. Lists stand in for sets
/// because `JsonUtility` can't serialize `HashSet`.
///
/// Design: `Docs/PROGRESSION_DESIGN.md`. This is Slice 1 (economy + zen core).
/// </summary>
[System.Serializable]
public class ProgressionProfile
{
    public int aestheticPoints = 0;
    public string gameMode = "Sandbox";              // last chosen mode (Career/Sandbox)

    public List<string> unlockedTools  = new List<string>();   // Career tool gates (Slice 3)
    public List<string> ownedItemIds   = new List<string>();   // purchased cosmetics (Slice 2)
    public List<string> milestones     = new List<string>();   // milestone ids reached
    public List<string> journalEntries = new List<string>();   // journal/encyclopedia ids unlocked

    // Currently-equipped cosmetic per slot (ItemDefinition.Id, "" = default). Restored on load.
    public string equippedBackground = "";
    public string equippedMusic      = "";
    public string equippedTheme      = "";
    public string equippedDecoration = "";

    /// <summary>Adds <paramref name="id"/> to a set-list if not already present. Returns true if it was new.</summary>
    public bool Add(List<string> set, string id)
    {
        if (string.IsNullOrEmpty(id) || set.Contains(id)) return false;
        set.Add(id);
        return true;
    }

    // ── Persistence ───────────────────────────────────────────────────────────
    static string FilePath => Path.Combine(Application.persistentDataPath, "profile.json");

    public static ProgressionProfile Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonUtility.FromJson<ProgressionProfile>(File.ReadAllText(FilePath))
                       ?? new ProgressionProfile();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Progression] Profile load failed: {e.Message}");
        }
        return new ProgressionProfile();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, prettyPrint: true)); }
        catch (System.Exception e) { Debug.LogWarning($"[Progression] Profile save failed: {e.Message}"); }
    }
}
