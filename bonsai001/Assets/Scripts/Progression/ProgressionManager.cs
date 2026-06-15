using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which mode the current game is played in. Sandbox unlocks every tool; Career gates
/// tools behind the unlock profile (gating itself lands in Slice 3).</summary>
public enum GameMode { Career, Sandbox }

/// <summary>
/// One earnable milestone — a first bloom, a survival anniversary, a species grown.
/// Reaching one pops a gentle toast, files a Journal entry, and (usually) awards Aesthetic Points.
/// Named milestones live in a small registry; dynamic ones (e.g. per-species) are built inline.
/// </summary>
public class ProgressionMilestone
{
    public readonly string id, title, journalId, journalText;
    public readonly int    reward;

    public ProgressionMilestone(string id, string title, int reward, string journalText)
    {
        this.id          = id;
        this.title       = title;
        this.reward      = reward;
        this.journalText = journalText;
        this.journalId   = "journal_" + id;
    }

    static Dictionary<string, ProgressionMilestone> registry;

    public static ProgressionMilestone Get(string id)
    {
        Ensure();
        return registry.TryGetValue(id, out var m) ? m : null;
    }

    static void Ensure()
    {
        if (registry != null) return;
        registry = new Dictionary<string, ProgressionMilestone>();
        void R(ProgressionMilestone m) => registry[m.id] = m;

        R(new ProgressionMilestone("first_trim",  "First Cut",        20, "You made your first pruning cut. Bonsai is shaped by what you remove."));
        R(new ProgressionMilestone("first_wire",  "First Wire",       20, "Your first wire is set. Wood remembers the shapes you teach it."));
        R(new ProgressionMilestone("first_repot", "First Repot",      30, "You repotted for the first time — fresh soil, refreshed roots."));
        R(new ProgressionMilestone("first_bloom", "First Bloom",      40, "Your tree flowered for the first time. A quiet reward for patience."));
        R(new ProgressionMilestone("first_fruit", "First Fruit",      40, "The first fruit has set and ripened."));
        R(new ProgressionMilestone("survive_5",   "Five Years",       60, "Five years tended. The trunk thickens; the design settles."));
        R(new ProgressionMilestone("survive_10",  "A Decade",        120, "Ten years. A bonsai is measured in decades, not seasons."));
        R(new ProgressionMilestone("survive_25",  "Quarter Century", 300, "Twenty-five years of care. This is a tree with a story."));
    }
}

/// <summary>
/// Scene singleton owning the global <see cref="ProgressionProfile"/> and the soft economy.
/// Awards **Aesthetic Points** (spendable on cosmetics only) for stewardship + milestones, and
/// raises events the HUD/Journal listen to. Add one to the scene (like FlowerManager / QuickStartManager).
///
/// Slice 1: currency + milestones + seasonal stewardship + HUD events. Tool-gating (Career) and
/// the cosmetic shop come in later slices; the APIs for them are stubbed here. See
/// `Docs/PROGRESSION_DESIGN.md`.
/// </summary>
public class ProgressionManager : MonoBehaviour
{
    public static ProgressionManager Instance { get; private set; }
    public static ProgressionProfile Profile  { get; private set; }

    /// <summary>True while an automated system (AutoStyler, Quick-Start) is performing a tree action.
    /// The player-technique milestone hooks (first_trim / first_repot) skip while this is set, so the
    /// auto-styler never earns the player's achievements. Player input paths leave it false.</summary>
    public static bool AutomationActive;

    /// <summary>(balance, delta, reason) — delta 0 means "initial sync", no toast.</summary>
    public static event Action<int, int, string>      OnCurrencyChanged;
    public static event Action<ProgressionMilestone>   OnMilestone;
    /// <summary>Fires once when a Career tool first unlocks (toolId). Sandbox never fires this.</summary>
    public static event Action<string>                 OnToolUnlocked;

    [Header("Economy tuning")]
    [Tooltip("Aesthetic Points awarded at the end of each growing season (LeafFall), scaled by " +
             "average tree health. A healthy tree pays out the full amount; a struggling one less.")]
    [SerializeField] int seasonStewardshipReward = 25;

    [Tooltip("Mode used until the New Game mode-select (Slice 3) sets one. Sandbox unlocks all tools.")]
    [SerializeField] GameMode defaultMode = GameMode.Sandbox;

    public GameMode CurrentMode { get; private set; }
    public int      Balance => Profile != null ? Profile.aestheticPoints : 0;

    TreeSkeleton skeleton;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance    = this;
        Profile     = ProgressionProfile.Load();
        CurrentMode = ParseMode(Profile.gameMode, defaultMode);
    }

    void OnEnable()  { GameManager.OnGameStateChanged += OnState; }
    void OnDisable() { GameManager.OnGameStateChanged -= OnState; }

    void Start()
    {
        // Sync any HUD that came up after Awake, then record the species-grown milestone.
        OnCurrencyChanged?.Invoke(Balance, 0, "init");
        TryGrowSpeciesMilestone();
    }

    // ── Currency ──────────────────────────────────────────────────────────────
    public void Award(int amount, string reason)
    {
        if (amount <= 0 || Profile == null) return;
        Profile.aestheticPoints += amount;
        Profile.Save();
        OnCurrencyChanged?.Invoke(Profile.aestheticPoints, amount, reason);
    }

    public bool CanAfford(int amount) => Profile != null && Profile.aestheticPoints >= amount;

    /// <summary>Spends points if affordable. Returns false (no change) if not.</summary>
    public bool TrySpend(int amount, string reason)
    {
        if (Profile == null || amount < 0 || Profile.aestheticPoints < amount) return false;
        Profile.aestheticPoints -= amount;
        Profile.Save();
        OnCurrencyChanged?.Invoke(Profile.aestheticPoints, -amount, reason);
        return true;
    }

    // ── Milestones ────────────────────────────────────────────────────────────
    /// <summary>Reaches a registered milestone by id (no-op if already reached or unknown).</summary>
    public void ReachMilestone(string id) => ReachMilestoneInternal(id, ProgressionMilestone.Get(id));

    /// <summary>Reaches a dynamic milestone (e.g. per-species), creating its def inline if unregistered.</summary>
    public void ReachMilestone(string id, string title, int reward, string journalText)
        => ReachMilestoneInternal(id, ProgressionMilestone.Get(id)
                                      ?? new ProgressionMilestone(id, title, reward, journalText));

    void ReachMilestoneInternal(string id, ProgressionMilestone def)
    {
        if (Profile == null || !Profile.Add(Profile.milestones, id)) return;   // already reached
        if (def != null) Profile.Add(Profile.journalEntries, def.journalId);
        Profile.Save();
        if (def != null && def.reward > 0) Award(def.reward, $"Milestone · {def.title}");
        if (def != null) OnMilestone?.Invoke(def);
    }

    // ── Tool gating (Career — consumed in Slice 3) ────────────────────────────
    public bool IsToolUnlocked(string toolId)
    {
        if (CurrentMode == GameMode.Sandbox) return true;
        return Profile != null && Profile.unlockedTools.Contains(toolId);
    }

    public void UnlockTool(string toolId)
    {
        if (Profile != null && Profile.Add(Profile.unlockedTools, toolId))
        {
            Profile.Save();
            OnToolUnlocked?.Invoke(toolId);
        }
    }

    public void SetMode(GameMode mode)
    {
        CurrentMode = mode;
        if (Profile != null) { Profile.gameMode = mode.ToString(); Profile.Save(); }
        if (mode == GameMode.Career) EvaluateUnlocks();   // catch up any already-qualified tools
    }

    /// <summary>Career-only: unlocks tools whose gentle, teaching-oriented triggers are now met.
    /// Triggers read only from the tree itself (structure depth, age in years, pot-bound pressure)
    /// so they are robust and persisted — see Docs/PROGRESSION_DESIGN.md §3.</summary>
    void EvaluateUnlocks()
    {
        if (CurrentMode != GameMode.Career) return;
        if (skeleton == null) skeleton = FindFirstObjectByType<TreeSkeleton>();
        if (skeleton == null || skeleton.root == null || skeleton.allNodes == null) return;

        int   age      = Mathf.Max(0, GameManager.year - skeleton.root.birthYear);
        int   maxDepth = 0;
        foreach (var n in skeleton.allNodes)
            if (n != null && !n.isRoot && n.depth > maxDepth) maxDepth = n.depth;
        float rootPressure = skeleton.RootPressureFactor();

        if (maxDepth >= 3)                     UnlockTool("trim");      // real structure to cut
        if (maxDepth >= 4)                     UnlockTool("wire");      // branches worth shaping
        if (age >= 2)                          UnlockTool("pinch");     // ramification age
        if (age >= 3)                          UnlockTool("defoliate");
        if (rootPressure >= 0.45f || age >= 3) UnlockTool("soil");      // pot-bound → repot/feed
        if (age >= 5)                          UnlockTool("root");      // root work after a repot
        if (age >= 8)                          UnlockTool("advanced");  // air-layer / Ishitsuki
    }

    // ── Owned cosmetics (shop — consumed in Slice 2) ──────────────────────────
    public bool Owns(string itemId) => Profile != null && Profile.ownedItemIds.Contains(itemId);

    public void GrantItem(string itemId)
    {
        if (Profile != null && Profile.Add(Profile.ownedItemIds, itemId)) Profile.Save();
    }

    // ── Seasonal stewardship ──────────────────────────────────────────────────
    void OnState(GameState state)
    {
        // LeafFall marks the end of the growing season — pay out stewardship for the year.
        if (state == GameState.LeafFall) AwardStewardship();

        // Survival milestones fire in both modes; Career also re-checks tool unlocks.
        EvaluateMilestones();
        if (CurrentMode == GameMode.Career) EvaluateUnlocks();
    }

    /// <summary>Age-based survival milestones (both modes). Age from the root node's birth year.</summary>
    void EvaluateMilestones()
    {
        if (skeleton == null) skeleton = FindFirstObjectByType<TreeSkeleton>();
        if (skeleton == null || skeleton.root == null) return;

        int age = GameManager.year - skeleton.root.birthYear;
        if (age >= 5)  ReachMilestone("survive_5");
        if (age >= 10) ReachMilestone("survive_10");
        if (age >= 25) ReachMilestone("survive_25");
    }

    void AwardStewardship()
    {
        float health = AverageTreeHealth();
        if (health <= 0f) return;
        int amt = Mathf.RoundToInt(seasonStewardshipReward * health);
        if (amt > 0) Award(amt, "Seasonal stewardship");
    }

    float AverageTreeHealth()
    {
        if (skeleton == null) skeleton = FindFirstObjectByType<TreeSkeleton>();
        if (skeleton == null || skeleton.allNodes == null) return 0f;

        float sum = 0f; int n = 0;
        foreach (var nd in skeleton.allNodes)
        {
            if (nd == null || nd.isRoot || nd.isDead) continue;
            sum += Mathf.Clamp01(nd.health);
            n++;
        }
        return n > 0 ? sum / n : 0f;
    }

    void TryGrowSpeciesMilestone()
    {
        if (skeleton == null) skeleton = FindFirstObjectByType<TreeSkeleton>();
        if (skeleton == null || skeleton.species == null) return;

        string sp = skeleton.species.speciesName;
        string id = "species_" + sp.Replace(" ", "_").ToLowerInvariant();
        ReachMilestone(id, $"Grew {sp}", 25, $"You have grown a {sp}. Each species teaches its own habits.");
    }

    static GameMode ParseMode(string s, GameMode fallback)
        => Enum.TryParse<GameMode>(s, out var m) ? m : fallback;
}
