using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// ── Serializable rows (JsonUtility writes one TrainSample per JSONL line) ──────
[Serializable] public class TrainContext
{
    public int    year, month, day, nodeCount, matchPct;
    public string species, style;
    public float  ageYears, treeHeight, avgHealth, nutrients, moisture;
}

[Serializable] public class TrainAction
{
    public string type;        // Trim | Pinch | Wire | Unwire | Paste | Defoliate | Repot | …
    public int    nodeId;
    public string parameters;  // small JSON blob, action-specific (may be empty)
}

[Serializable] public class TrainNode
{
    public int   id, depth, parentId;
    public float heightNorm, azimuthDeg, radius, length, vigor, health, refinementLevel;
    public bool  isTerminal, hasWire;
}

[Serializable] public class TrainSample
{
    public string       ts;       // wall-clock timestamp
    public string       source;   // "player" or "auto" (AutoStyler / Quick-Start)
    public TrainContext context;
    public TrainAction  action;
    public TrainNode[]  nodes;     // compact tree snapshot at action time
}

/// <summary>
/// Append-only recorder of player styling/care actions + a tree snapshot, for building an ML dataset
/// to train the auto-stylist on real technique. One JSONL line per action in
/// <c>persistentDataPath/training/&lt;session&gt;.jsonl</c>. Actions from automated systems are kept but
/// labelled <c>source="auto"</c> (via <see cref="ProgressionManager.AutomationActive"/>) so the dataset
/// can filter to real player input. Toggle <c>Recording</c> from the Settings → Debug tab (off by default).
///
/// Add a `TrainingRecorder` component to the scene to enable. See PLAN backlog "Training Data Recorder".
/// </summary>
public class TrainingRecorder : MonoBehaviour
{
    public static TrainingRecorder Instance { get; private set; }

    [Tooltip("Record player actions to a JSONL session file. Off by default; toggle from Settings → Debug.")]
    [SerializeField] bool recording = false;
    [Tooltip("Cap on nodes written per snapshot (keeps lines bounded on big trees).")]
    [SerializeField] int maxNodesPerSample = 600;

    public bool Recording { get => recording; set => recording = value; }
    public int  SampleCount { get; private set; }

    TreeSkeleton skeleton;
    string sessionPath;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        string dir = Path.Combine(Application.persistentDataPath, "training");
        try { Directory.CreateDirectory(dir); } catch { /* best effort */ }
        string id = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + UnityEngine.Random.Range(1000, 9999);
        sessionPath = Path.Combine(dir, id + ".jsonl");
    }

    /// <summary>Records one action with the current tree snapshot. No-op unless Recording is on.</summary>
    public void RecordAction(string type, int nodeId, string paramsJson = "")
    {
        if (!recording) return;
        if (skeleton == null) skeleton = FindFirstObjectByType<TreeSkeleton>();
        if (skeleton == null || skeleton.allNodes == null || skeleton.root == null) return;

        var sample = BuildSample(type, nodeId, paramsJson);
        if (sample == null) return;
        try
        {
            File.AppendAllText(sessionPath, JsonUtility.ToJson(sample) + "\n");
            SampleCount++;
        }
        catch (Exception e) { Debug.LogWarning($"[Training] write failed: {e.Message}"); }
    }

    TrainSample BuildSample(string type, int nodeId, string paramsJson)
    {
        var all = skeleton.allNodes;

        float baseY = skeleton.root.worldPosition.y;
        float topY  = baseY;
        float sumH  = 0f; int live = 0;
        foreach (var n in all)
        {
            if (n == null || n.isRoot || n.isDead) continue;
            float y = n.tipPosition.y;
            if (y > topY) topY = y;
            sumH += Mathf.Clamp01(n.health); live++;
        }
        float height = Mathf.Max(0.01f, topY - baseY);

        var sty   = AutoStyler.Instance;
        var soil  = skeleton.GetComponent<PotSoil>();

        var ctx = new TrainContext
        {
            year       = GameManager.year,
            month      = GameManager.month,
            day        = GameManager.day,
            species    = skeleton.SpeciesName,
            style      = sty != null && sty.style != null ? sty.style.name : "",
            ageYears   = GameManager.year - skeleton.root.birthYear,
            treeHeight = height,
            nodeCount  = all.Count,
            avgHealth  = live > 0 ? sumH / live : 0f,
            nutrients  = skeleton.nutrientReserve,
            moisture   = soil != null ? soil.saturationLevel : -1f,
            matchPct   = sty != null ? sty.MatchPercent : -1,
        };

        var nodes = new List<TrainNode>(Mathf.Min(all.Count, maxNodesPerSample));
        foreach (var n in all)
        {
            if (n == null || n.isRoot || n.isDead) continue;
            if (nodes.Count >= maxNodesPerSample) break;

            Vector3 gd = n.growDirection;
            nodes.Add(new TrainNode
            {
                id              = n.id,
                depth           = n.depth,
                parentId        = n.parent != null ? n.parent.id : -1,
                heightNorm      = Mathf.Clamp01((n.tipPosition.y - baseY) / height),
                azimuthDeg      = Mathf.Atan2(gd.z, gd.x) * Mathf.Rad2Deg,
                radius          = n.radius,
                length          = n.length,
                vigor           = n.branchVigor,
                health          = n.health,
                refinementLevel = n.refinementLevel,
                isTerminal      = n.isTerminal,
                hasWire         = n.hasWire,
            });
        }

        return new TrainSample
        {
            ts      = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            source  = ProgressionManager.AutomationActive ? "auto" : "player",
            context = ctx,
            action  = new TrainAction { type = type, nodeId = nodeId, parameters = paramsJson ?? "" },
            nodes   = nodes.ToArray(),
        };
    }
}
