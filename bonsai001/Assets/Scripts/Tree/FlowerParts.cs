using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Modelled flower and fruit parts (2026-10-30): replacements for the shapes FlowerVisuals and
/// BudVisuals draw in code. Each part (part_cherry_petal, part_fig…) can have any number of
/// variants, listed in <see cref="FlowerPartLibrary"/>; a part with none keeps its code shape.
///
/// <para>A model's origin is where it attaches. Most point along +Z; fruit hangs down -Y; the
/// stamens face +Y and are measured by their radius. The first variant is scaled to the size the
/// code would draw, and later variants by the same factor, so they keep their sizes relative to
/// it. The game colours them: a gradient from base to tip, and an accent colour on any submesh
/// whose material is named for it (a samara's seed, a fruit's stalk).</para>
/// </summary>
public static class FlowerParts
{
    public enum Axis { Forward, Down, Radial }

    sealed class Variant
    {
        public Vector3[] verts, normals;
        public float[]   along;    // 0 at the origin, 1 at the first variant's far end
        public bool[]    accent;
        public int[]     tris;
    }

    static FlowerPartLibrary library;
    static bool loaded;
    static readonly Dictionary<string, (Variant[] variants, float span)> parts =
        new Dictionary<string, (Variant[], float)>();

    // A submesh whose material name contains one of these takes the accent colour.
    static readonly string[] AccentNames = { "stem", "stalk", "seed", "nut", "accent" };

    public static Axis AxisOf(string id) => id switch
    {
        "part_cherry_fruit" or "part_juniper_berry" or "part_wisteria_pod" or "part_catkin"
            or "part_birch_seed_catkin" or "part_willow_fluff_catkin" => Axis.Down,
        "part_cherry_stamens" => Axis.Radial,
        _ => Axis.Forward,
    };

    /// <summary>Forget what's loaded: a new library (setup, or a new play session).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        library = null;
        loaded  = false;
        parts.Clear();
    }

    public static bool Has(string id) => Get(id).variants != null;

    /// <summary>A stable variant pick from three ints (a node id, a year, an index).</summary>
    public static int Key(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
            return (int)(h & 0x7FFFFFFF);
        }
    }

    /// <summary>
    /// Adds a variant of <paramref name="id"/> to the mesh lists at <paramref name="at"/>, turned
    /// by <paramref name="rot"/> (the model's +Z to its forward, +Y to its up) and scaled so the
    /// first variant would be <paramref name="size"/> along the part's axis. Returns false if the
    /// part has no models, so the caller draws its code shape.
    /// </summary>
    public static bool Append(string id, int key, Vector3 at, Quaternion rot, float size,
                              Color baseCol, Color tipCol, Color accentCol, Vector2 fx,
                              List<Vector3> verts, List<Vector3> normals, List<Color> colors,
                              List<Vector2> flex, List<int> tris)
    {
        var (variants, span) = Get(id);
        if (variants == null) return false;
        if (size <= 0f) return true;
        var   v     = variants[(key & 0x7FFFFFFF) % variants.Length];
        float k     = size / span;
        int   start = verts.Count;
        for (int i = 0; i < v.verts.Length; i++)
        {
            verts.Add(at + rot * (v.verts[i] * k));
            normals.Add(rot * v.normals[i]);
            colors.Add(v.accent[i] ? accentCol : Color.Lerp(baseCol, tipCol, v.along[i]));
            flex.Add(fx);
        }
        foreach (int t in v.tris) tris.Add(start + t);
        return true;
    }

    static (Variant[] variants, float span) Get(string id)
    {
        if (!loaded)
        {
            loaded  = true;
            library = Resources.Load<FlowerPartLibrary>("FlowerPartLibrary");
        }
        if (library == null) return (null, 0f);
        if (!parts.TryGetValue(id, out var p))
        {
            p = Build(id, library.Find(id));
            parts[id] = p;
        }
        return p;
    }

    static float Measure(Vector3 p, Axis axis) => axis switch
    {
        Axis.Down   => -p.y,
        Axis.Radial => new Vector2(p.x, p.z).magnitude,
        _           => p.z,
    };

    static (Variant[], float) Build(string id, GameObject[] models)
    {
        if (models == null || models.Length == 0) return (null, 0f);
        var   axis = AxisOf(id);
        var   list = new List<Variant>();
        float span = 0f;
        foreach (var model in models)
        {
            var mf   = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) continue;
            if (!mesh.isReadable)
            {
                Debug.LogWarning($"[FlowerParts] {model.name} isn't readable; run Bonsai ▸ Set Up Imported Models.");
                continue;
            }
            // The model's own rotation and scale (an unapplied Blender rotation lands on the FBX
            // root) without its position: the origin is where the part attaches.
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            var src = mesh.vertices;
            var nrm = mesh.normals;
            var v   = new Variant
            {
                verts   = new Vector3[src.Length], normals = new Vector3[src.Length],
                along   = new float[src.Length],   accent  = new bool[src.Length],
                tris    = mesh.triangles,
            };
            float far = 0f;
            for (int i = 0; i < src.Length; i++)
            {
                v.verts[i]   = m.MultiplyPoint3x4(src[i]);
                v.normals[i] = nrm.Length == src.Length ? m.MultiplyVector(nrm[i]).normalized : Vector3.up;
                far = Mathf.Max(far, Measure(v.verts[i], axis));
            }
            if (far <= 1e-5f)
            {
                Debug.LogWarning($"[FlowerParts] {model.name} doesn't reach out from its origin along " +
                                 (axis == Axis.Down ? "-Y (Blender -Z)" : axis == Axis.Radial ? "its radius" : "+Z (Blender -Y)") +
                                 "; skipped. Check its origin and rotation.");
                continue;
            }
            if (span <= 0f) span = far;
            for (int i = 0; i < src.Length; i++) v.along[i] = Mathf.Clamp01(Measure(v.verts[i], axis) / span);

            var mats = mf.GetComponent<Renderer>() != null ? mf.GetComponent<Renderer>().sharedMaterials : null;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string name = mats != null && s < mats.Length && mats[s] != null ? mats[s].name.ToLowerInvariant() : "";
                bool   acc  = false;
                foreach (var a in AccentNames) acc |= name.Contains(a);
                if (acc) foreach (int t in mesh.GetTriangles(s)) v.accent[t] = true;
            }
            list.Add(v);
        }
        if (list.Count == 0) return (null, 0f);
        Debug.Log($"[FlowerParts] {id}: {list.Count} variant{(list.Count == 1 ? "" : "s")}");
        return (list.ToArray(), span);
    }
}
