using UnityEngine;

/// <summary>
/// Places the equipped Decoration cosmetic — a figurine/accent beside the tree. A Decoration
/// <see cref="ItemDefinition"/> supplies either a `prefab` (author-supplied model) or a built-in
/// `proceduralId` (e.g. "lantern") so the system shows something with zero art. One decoration is
/// shown at a time (equip swaps it; "None" clears it). Driven by <see cref="CustomizeManager"/>.
///
/// Set <c>anchor</c> to an empty Transform where decorations should sit (on the table beside the
/// pot); otherwise it falls back to an offset from the planter/tree. See Docs/PROGRESSION_DESIGN.md §7.
/// </summary>
public class DecorationManager : MonoBehaviour
{
    public static DecorationManager Instance { get; private set; }

    [Tooltip("Where decorations are placed (an empty GameObject on the table beside the pot). " +
             "If null, falls back to an offset from the planter/tree.")]
    [SerializeField] Transform anchor;
    [Tooltip("Fallback placement offset from the planter/tree when no anchor is assigned.")]
    [SerializeField] Vector3 fallbackOffset = new Vector3(0.7f, 0f, 0.3f);
    [Tooltip("Uniform scale applied to procedural decorations.")]
    [SerializeField] float proceduralScale = 1f;

    GameObject current;
    Material   stoneMat;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    /// <summary>Equip a Decoration item (null or empty item = clear).</summary>
    public void Apply(ItemDefinition def)
    {
        if (current != null) { Destroy(current); current = null; }
        if (def == null) return;

        GameObject go = null;
        if (def.prefab != null)                       go = Instantiate(def.prefab);
        else if (!string.IsNullOrEmpty(def.proceduralId)) go = BuildProcedural(def.proceduralId);
        if (go == null) return;

        go.name = "Decoration_" + def.name;
        if (anchor != null)
        {
            go.transform.SetParent(anchor, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
        }
        else
        {
            go.transform.position = FallbackPosition();
        }
        current = go;
    }

    Vector3 FallbackPosition()
    {
        var table = GameObject.Find("PlanterTable") ?? GameObject.Find("Platform");
        if (table != null) return table.transform.position + fallbackOffset;
        var tree = FindFirstObjectByType<TreeSkeleton>();
        if (tree != null) return tree.transform.position + fallbackOffset;
        return fallbackOffset;
    }

    // ── Procedural decorations (zero-art placeholders) ─────────────────────────
    GameObject BuildProcedural(string id)
    {
        switch (id)
        {
            case "lantern": return BuildLantern();
            default:        return null;
        }
    }

    Material StoneMat()
    {
        if (stoneMat != null) return stoneMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        stoneMat = new Material(sh);
        var c = new Color(0.55f, 0.55f, 0.52f);
        if (stoneMat.HasProperty("_BaseColor")) stoneMat.SetColor("_BaseColor", c);
        if (stoneMat.HasProperty("_Color"))     stoneMat.SetColor("_Color",     c);
        if (stoneMat.HasProperty("_Smoothness")) stoneMat.SetFloat("_Smoothness", 0.1f);
        return stoneMat;
    }

    // A simple stacked-stone lantern (toro): base, post, fire box, cap, finial.
    GameObject BuildLantern()
    {
        var root = new GameObject("Lantern");
        root.transform.localScale = Vector3.one * proceduralScale;
        var mat = StoneMat();

        void Box(Vector3 pos, Vector3 scale)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.transform.SetParent(root.transform, false);
            b.transform.localPosition = pos;
            b.transform.localScale    = scale;
            var col = b.GetComponent<Collider>(); if (col != null) Destroy(col);
            var r = b.GetComponent<Renderer>(); if (r != null) r.sharedMaterial = mat;
        }

        Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.30f, 0.10f, 0.30f));  // base
        Box(new Vector3(0f, 0.24f, 0f), new Vector3(0.08f, 0.28f, 0.08f));  // post
        Box(new Vector3(0f, 0.44f, 0f), new Vector3(0.22f, 0.16f, 0.22f));  // fire box
        Box(new Vector3(0f, 0.55f, 0f), new Vector3(0.30f, 0.06f, 0.30f));  // cap
        Box(new Vector3(0f, 0.61f, 0f), new Vector3(0.07f, 0.08f, 0.07f));  // finial
        return root;
    }
}
