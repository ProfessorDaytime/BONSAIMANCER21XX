using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ── Data classes ──────────────────────────────────────────────────────────────

[System.Serializable]
public class SaveNode
{
    // Identity
    public int   id;
    public int   depth;
    public int   parentId;   // -1 for the trunk root

    // Geometry
    public float px, py, pz;               // worldPosition
    public float dx, dy, dz;               // growDirection
    public float radius;
    public float minRadius;
    public float length;
    public float targetLength;

    // Growth
    public bool  isGrowing;
    public float age;

    // State flags
    public bool isTrimmed;
    public bool hasLeaves;
    public bool isRoot;
    public int  subdivisionsLeft;
    public int  birthYear;
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

    // Wire
    public bool  hasWire;
    public float woX, woY, woZ;            // wireOriginalDirection
    public float wtX, wtY, wtZ;            // wireTargetDirection
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
    public float wnX, wnY, wnZ;           // woundFaceNormal
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
    public int   startYear;
    public int   startMonth;
    public int   lastGrownYear;
    public bool  isIshitsukiMode;

    // Planting surface
    public float planNX, planNY, planNZ;
    public float planPX, planPY, planPZ;
}

// ── SaveManager ───────────────────────────────────────────────────────────────

public static class SaveManager
{
    static string SavePath => Path.Combine(Application.persistentDataPath, "bonsai_save.json");

    // ── Save ─────────────────────────────────────────────────────────────────

    public static void Save(TreeSkeleton skeleton, LeafManager leafManager = null)
    {
        if (skeleton == null || skeleton.root == null)
        {
            Debug.LogWarning("[Save] No tree to save.");
            return;
        }

        var data = new SaveData
        {
            // GameManager time
            year     = GameManager.year,
            month    = GameManager.month,
            day      = GameManager.day,
            hour     = GameManager.hour,
            waterings = GameManager.waterings,

            // Skeleton live state
            treeEnergy              = skeleton.treeEnergy,
            soilMoisture            = skeleton.soilMoisture,
            droughtDaysAccumulated  = skeleton.droughtDaysAccumulated,
            startYear               = skeleton.SaveStartYear,
            startMonth              = skeleton.SaveStartMonth,
            lastGrownYear           = skeleton.SaveLastGrownYear,
            isIshitsukiMode         = skeleton.isIshitsukiMode,

            planNX = skeleton.plantingNormal.x,
            planNY = skeleton.plantingNormal.y,
            planNZ = skeleton.plantingNormal.z,
            planPX = skeleton.plantingSurfacePoint.x,
            planPY = skeleton.plantingSurfacePoint.y,
            planPZ = skeleton.plantingSurfacePoint.z,
        };

        foreach (var node in skeleton.allNodes)
        {
            var sn = new SaveNode
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

                isTrimmed        = node.isTrimmed,
                hasLeaves        = node.hasLeaves,
                isRoot           = node.isRoot,
                subdivisionsLeft = node.subdivisionsLeft,
                birthYear        = node.birthYear,
                refinementLevel  = node.refinementLevel,
                branchVigor      = node.branchVigor,

                hasBud             = node.hasBud,
                backBudStimulated  = node.backBudStimulated,
                isTrimCutPoint     = node.isTrimCutPoint,
                trimCutDepth       = node.trimCutDepth,
                regrowthSeasonCount= node.regrowthSeasonCount,

                health = node.health,

                hasWire          = node.hasWire,
                woX = node.wireOriginalDirection.x, woY = node.wireOriginalDirection.y, woZ = node.wireOriginalDirection.z,
                wtX = node.wireTargetDirection.x,   wtY = node.wireTargetDirection.y,   wtZ = node.wireTargetDirection.z,
                wireSetProgress    = node.wireSetProgress,
                wireDamageProgress = node.wireDamageProgress,
                wireAgeDays        = node.wireAgeDays,
                isTrainingWire     = node.isTrainingWire,

                boundaryPressure = node.boundaryPressure,
                isAirLayerRoot   = node.isAirLayerRoot,

                hasWound  = node.hasWound,
                woundRadius = node.woundRadius,
                wnX = node.woundFaceNormal.x, wnY = node.woundFaceNormal.y, wnZ = node.woundFaceNormal.z,
                woundAge    = node.woundAge,
                pasteApplied= node.pasteApplied,
            };
            data.nodes.Add(sn);
        }

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"[Save] Saved {data.nodes.Count} nodes → {SavePath}");
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public static bool Load(TreeSkeleton skeleton, LeafManager leafManager = null)
    {
        if (!File.Exists(SavePath))
        {
            Debug.Log("[Save] No save file found.");
            return false;
        }

        string   json = File.ReadAllText(SavePath);
        SaveData data = JsonUtility.FromJson<SaveData>(json);

        if (data == null || data.nodes == null || data.nodes.Count == 0)
        {
            Debug.LogWarning("[Save] Save file is empty or corrupt.");
            return false;
        }

        // ── Restore GameManager time ──────────────────────────────────────
        GameManager.year     = data.year;
        GameManager.month    = data.month;
        GameManager.day      = data.day;
        GameManager.hour     = data.hour;
        GameManager.waterings= data.waterings;

        // ── Rebuild the tree on TreeSkeleton ─────────────────────────────
        skeleton.LoadFromSaveData(data, leafManager);

        Debug.Log($"[Save] Loaded {data.nodes.Count} nodes | year={data.year} month={data.month}");
        return true;
    }

    public static bool SaveExists() => File.Exists(SavePath);
}
