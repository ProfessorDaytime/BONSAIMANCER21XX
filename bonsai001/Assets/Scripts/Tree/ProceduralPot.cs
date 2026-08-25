using UnityEngine;

/// <summary>
/// Zero-art placeholder pot meshes, generated to EXACTLY the logical PotDimensions
/// (2026-07-04, queue #3 calibration). PotSoil.ApplyPotSize swaps one in whenever the
/// pot size changes, so the visual pot and the root-containment box can never drift
/// apart again — replace with authored models per size later (assign them on the pot
/// object and turn off usePlaceholderPotMesh).
///
/// Shape: octagonal frustum planter — rim slightly wider than base, flat rim lip,
/// shallow inner cavity with a soil disc. Submesh 0 = ceramic, submesh 1 = soil,
/// so PotSoil can assign two simple materials.
/// </summary>
public static class ProceduralPot
{
    /// <summary>Soil surface for an authored (open-shell) pot mesh: a slightly domed disc with a
    /// short downward skirt so it reads as a filled pot rather than a paper-thin plane. Origin at
    /// the soil surface centre; the skirt hangs below into the pot. Single submesh (soil).</summary>
    public static Mesh BuildSoilSurface(float radX, float radZ, float skirtDepth)
    {
        const int sides = 16;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        // Gentle crown so the surface catches light instead of reading flat.
        float crown = Mathf.Min(radX, radZ) * 0.06f;

        int centre = verts.Count;
        verts.Add(new Vector3(0f, crown, 0f));

        int rim = verts.Count;
        for (int i = 0; i < sides; i++)
        {
            float a = (i + 0.5f) / sides * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(a) * radX, 0f, Mathf.Sin(a) * radZ));
        }
        int skirt = verts.Count;
        for (int i = 0; i < sides; i++)
        {
            float a = (i + 0.5f) / sides * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(a) * radX, -skirtDepth, Mathf.Sin(a) * radZ));
        }

        // Top fan — wound so the surface faces UP (verified against ProceduralPot.Cap(up:true)).
        for (int i = 0; i < sides; i++)
        {
            int i2 = (i + 1) % sides;
            tris.Add(centre); tris.Add(rim + i2); tris.Add(rim + i);
        }
        // Skirt wall, outward-facing (same winding rule as the pot's outer wall).
        for (int i = 0; i < sides; i++)
        {
            int i2 = (i + 1) % sides;
            int t0 = rim + i, t1 = rim + i2, b0 = skirt + i, b1 = skirt + i2;
            tris.Add(t0); tris.Add(b0); tris.Add(t1);
            tris.Add(t1); tris.Add(b0); tris.Add(b1);
        }

        var mesh = new Mesh { name = $"PotSoil_{radX:F2}x{radZ:F2}" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Builds a pot of world size w×d (rim outer) × h tall. Origin at the
    /// CENTER of the containment box: rim top sits at +h/2, base at -h/2 — matching
    /// how the rootArea box is scaled around its transform.</summary>
    public static Mesh Build(float w, float d, float h)
    {
        const int   sides     = 8;
        const float baseScale = 0.78f;   // base narrower than rim (planter taper)
        const float lipFrac   = 0.08f;   // rim lip width, fraction of radius
        const float soilDrop  = 0.035f;  // soil surface just below the rim — the tree plants at
                                         // the BOX TOP (= rim plane), so a deep soil drop left the
                                         // trunk + surface roots hovering above visible soil
                                         // (2026-07-04 "floating above the pot")

        float rx = w * 0.5f, rz = d * 0.5f;
        float top = h * 0.5f, bot = -h * 0.5f;

        var verts = new System.Collections.Generic.List<Vector3>();
        var ceramic = new System.Collections.Generic.List<int>();
        var soil    = new System.Collections.Generic.List<int>();

        Vector3 P(float a, float sx, float sz, float y) =>
            new Vector3(Mathf.Cos(a) * rx * sx, y, Mathf.Sin(a) * rz * sz);

        // Ring helper: adds `sides` verts at angle steps, returns start index.
        int Ring(float sx, float sz, float y)
        {
            int start = verts.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) / sides * Mathf.PI * 2f;   // rotated half-step: flat front face
                verts.Add(P(a, sx, sz, y));
            }
            return start;
        }

        void Wall(int ringA, int ringB, System.Collections.Generic.List<int> tris, bool flip = false)
        {
            for (int i = 0; i < sides; i++)
            {
                int i2 = (i + 1) % sides;
                int a0 = ringA + i, a1 = ringA + i2, b0 = ringB + i, b1 = ringB + i2;
                if (!flip) { tris.Add(a0); tris.Add(b0); tris.Add(a1); tris.Add(a1); tris.Add(b0); tris.Add(b1); }
                else       { tris.Add(a0); tris.Add(a1); tris.Add(b0); tris.Add(a1); tris.Add(b1); tris.Add(b0); }
            }
        }

        void Cap(int ring, float y, System.Collections.Generic.List<int> tris, bool up)
        {
            int c = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            for (int i = 0; i < sides; i++)
            {
                int i2 = (i + 1) % sides;
                if (up) { tris.Add(c); tris.Add(ring + i2); tris.Add(ring + i); }
                else    { tris.Add(c); tris.Add(ring + i);  tris.Add(ring + i2); }
            }
        }

        // Outer wall: base → rim. Winding verified NUMERICALLY (memory
        // feedback_mesh_winding — mental rotation has now failed three times):
        // unflipped tri (a0,b0,a1) at 45° gives cross(b0-a0, a1-a0) = (+0.54h, 0, +0.54h)
        // = outward. flip:true pointed the whole outer wall INWARD → the wall culled
        // from outside and the pot rendered see-through with a black far-interior
        // (2026-07-04 "weird occlusion").
        int outerBot = Ring(baseScale, baseScale, bot);
        int outerTop = Ring(1f, 1f, top);
        Wall(outerBot, outerTop, ceramic, flip: false);

        // Rim lip: outer rim → inner rim (flat, facing up)
        float innerS = 1f - lipFrac * 2f;
        int innerTop = Ring(innerS, innerS, top);
        Wall(outerTop, innerTop, ceramic);            // upward-facing lip

        // Inner cavity wall: inner rim → soil ring
        float soilY  = top - h * soilDrop;
        int soilRing = Ring(innerS * 0.96f, innerS * 0.96f, soilY);
        Wall(innerTop, soilRing, ceramic);

        // Soil disc
        Cap(soilRing, soilY, soil, up: true);

        // Bottom cap (closes the base)
        Cap(outerBot, bot, ceramic, up: false);

        var mesh = new Mesh { name = $"Pot_{w:F2}x{d:F2}x{h:F2}" };
        mesh.subMeshCount = 2;
        mesh.SetVertices(verts);
        mesh.SetTriangles(ceramic, 0);
        mesh.SetTriangles(soil, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
