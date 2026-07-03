using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural conifer foliage geometry.
///
/// The whole point is efficiency: a conifer has thousands of needles, so we never
/// make one object — or even one mesh — per needle. Instead we build ONE "tuft" mesh
/// that contains a whole bundle of needles, and the LeafManager drops a single tuft
/// GameObject at each branch tip (one tuft per terminal, not per needle). Every tuft
/// in a tree shares the same Mesh + Material, so the renderer batches them.
///
/// Each tuft is built in local space with +Z pointing outward along the branch and
/// the origin at the branch tip. A needle is a thin tapered double-sided quad.
/// </summary>
public static class NeedleMesh
{
    /// <summary>Builds a unit-scale tuft (the LeafManager scales it per species/season).
    /// variantSeed picks a deterministic variant — LeafManager builds a small pool and
    /// indexes it by node id, so tips don't all repeat the one identical tuft.</summary>
    public static Mesh Build(FoliageType type, int needleCount, int variantSeed = 0)
    {
        var verts = new List<Vector3>(needleCount * 8);
        var tris  = new List<int>(needleCount * 12);

        // Deterministic per (type, count, variant) so the shared meshes look stable across sessions.
        var rng = new System.Random(7919 + (int)type * 131 + needleCount + variantSeed * 7717);
        float R(float a, float b) => (float)(a + rng.NextDouble() * (b - a));

        switch (type)
        {
            case FoliageType.PineFascicle:  BuildPine  (verts, tris, needleCount, R); break;
            case FoliageType.SpruceRadial:  BuildRadial(verts, tris, needleCount, 0.55f, 0.030f, R); break;
            case FoliageType.FeatheryFrond: BuildFrond (verts, tris, needleCount, R); break;
            case FoliageType.Scale:         BuildRadial(verts, tris, needleCount, 0.28f, 0.055f, R); break;
            default:                        BuildRadial(verts, tris, needleCount, 0.55f, 0.030f, R); break;
        }

        var mesh = new Mesh { name = "NeedleTuft_" + type + "_v" + variantSeed };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>One lone needle (double-sided tapered quad) — used for the year-round
    /// evergreen shed, where single spent needles drift off the canopy.</summary>
    public static Mesh BuildSingleNeedle()
    {
        var verts = new List<Vector3>(8);
        var tris  = new List<int>(12);
        AddNeedle(verts, tris, Vector3.zero, Vector3.forward, 0.6f, 0.025f, 0.004f);
        var mesh = new Mesh { name = "ShedNeedle" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── One needle ────────────────────────────────────────────────────────────
    // A tapered quad from `baseC` along `dir` for `len`, wide at the base and nearly
    // pointed at the tip. Emitted double-sided (front + reversed back, with their own
    // duplicated verts) so RecalculateNormals lights both faces correctly — thin
    // foliage must never go dark edge-on.
    static void AddNeedle(List<Vector3> v, List<int> t,
                          Vector3 baseC, Vector3 dir, float len, float wBase, float wTip)
    {
        dir = dir.normalized;
        // Width axis perpendicular to the needle; face normal ends up ≈ cross(side, dir).
        Vector3 refA = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        Vector3 side = Vector3.Cross(dir, refA).normalized;
        Vector3 tipC = baseC + dir * len;

        Vector3 a = baseC - side * (wBase * 0.5f);
        Vector3 b = baseC + side * (wBase * 0.5f);
        Vector3 c = tipC  + side * (wTip  * 0.5f);
        Vector3 d = tipC  - side * (wTip  * 0.5f);

        int f = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);          // front
        t.Add(f); t.Add(f + 1); t.Add(f + 2);
        t.Add(f); t.Add(f + 2); t.Add(f + 3);

        int k = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);          // back (reversed winding)
        t.Add(k); t.Add(k + 2); t.Add(k + 1);
        t.Add(k); t.Add(k + 3); t.Add(k + 2);
    }

    // ── Pine: long needles in fascicles (bundles of 2–5) fanning forward ────────
    static void BuildPine(List<Vector3> v, List<int> t, int count, System.Func<float, float, float> R)
    {
        int fascicles = Mathf.Max(3, count / 3);
        int perBundle = Mathf.Max(2, Mathf.RoundToInt((float)count / fascicles));
        for (int fI = 0; fI < fascicles; fI++)
        {
            float az   = R(0f, 360f) * Mathf.Deg2Rad;
            float tilt = R(8f, 52f)  * Mathf.Deg2Rad;          // forward cone around +Z
            Vector3 fdir = new Vector3(
                Mathf.Sin(tilt) * Mathf.Cos(az),
                Mathf.Sin(tilt) * Mathf.Sin(az) * 0.6f + 0.25f, // bias slightly up
                Mathf.Cos(tilt)).normalized;
            Vector3 baseC = new Vector3(R(-0.05f, 0.05f), R(-0.04f, 0.04f), R(-0.12f, 0.04f));
            for (int n = 0; n < perBundle; n++)
            {
                Vector3 jit = new Vector3(R(-0.10f, 0.10f), R(-0.10f, 0.10f), R(-0.04f, 0.04f));
                Vector3 dir = (fdir + jit).normalized;
                AddNeedle(v, t, baseC, dir, R(0.82f, 1.05f), 0.018f, 0.003f);
            }
        }
    }

    // ── Spruce / cedar / scale: short needles radiating all round, forward-biased ─
    static void BuildRadial(List<Vector3> v, List<int> t, int count,
                            float len, float width, System.Func<float, float, float> R)
    {
        for (int i = 0; i < count; i++)
        {
            // Forward-biased sphere: needles point everywhere but lean outward (+Z).
            Vector3 dir = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-0.35f, 1f));
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 baseC = new Vector3(R(-0.04f, 0.04f), R(-0.04f, 0.04f), R(-0.10f, 0.06f));
            AddNeedle(v, t, baseC, dir, len * R(0.7f, 1.05f), width, 0.004f);
        }
    }

    // ── Feathery frond: flat fern-like spray in the local XZ plane ──────────────
    static void BuildFrond(List<Vector3> v, List<int> t, int count, System.Func<float, float, float> R)
    {
        int pairs = Mathf.Max(3, count / 2);
        const float rachis = 1f;
        for (int i = 0; i < pairs; i++)
        {
            float z = (i + 0.5f) / pairs * rachis;
            float taper = 1f - 0.35f * z;                       // shorter toward the tip
            Vector3 baseC = new Vector3(0f, 0f, z);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 dir = new Vector3(s * 0.85f, R(-0.06f, 0.06f), 0.4f).normalized;
                AddNeedle(v, t, baseC, dir, 0.5f * taper * R(0.85f, 1.02f), 0.022f, 0.004f);
            }
        }
        // A couple of needles continuing straight off the tip.
        for (int n = 0; n < 2; n++)
            AddNeedle(v, t, new Vector3(0f, 0f, rachis), new Vector3(R(-0.1f, 0.1f), 0f, 1f).normalized,
                      0.32f, 0.02f, 0.004f);
    }
}
