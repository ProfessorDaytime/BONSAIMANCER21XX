using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ── Enums ─────────────────────────────────────────────────────────────────────

public enum TreeOrigin { Seedling, Cutting, AirLayer }

// ── Data classes ──────────────────────────────────────────────────────────────

[System.Serializable]
public class SaveWeed
{
    public float px, py, pz;
    public int   weedType;
    public bool  isRipped;
    public float forceRequired;
    public float ripChance;
}

[System.Serializable]
public class SaveNode
{
    // Identity
    public int   id;
    public int   depth;
    public int   parentId;

    // Geometry
    public float px, py, pz;
    public float dx, dy, dz;
    public float radius;
    public float minRadius;
    public float length;
    public float targetLength;

    // Growth
    public bool  isGrowing;
    public float age;

    // State flags
    public bool  isTrimmed;
    public bool  hasLeaves;
    public bool  isRoot;
    public int   subdivisionsLeft;
    public int   birthYear;
    public float refinementLevel;
    public float branchVigor;

    // Bud
    public bool hasBud;
    public bool backBudStimulated;

    // Post-trim regrowth cap
    public bool isTrimCutPoint;
    public int  trimCutDepth;
    public int  regrowthSeasonCount;

    // Health
    public float health;

    // Branch weight
    public float branchLoad;
    public float sagAngleDeg;

    // Dieback
    public bool isDead;
    public bool isDeadwood;
    public int  shadedSeasons;
    public int  deadSeasons;

    // Fungal
    public float fungalLoad;
    public bool  isMycorrhizal;
    public int   healthySeasonsCount;

    // Wire
    public bool  hasWire;
    public float woX, woY, woZ;
    public float wtX, wtY, wtZ;
    public float wireSetProgress;
    public float wireDamageProgress;
    public float wireAgeDays;
    public bool  isTrainingWire;

    // Pot-bound
    public int boundaryPressure;

    // Air layer
    public bool isAirLayerRoot;

    // Wound
    public bool  hasWound;
    public float woundRadius;
    public float wnX, wnY, wnZ;
    public float woundAge;
    public bool  pasteApplied;
}

[System.Serializable]
public class SaveData
{
    // Tree nodes
    public List<SaveNode> nodes = new List<SaveNode>();

    // GameManager time state
    public int   year;
    public int   month;
    public int   day;
    public float hour;
    public int   waterings;

    // TreeSkeleton live state
    public float treeEnergy;
    public float soilMoisture;
    public float droughtDaysAccumulated;
    public float nutrientReserve;

    // Weed state
    public List<SaveWeed> weeds = new List<SaveWeed>();

    // Soil state
    public float soilAkadama;
    public float soilPumice;
    public float soilLavaRock;
    public float soilOrganic;
    public float soilSand;
    public float soilKanuma;
    public float soilPerlite;
    public float soilDegradation;
    public float soilSaturation;
    public int   soilSeasonsSinceRepot;
    public int   soilPreset;
    public int   potSize;   // PotSoil.PotSize cast to int

    public int   startYear;
    public int   startMonth;
    public int   lastGrownYear;
    public bool  isIshitsukiMode;

    // Planting surface
    public float planNX, planNY, planNZ;
    public float planPX, planPY, planPZ;

    // Save identity (written by SaveSlot; blank on old saves)
    public string saveName;
    public int    treeOrigin;   // TreeOrigin cast to int
    public string speciesName;
    public string saveTimestamp;
}

/// <summary>
/// Lightweight metadata written alongside each save slot.
/// Used to populate the load menu without reading the full save.json.
/// </summary>
[System.Serializable]
public class SaveMeta
{
    public string slotId;
    public string saveName;
    public int    treeOrigin;   // TreeOrigin cast to int
    public string speciesName;
    public int    year;
    public int    month;
    public string saveTimestamp;   // "yyyyMMdd_HHmmss" UTC — sorts lexicographically
    public int    seasonsSinceRepot;
    public int    nodeCount;
    public float  avgHealth;   // 0–1; average health of living non-root nodes
    public int    treeAge;     // years since first planted (year - startYear)
}

// ── SaveManager ───────────────────────────────────────────────────────────────

public static class SaveManager
{
    // ── Paths ─────────────────────────────────────────────────────────────────

    /// <summary>Root folder that contains one sub-folder per save slot.</summary>
    public static string SavesRoot => Path.Combine(Application.persistentDataPath, "saves");

    static string SlotDir(string slotId)      => Path.Combine(SavesRoot, slotId);
    static string SlotSavePath(string slotId) => Path.Combine(SlotDir(slotId), "save.json");
    static string SlotMetaPath(string slotId) => Path.Combine(SlotDir(slotId), "meta.json");

    // Legacy single-file paths (kept for migration only).
    static string LegacySavePath     => Path.Combine(Application.persistentDataPath, "bonsai_save.json");
    static string LegacyOriginalPath => Path.Combine(Application.persistentDataPath, "bonsai_original.json");

    // Special slot that stores the pre-sever original; excluded from ListAllSaves.
    static string OriginalSlotDir  => Path.Combine(SavesRoot, "_original");
    static string OriginalSavePath => Path.Combine(OriginalSlotDir, "save.json");

    // ── Active slot ───────────────────────────────────────────────────────────

    static string _activeSlotId;

    /// <summary>
    /// The slot that the quick-save button writes to.
    /// Persisted in PlayerPrefs across sessions.
    /// Null when no slot is active (new game not yet saved).
    /// </summary>
    public static string ActiveSlotId
    {
        get
        {
            if (_activeSlotId == null)
            {
                string v = PlayerPrefs.GetString("BonsaiActiveSlot", "");
                _activeSlotId = string.IsNullOrEmpty(v) ? "" : v;
            }
            return string.IsNullOrEmpty(_activeSlotId) ? null : _activeSlotId;
        }
        set
        {
            _activeSlotId = value ?? "";
            if (string.IsNullOrEmpty(_activeSlotId))
                PlayerPrefs.DeleteKey("BonsaiActiveSlot");
            else
                PlayerPrefs.SetString("BonsaiActiveSlot", _activeSlotId);
            PlayerPrefs.Save();
        }
    }

    // ── Slot ID factory ───────────────────────────────────────────────────────

    public static string NewSlotId() =>
        DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

    // ── Has any save ──────────────────────────────────────────────────────────

    /// <summary>
    /// True if at least one named save slot exists (including after migration).
    /// Call this at startup to decide whether to show the load menu.
    /// </summary>
    public static bool HasAnySave()
    {
        MigrateLegacyIfNeeded();
        if (!Directory.Exists(SavesRoot)) return false;
        foreach (var dir in Directory.GetDirectories(SavesRoot))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("_")) continue;   // skip special slots
            if (File.Exists(Path.Combine(dir, "meta.json"))) return true;
        }
        return false;
    }

    // ── Migration ─────────────────────────────────────────────────────────────

    /// <summary>
    /// If the legacy single-file save exists and no slot folder has been created yet,
    /// moves the legacy file into a new slot so it appears in the load menu.
    /// </summary>
    public static void MigrateLegacyIfNeeded()
    {
        if (!File.Exists(LegacySavePath)) return;

        // Already migrated if any non-special slot folder exists.
        if (Directory.Exists(SavesRoot))
        {
            foreach (var d in Directory.GetDirectories(SavesRoot))
                if (!Path.GetFileName(d).StartsWith("_")) return;
        }

        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(LegacySavePath));
            if (data == null) return;

            string slotId = "legacy_" + NewSlotId();
            Directory.CreateDirectory(SlotDir(slotId));
            File.Copy(LegacySavePath, SlotSavePath(slotId), overwrite: true);

            var meta = new SaveMeta
            {
                slotId            = slotId,
                saveName          = "Bonsai",
                treeOrigin        = 0,
                speciesName       = string.IsNullOrEmpty(data.speciesName) ? "Unknown" : data.speciesName,
                year              = data.year,
                month             = data.month,
                saveTimestamp     = "20000101_000000",   // sorts to bottom
                seasonsSinceRepot = data.soilSeasonsSinceRepot,
                nodeCount         = data.nodes?.Count ?? 0,
            };
            File.WriteAllText(SlotMetaPath(slotId), JsonUtility.ToJson(meta, true));

            // Migrate the original backup if it exists.
            if (File.Exists(LegacyOriginalPath))
            {
                Directory.CreateDirectory(OriginalSlotDir);
                File.Copy(LegacyOriginalPath, OriginalSavePath, overwrite: true);
                File.Delete(LegacyOriginalPath);
            }

            File.Delete(LegacySavePath);
            ActiveSlotId = slotId;
            Debug.Log($"[Save] Migrated legacy save → slot {slotId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Save] Migration failed: {e.Message}");
        }
    }

    // ── List saves ────────────────────────────────────────────────────────────

    /// <summary>Returns all save slots sorted by timestamp descending (newest first).</summary>
    public static List<SaveMeta> ListAllSaves()
    {
        MigrateLegacyIfNeeded();
        var result = new List<SaveMeta>();
        if (!Directory.Exists(SavesRoot)) return result;

        foreach (var dir in Directory.GetDirectories(SavesRoot))
        {
            if (Path.GetFileName(dir).StartsWith("_")) continue;
            string metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var meta = JsonUtility.FromJson<SaveMeta>(File.ReadAllText(metaPath));
                if (meta != null) result.Add(meta);
            }
            catch { /* skip corrupt meta */ }
        }

        result.Sort((a, b) =>
            string.Compare(b.saveTimestamp, a.saveTimestamp, StringComparison.Ordinal));
        return result;
    }

    // ── Save slot ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the tree into the given slot, writing both save.json and meta.json.
    /// Sets <see cref="ActiveSlotId"/> to this slot.
    /// </summary>
    public static void SaveSlot(string slotId, TreeSkeleton skeleton,
                                LeafManager leafManager, SaveMeta meta)
    {
        if (skeleton == null || skeleton.root == null)
        {
            Debug.LogWarning("[Save] SaveSlot: no tree to save.");
            return;
        }

        Directory.CreateDirectory(SlotDir(slotId));

        var data           = BuildSaveData(skeleton);
        data.saveName      = meta.saveName;
        data.treeOrigin    = meta.treeOrigin;
        data.speciesName   = meta.speciesName;
        data.saveTimestamp = meta.saveTimestamp;

        File.WriteAllText(SlotSavePath(slotId), JsonUtility.ToJson(data, prettyPrint: true));
        File.WriteAllText(SlotMetaPath(slotId), JsonUtility.ToJson(meta, prettyPrint: true));

        ActiveSlotId = slotId;
        Debug.Log($"[Save] Saved slot={slotId} name='{meta.saveName}' nodes={data.nodes.Count}");
    }

    // ── Autosave ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves to the active slot. If no slot exists yet, creates one automatically
    /// with a default name so the game is never silently lost.
    /// </summary>
    public static void AutoSave(TreeSkeleton skeleton, LeafManager leafManager = null)
    {
        if (skeleton == null || skeleton.root == null) return;

        if (string.IsNullOrEmpty(ActiveSlotId))
        {
            string slotId = NewSlotId();
            string name   = (skeleton.SpeciesName ?? "Bonsai") + " " + GameManager.year + " (autosave)";
            var meta = new SaveMeta
            {
                slotId            = slotId,
                saveName          = name,
                treeOrigin        = (int)skeleton.treeOrigin,
                speciesName       = skeleton.SpeciesName,
                year              = GameManager.year,
                month             = GameManager.month,
                saveTimestamp     = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"),
                nodeCount         = skeleton.allNodes?.Count ?? 0,
                seasonsSinceRepot = skeleton.GetComponent<PotSoil>()?.seasonsSinceRepot ?? 0,
                treeAge           = GameManager.year - skeleton.SaveStartYear,
                avgHealth         = CalcAvgHealth(skeleton),
            };
            SaveSlot(slotId, skeleton, leafManager, meta);
            Debug.Log($"[Save] AutoSave created new slot '{name}' id={slotId}");
            return;
        }

        Save(skeleton, leafManager);
    }

    // ── Quick-save (to active slot) ───────────────────────────────────────────

    /// <summary>
    /// Quick-saves to the current active slot.
    /// Returns false (does nothing) if no active slot is set — caller must prompt for a name.
    /// </summary>
    public static bool Save(TreeSkeleton skeleton, LeafManager leafManager = null)
    {
        string slotId = ActiveSlotId;
        if (string.IsNullOrEmpty(slotId))
        {
            Debug.LogWarning("[Save] Save() called with no active slot — prompt for a name first.");
            return false;
        }

        // Read existing meta so we preserve the save name and origin.
        SaveMeta meta;
        string metaPath = SlotMetaPath(slotId);
        if (File.Exists(metaPath))
        {
            meta = JsonUtility.FromJson<SaveMeta>(File.ReadAllText(metaPath)) ?? new SaveMeta();
        }
        else
        {
            meta = new SaveMeta
            {
                saveName    = skeleton.SpeciesName + " " + GameManager.year,
                treeOrigin  = (int)skeleton.treeOrigin,
                speciesName = skeleton.SpeciesName,
            };
        }

        // Always refresh dynamic fields.
        meta.slotId            = slotId;
        meta.year              = GameManager.year;
        meta.month             = GameManager.month;
        meta.saveTimestamp     = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        meta.nodeCount         = skeleton.allNodes?.Count ?? 0;
        meta.treeOrigin        = (int)skeleton.treeOrigin;
        meta.speciesName       = skeleton.SpeciesName;
        meta.seasonsSinceRepot = skeleton.GetComponent<PotSoil>()?.seasonsSinceRepot ?? 0;
        meta.treeAge           = GameManager.year - skeleton.SaveStartYear;
        meta.avgHealth         = CalcAvgHealth(skeleton);
        if (string.IsNullOrEmpty(meta.saveName))
            meta.saveName = skeleton.SpeciesName + " " + GameManager.year;

        SaveSlot(slotId, skeleton, leafManager, meta);
        return true;
    }

    // ── Load slot ─────────────────────────────────────────────────────────────

    /// <summary>Loads the named slot into the skeleton. Sets ActiveSlotId on success.</summary>
    public static bool LoadSlot(string slotId, TreeSkeleton skeleton,
                                LeafManager leafManager = null)
    {
        string path = SlotSavePath(slotId);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[Save] LoadSlot: no save.json at slot={slotId}");
            return false;
        }

        SaveData data;
        try { data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path)); }
        catch (Exception e) { Debug.LogError($"[Save] LoadSlot parse error: {e.Message}"); return false; }

        if (data == null || data.nodes == null || data.nodes.Count == 0)
        {
            Debug.LogWarning($"[Save] LoadSlot: slot={slotId} is empty or corrupt.");
            return false;
        }

        GameManager.year      = data.year;
        GameManager.month     = data.month;
        GameManager.day       = data.day;
        GameManager.hour      = data.hour;
        GameManager.waterings = data.waterings;

        skeleton.LoadFromSaveData(data, leafManager);
        skeleton.treeOrigin = (TreeOrigin)data.treeOrigin;

        ActiveSlotId = slotId;
        Debug.Log($"[Save] Loaded slot={slotId} nodes={data.nodes.Count} year={data.year}");
        return true;
    }

    // ── Load (active slot) ────────────────────────────────────────────────────

    /// <summary>Loads the active slot. Returns false if no active slot.</summary>
    public static bool Load(TreeSkeleton skeleton, LeafManager leafManager = null)
    {
        string slotId = ActiveSlotId;
        if (string.IsNullOrEmpty(slotId))
        {
            Debug.LogWarning("[Save] Load() called with no active slot.");
            return false;
        }
        return LoadSlot(slotId, skeleton, leafManager);
    }

    // ── Delete slot ───────────────────────────────────────────────────────────

    public static void DeleteSlot(string slotId)
    {
        string dir = SlotDir(slotId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            Debug.Log($"[Save] Deleted slot={slotId}");
        }
        if (ActiveSlotId == slotId)
            ActiveSlotId = null;
    }

    // ── Original-tree backup (air-layer sever) ────────────────────────────────

    public static bool OriginalExists() => File.Exists(OriginalSavePath);

    public static void SaveOriginal(TreeSkeleton skeleton, LeafManager leafManager = null)
    {
        if (skeleton == null || skeleton.root == null)
        {
            Debug.LogWarning("[Save] SaveOriginal: no tree to save.");
            return;
        }
        Directory.CreateDirectory(OriginalSlotDir);
        var data = BuildSaveData(skeleton);
        data.saveTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        File.WriteAllText(OriginalSavePath, JsonUtility.ToJson(data, prettyPrint: true));
        Debug.Log($"[Save] Original backed up → {OriginalSavePath}");
    }

    public static bool LoadOriginal(TreeSkeleton skeleton, LeafManager leafManager = null)
    {
        if (!File.Exists(OriginalSavePath))
        {
            Debug.Log("[Save] No original backup found.");
            return false;
        }
        SaveData data;
        try { data = JsonUtility.FromJson<SaveData>(File.ReadAllText(OriginalSavePath)); }
        catch { Debug.LogWarning("[Save] Original backup corrupt."); return false; }

        if (data == null || data.nodes == null || data.nodes.Count == 0)
        {
            Debug.LogWarning("[Save] Original backup empty.");
            return false;
        }

        GameManager.year      = data.year;
        GameManager.month     = data.month;
        GameManager.day       = data.day;
        GameManager.hour      = data.hour;
        GameManager.waterings = data.waterings;

        skeleton.LoadFromSaveData(data, leafManager);
        skeleton.treeOrigin = (TreeOrigin)data.treeOrigin;
        Debug.Log($"[Save] Original tree restored: {data.nodes.Count} nodes");
        return true;
    }

    // ── Legacy compat shims ───────────────────────────────────────────────────

    public static bool SaveExists() => HasAnySave();

    // ── Health helper ─────────────────────────────────────────────────────────

    public static float CalcAvgHealth(TreeSkeleton skeleton)
    {
        float sum = 0f; int count = 0;
        if (skeleton.allNodes == null) return 1f;
        foreach (var n in skeleton.allNodes)
            if (!n.isRoot && !n.isDead) { sum += n.health; count++; }
        return count > 0 ? sum / count : 1f;
    }

    // ── Core serialization helpers ────────────────────────────────────────────

    /// <summary>
    /// Builds a SaveData from the current skeleton state without writing to disk.
    /// Does not fill saveName / treeOrigin / speciesName / saveTimestamp — caller sets those.
    /// </summary>
    public static SaveData BuildSaveData(TreeSkeleton skeleton)
    {
        var potSoil  = skeleton.GetComponent<PotSoil>();
        var weedMgr  = skeleton.GetComponent<WeedManager>();

        var data = new SaveData
        {
            year      = GameManager.year,
            month     = GameManager.month,
            day       = GameManager.day,
            hour      = GameManager.hour,
            waterings = GameManager.waterings,

            treeEnergy             = skeleton.treeEnergy,
            soilMoisture           = skeleton.soilMoisture,
            droughtDaysAccumulated = skeleton.droughtDaysAccumulated,
            nutrientReserve        = skeleton.nutrientReserve,

            weeds = weedMgr?.GetSaveState() ?? new List<SaveWeed>(),

            soilAkadama           = potSoil?.akadama           ?? 0.5f,
            soilPumice            = potSoil?.pumice            ?? 0.3f,
            soilLavaRock          = potSoil?.lavaRock          ?? 0.2f,
            soilOrganic           = potSoil?.organic           ?? 0f,
            soilSand              = potSoil?.sand              ?? 0f,
            soilKanuma            = potSoil?.kanuma            ?? 0f,
            soilPerlite           = potSoil?.perlite           ?? 0f,
            soilDegradation       = potSoil?.soilDegradation   ?? 0f,
            soilSaturation        = potSoil?.saturationLevel   ?? 0f,
            soilSeasonsSinceRepot = potSoil?.seasonsSinceRepot ?? 0,
            soilPreset            = (int)(potSoil?.preset   ?? PotSoil.SoilPreset.ClassicBonsai),
            potSize               = (int)(potSoil?.potSize  ?? PotSoil.PotSize.M),

            startYear       = skeleton.SaveStartYear,
            startMonth      = skeleton.SaveStartMonth,
            lastGrownYear   = skeleton.SaveLastGrownYear,
            isIshitsukiMode = skeleton.isIshitsukiMode,

            planNX = skeleton.plantingNormal.x,
            planNY = skeleton.plantingNormal.y,
            planNZ = skeleton.plantingNormal.z,
            planPX = skeleton.plantingSurfacePoint.x,
            planPY = skeleton.plantingSurfacePoint.y,
            planPZ = skeleton.plantingSurfacePoint.z,
        };

        foreach (var node in skeleton.allNodes)
            data.nodes.Add(SerializeNode(node));

        return data;
    }

    /// <summary>Serializes a single TreeNode to a SaveNode. Used by BuildSaveData and SeverAirLayer.</summary>
    public static SaveNode SerializeNode(TreeNode node)
    {
        return new SaveNode
        {
            id       = node.id,
            depth    = node.depth,
            parentId = node.parent != null ? node.parent.id : -1,

            px = node.worldPosition.x, py = node.worldPosition.y, pz = node.worldPosition.z,
            dx = node.growDirection.x, dy = node.growDirection.y, dz = node.growDirection.z,

            radius       = node.radius,
            minRadius    = node.minRadius,
            length       = node.length,
            targetLength = node.targetLength,
            isGrowing    = node.isGrowing,
            age          = node.age,

            isTrimmed         = node.isTrimmed,
            hasLeaves         = node.hasLeaves,
            isRoot            = node.isRoot,
            subdivisionsLeft  = node.subdivisionsLeft,
            birthYear         = node.birthYear,
            refinementLevel   = node.refinementLevel,
            branchVigor       = node.branchVigor,

            hasBud              = node.hasBud,
            backBudStimulated   = node.backBudStimulated,
            isTrimCutPoint      = node.isTrimCutPoint,
            trimCutDepth        = node.trimCutDepth,
            regrowthSeasonCount = node.regrowthSeasonCount,

            health      = node.health,
            branchLoad  = node.branchLoad,
            sagAngleDeg = node.sagAngleDeg,

            isDead        = node.isDead,
            isDeadwood    = node.isDeadwood,
            shadedSeasons = node.shadedSeasons,
            deadSeasons   = node.deadSeasons,

            fungalLoad          = node.fungalLoad,
            isMycorrhizal       = node.isMycorrhizal,
            healthySeasonsCount = node.healthySeasonsCount,

            hasWire            = node.hasWire,
            woX = node.wireOriginalDirection.x, woY = node.wireOriginalDirection.y, woZ = node.wireOriginalDirection.z,
            wtX = node.wireTargetDirection.x,   wtY = node.wireTargetDirection.y,   wtZ = node.wireTargetDirection.z,
            wireSetProgress    = node.wireSetProgress,
            wireDamageProgress = node.wireDamageProgress,
            wireAgeDays        = node.wireAgeDays,
            isTrainingWire     = node.isTrainingWire,

            boundaryPressure = node.boundaryPressure,
            isAirLayerRoot   = node.isAirLayerRoot,

            hasWound     = node.hasWound,
            woundRadius  = node.woundRadius,
            wnX = node.woundFaceNormal.x, wnY = node.woundFaceNormal.y, wnZ = node.woundFaceNormal.z,
            woundAge     = node.woundAge,
            pasteApplied = node.pasteApplied,
        };
    }
}
