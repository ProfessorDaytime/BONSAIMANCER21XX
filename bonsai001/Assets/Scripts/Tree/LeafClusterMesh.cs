using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural broadleaf foliage, needle-tuft style (2026-07-05): ONE cluster mesh holds
/// a whole fan of leaf blades, and LeafManager places a single cluster GameObject per
/// branch tip. Replaces the per-leaf prefab path, which spawned one GameObject per leaf
/// AND a unique deformed mesh copy per leaf (ApplyDeformation → Instantiate(mesh)) —
/// ~2,500 unbatchable objects on a mature maple. Clusters come from a small shared pool
/// (variants indexed by node id), so everything batches like the needles do.
///
/// Blades are species-shaped 2D outlines (lobed maple, ovate ficus, deltoid birch,
/// lanceolate willow, pinnate wisteria) built as double-sided fans with baked twist
/// and droop variety. Built in local space: +Z points outward along the branch,
/// origin at the branch tip, blades fanning sideways with a slight rise — matching
/// the planar-pad aesthetic.
/// </summary>
public static class LeafClusterMesh
{
    /// <summary>Builds a unit-scale cluster of `bladeCount` leaves (LeafManager scales
    /// per species/season). Deterministic per (shape, count, variant).</summary>
    public static Mesh Build(LeafShape shape, int bladeCount, int variantSeed = 0)
    {
        var verts = new List<Vector3>(bladeCount * 40);
        var tris  = new List<int>(bladeCount * 60);

        var rng = new System.Random(4211 + (int)shape * 173 + bladeCount + variantSeed * 5501);
        float R(float a, float b) => (float)(a + rng.NextDouble() * (b - a));

        for (int i = 0; i < bladeCount; i++)
        {
            // Fan the blades sideways around +Z with a slight upward rise — planar pads.
            float az   = (i + 0.5f) / bladeCount * 300f - 150f + R(-14f, 14f);   // spread, not full circle
            float elev = R(-8f, 20f);
            Quaternion rot = Quaternion.AngleAxis(az, Vector3.up)
                           * Quaternion.AngleAxis(-elev, Vector3.right);
            Vector3 baseC = new Vector3(R(-0.06f, 0.06f), R(-0.03f, 0.05f), R(-0.10f, 0.06f));
            float   size  = R(0.75f, 1.05f);
            float   twist = R(-22f, 22f);
            float   droop = R(0.05f, 0.30f);

            AddBlade(verts, tris, shape, baseC, rot, size, twist, droop, R);
        }

        var mesh = new Mesh { name = $"LeafCluster_{shape}_v{variantSeed}" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── One blade ─────────────────────────────────────────────────────────────
    // Outline sampled as (along, halfWidth) pairs from stem (t=0) to tip (t=1),
    // triangulated as a centre-spine fan, twisted around the spine and drooped,
    // then emitted double-sided so RecalculateNormals lights both faces.
    static void AddBlade(List<Vector3> v, List<int> t, LeafShape shape,
                         Vector3 baseC, Quaternion rot, float size,
                         float twistDeg, float droop, System.Func<float, float, float> R)
    {
        (float len, float[] widths) = Outline(shape, R);

        int   rows  = widths.Length;
        int   start = v.Count;

        // Front side: spine + left/right edge verts per row.
        for (int r = 0; r < rows; r++)
        {
            float tt   = r / (float)(rows - 1);
            float along = tt * len * size;
            float halfW = widths[r] * size;

            float twist = twistDeg * tt * Mathf.Deg2Rad;
            float dy    = -droop * along * tt;                       // tip curls down

            Vector3 spine = new Vector3(0f, dy, along);
            Vector3 side  = new Vector3(Mathf.Cos(twist), Mathf.Sin(twist), 0f) * halfW;

            v.Add(baseC + rot * (spine - side));
            v.Add(baseC + rot * spine);
            v.Add(baseC + rot * (spine + side));
        }
        for (int r = 0; r < rows - 1; r++)
        {
            int a = start + r * 3, b = start + (r + 1) * 3;
            // left strip + right strip (clockwise from front)
            t.Add(a);     t.Add(b);     t.Add(a + 1);
            t.Add(a + 1); t.Add(b);     t.Add(b + 1);
            t.Add(a + 1); t.Add(b + 1); t.Add(a + 2);
            t.Add(a + 2); t.Add(b + 1); t.Add(b + 2);
        }

        // Back side: duplicate verts, reversed winding.
        int backStart = v.Count;
        int frontCount = rows * 3;
        for (int i = 0; i < frontCount; i++) v.Add(v[start + i]);
        for (int r = 0; r < rows - 1; r++)
        {
            int a = backStart + r * 3, b = backStart + (r + 1) * 3;
            t.Add(a);     t.Add(a + 1); t.Add(b);
            t.Add(a + 1); t.Add(b + 1); t.Add(b);
            t.Add(a + 1); t.Add(a + 2); t.Add(b + 1);
            t.Add(a + 2); t.Add(b + 2); t.Add(b + 1);
        }
    }

    /// <summary>Species blade outlines: length + half-width profile from stem to tip.</summary>
    static (float, float[]) Outline(LeafShape shape, System.Func<float, float, float> R)
    {
        switch (shape)
        {
            case LeafShape.Lobed:       // maple: wide, spiky lobes
                return (0.95f, new[] { 0.06f, 0.30f, 0.16f, 0.42f, 0.14f, 0.30f, 0.04f });
            case LeafShape.Deltoid:     // birch: triangular, widest near the base
                return (0.85f, new[] { 0.10f, 0.34f, 0.28f, 0.20f, 0.12f, 0.05f });
            case LeafShape.Lanceolate:  // willow: long and narrow
                return (1.25f, new[] { 0.04f, 0.10f, 0.12f, 0.11f, 0.08f, 0.05f, 0.02f });
            case LeafShape.Pinnate:     // wisteria: slim leaflet (clusters read as compound)
                return (1.00f, new[] { 0.05f, 0.13f, 0.15f, 0.13f, 0.09f, 0.03f });
            case LeafShape.Ovate:       // ficus/elm/cherry: classic oval, widest mid-blade
            default:
                return (0.90f, new[] { 0.07f, 0.22f, 0.28f, 0.26f, 0.18f, 0.06f });
        }
    }
}

/// <summary>Broadleaf blade silhouettes for the procedural cluster foliage.</summary>
public enum LeafShape { Ovate, Lobed, Deltoid, Lanceolate, Pinnate }
