using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns and tracks weeds on the bonsai pot soil surface.
/// Attach to the same GameObject as TreeSkeleton so weed GameObjects
/// become tree children — CameraOrbit's existing tree-child check then
/// blocks camera drags when the player clicks a weed.
///
/// Each spring TreeSkeleton calls ApplySeasonAndSpawn() to drain nutrients/
/// moisture and possibly add a new weed.
/// </summary>
public class WeedManager : MonoBehaviour
{
    public static WeedManager Instance { get; private set; }

    [Header("Weed Spawning")]
    [Tooltip("Probability of spawning one new weed each spring.")]
    [SerializeField] [Range(0f, 1f)] float spawnChancePerSeason = 0.5f;

    [Tooltip("Hard cap — no new weeds spawn when this many are already present.")]
    [SerializeField] int maxWeeds = 6;

    [Header("Seasonal Impact — per weed per season")]
    [SerializeField] float grassNutrientDrain   = 0.04f;
    [SerializeField] float grassMoistureDrain   = 0.03f;
    [SerializeField] float cloverNutrientDrain  = 0.03f;
    [SerializeField] float cloverMoistureDrain  = 0.02f;
    [SerializeField] float dandelionNutrientDrain = 0.10f;
    [SerializeField] float dandelionMoistureDrain = 0.06f;
    [SerializeField] float thistleNutrientDrain = 0.14f;
    [SerializeField] float thistleMoistureDrain = 0.08f;

    [Tooltip("Extra soil-moisture drained the season after herbicide — penalises beneficial soil organisms.")]
    [SerializeField] float herbicideAerationDrain = 0.15f;

    [Header("Spawn Weights (should sum to ~1)")]
    [SerializeField] [Range(0f, 1f)] float grassWeight     = 0.40f;
    [SerializeField] [Range(0f, 1f)] float cloverWeight    = 0.35f;
    [SerializeField] [Range(0f, 1f)] float dandelionWeight = 0.15f;
    // Thistle fills the remainder.

    [Header("Spawn Exclusion")]
    [Tooltip("Weeds won't spawn within this many trunk-radii of the trunk centre.")]
    [SerializeField] float trunkExclusionMultiplier = 4f;

    [Tooltip("Overlap radius used when checking if a candidate spawn point sits on a rock.")]
    [SerializeField] float rockCheckRadius = 0.15f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    readonly List<Weed> activeWeeds = new List<Weed>();
    bool    herbicideAerationActive;
    Vector3 soilCenter;
    float   potRadius;
    float   trunkRadius;

    // ── Events ────────────────────────────────────────────────────────────────

    public static event System.Action OnFirstWeedSpawned;

    // ── Properties ────────────────────────────────────────────────────────────

    public int  ActiveWeedCount         => activeWeeds.Count;
    public bool HerbicideAerationActive => herbicideAerationActive;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        // Ensure WeedPuller is present — auto-add so only WeedManager needs to be manually added.
        if (GetComponent<WeedPuller>() == null)
            gameObject.AddComponent<WeedPuller>();
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>Called by TreeSkeleton each spring before ApplySeasonAndSpawn.</summary>
    public void SetPotBounds(Vector3 center, float radius)
    {
        soilCenter = center;
        potRadius  = radius;
    }

    /// <summary>Updated each spring so weeds stay clear of the thickening trunk.</summary>
    public void SetTrunkRadius(float r) => trunkRadius = r;

    // ── Seasonal tick ─────────────────────────────────────────────────────────

    public void ApplySeasonAndSpawn(TreeSkeleton skeleton)
    {
        // Seasonal drain from existing weeds
        int drainCount = 0;
        foreach (var w in activeWeeds)
        {
            if (w == null) continue;
            float mult = w.isRipped ? 0.6f : 1f;
            float nd = 0f, md = 0f;
            switch (w.weedType)
            {
                case WeedType.Grass:     nd = grassNutrientDrain;     md = grassMoistureDrain;     break;
                case WeedType.Clover:    nd = cloverNutrientDrain;    md = cloverMoistureDrain;    break;
                case WeedType.Dandelion: nd = dandelionNutrientDrain; md = dandelionMoistureDrain; break;
                case WeedType.Thistle:   nd = thistleNutrientDrain;   md = thistleMoistureDrain;   break;
            }
            skeleton.nutrientReserve = Mathf.Max(0f, skeleton.nutrientReserve - nd * mult);
            skeleton.soilMoisture    = Mathf.Max(0f, skeleton.soilMoisture    - md * mult);
            drainCount++;
        }
        if (drainCount > 0)
            Debug.Log($"[Weeds] {drainCount} weed(s) drained nutrients — " +
                      $"reserve={skeleton.nutrientReserve:F2} moisture={skeleton.soilMoisture:F2}");

        if (herbicideAerationActive)
        {
            skeleton.soilMoisture   = Mathf.Max(0f, skeleton.soilMoisture - herbicideAerationDrain);
            herbicideAerationActive = false;
            Debug.Log("[Weeds] Herbicide aeration penalty applied.");
        }

        if (activeWeeds.Count < maxWeeds && Random.value < spawnChancePerSeason)
            SpawnWeed();
    }

    // ── Removal ───────────────────────────────────────────────────────────────

    public void RemoveWeed(Weed weed)
    {
        if (weed == null) return;
        activeWeeds.Remove(weed);
        Debug.Log($"[Weeds] Clean pull — {weed.name} removed. Remaining={activeWeeds.Count}");
        Destroy(weed.gameObject);
    }

    public void RipWeed(Weed weed)
    {
        if (weed == null) return;
        weed.isRipped      = true;
        weed.forceRequired *= 1.8f;
        weed.ripChance     *= 0.5f;

        var s = weed.transform.localScale;
        weed.transform.localScale = new Vector3(s.x, s.y * 0.3f, s.z);
        weed.transform.position   = weed.restPosition;
        weed.restPosition         = weed.transform.position;

        var rend = weed.GetComponentInChildren<Renderer>();
        if (rend != null) rend.material.color = new Color(0.38f, 0.20f, 0.05f);

        Debug.Log($"[Weeds] RIPPED — {weed.name} stump left. " +
                  $"force={weed.forceRequired:F3} rip={weed.ripChance:F2}");
    }

    /// <summary>
    /// Gently removes all weeds — called when entering repot mode.
    /// No herbicide penalty, no mycorrhizal damage; the player physically
    /// clears the pot surface before working the roots.
    /// </summary>
    public void PullAllWeeds()
    {
        int count = activeWeeds.Count;
        foreach (var w in activeWeeds)
            if (w != null) Destroy(w.gameObject);
        activeWeeds.Clear();
        if (count > 0)
            Debug.Log($"[Weeds] Cleared {count} weed(s) for repot.");
    }

    public void HerbicideAll()
    {
        int count = activeWeeds.Count;
        foreach (var w in activeWeeds)
            if (w != null) Destroy(w.gameObject);
        activeWeeds.Clear();
        herbicideAerationActive = true;

        // Broad-spectrum herbicide kills beneficial mycorrhizal fungi in the root zone
        GetComponent<TreeSkeleton>()?.DamageMycorrhizae();

        Debug.Log($"[Weeds] Herbicide cleared {count} weed(s). Aeration penalty queued.");
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    public List<SaveWeed> GetSaveState()
    {
        var list = new List<SaveWeed>(activeWeeds.Count);
        foreach (var w in activeWeeds)
        {
            if (w == null) continue;
            list.Add(new SaveWeed
            {
                px = w.restPosition.x, py = w.restPosition.y, pz = w.restPosition.z,
                weedType      = (int)w.weedType,
                isRipped      = w.isRipped,
                forceRequired = w.forceRequired,
                ripChance     = w.ripChance,
            });
        }
        return list;
    }

    public void LoadSaveState(List<SaveWeed> saved)
    {
        foreach (var w in activeWeeds)
            if (w != null) Destroy(w.gameObject);
        activeWeeds.Clear();
        if (saved == null) return;
        foreach (var sw in saved)
            SpawnWeedAt(new Vector3(sw.px, sw.py, sw.pz),
                        (WeedType)sw.weedType, sw.isRipped, sw.forceRequired, sw.ripChance);
    }

    // ── Internal spawning ─────────────────────────────────────────────────────

    void SpawnWeed()
    {
        // Try up to 8 candidate positions; discard ones that hit exclusion zones
        float minDist = trunkRadius * trunkExclusionMultiplier;
        Vector3 pos = Vector3.zero;
        bool found  = false;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 rnd     = Random.insideUnitCircle * potRadius * 0.85f;
            Vector3 candidate = soilCenter + new Vector3(rnd.x, 0f, rnd.y);

            // Reject if too close to trunk
            float dist2D = new Vector2(rnd.x, rnd.y).magnitude;
            if (dist2D < minDist) continue;

            // Reject if sitting on a rock collider
            if (IsOnRock(candidate)) continue;

            pos   = candidate;
            found = true;
            break;
        }

        if (!found) return;   // couldn't find a clear spot this season

        float r      = Random.value;
        WeedType type;
        if      (r < grassWeight)                                   type = WeedType.Grass;
        else if (r < grassWeight + cloverWeight)                    type = WeedType.Clover;
        else if (r < grassWeight + cloverWeight + dandelionWeight)  type = WeedType.Dandelion;
        else                                                        type = WeedType.Thistle;

        SpawnWeedAt(pos, type, false, -1f, -1f);
    }

    bool IsOnRock(Vector3 pos)
    {
        // Cast a small sphere downward from just above the surface
        Vector3 origin = pos + Vector3.up * 0.3f;
        if (Physics.SphereCast(origin, rockCheckRadius, Vector3.down, out RaycastHit hit, 0.6f))
        {
            if (hit.collider.CompareTag("Rock")) return true;
        }
        // Also check for overlap at the surface level
        var cols = Physics.OverlapSphere(pos, rockCheckRadius);
        foreach (var c in cols)
            if (c.CompareTag("Rock")) return true;
        return false;
    }

    void SpawnWeedAt(Vector3 pos, WeedType type, bool isRipped, float forceOverride, float ripChanceOverride)
    {
        var go  = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Weed_" + type;
        // Parent to the tree GO so CameraOrbit blocks drags on it automatically
        go.transform.SetParent(transform, worldPositionStays: true);

        float width, height, force, rip;
        Color col;

        switch (type)
        {
            case WeedType.Grass:
                width = 0.13f; height = 0.20f;
                col   = new Color(0.20f, 0.58f, 0.14f);
                force = 0.04f; rip   = 0.25f;
                break;
            case WeedType.Clover:
                width = 0.16f; height = 0.10f;
                col   = new Color(0.16f, 0.52f, 0.20f);
                force = 0.035f; rip  = 0.15f;
                break;
            case WeedType.Dandelion:
                width = 0.07f; height = 0.32f;
                col   = new Color(0.22f, 0.60f, 0.10f);
                force = 0.08f; rip   = 0.50f;
                break;
            default: // Thistle
                width = 0.09f; height = 0.42f;
                col   = new Color(0.18f, 0.40f, 0.22f);
                force = 0.11f; rip   = 0.60f;
                break;
        }

        // Place base flush with soil surface, pivot at mid-height
        go.transform.position   = pos + Vector3.up * height * 0.5f;
        go.transform.localScale = new Vector3(width, height, width);

        var mat = new Material(Shader.Find("Unlit/Color")) { color = col };
        go.GetComponent<Renderer>().material = mat;

        var weed          = go.AddComponent<Weed>();
        weed.weedType     = type;
        weed.restPosition = go.transform.position;
        weed.forceRequired = forceOverride > 0f ? forceOverride : force;
        weed.ripChance     = forceOverride > 0f ? ripChanceOverride : rip;

        if (isRipped)
        {
            weed.isRipped = true;
            go.transform.localScale = new Vector3(width, height * 0.3f, width);
            mat.color = new Color(0.38f, 0.20f, 0.05f);
            weed.restPosition = go.transform.position;
        }

        activeWeeds.Add(weed);
        OnFirstWeedSpawned?.Invoke();
        Debug.Log($"[Weeds] Spawned {go.name} at {pos} | " +
                  $"force={weed.forceRequired:F3} rip={weed.ripChance:F2}");
    }
}
