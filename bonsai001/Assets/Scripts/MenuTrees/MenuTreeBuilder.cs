using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Turns a <see cref="MenuTreeSnapshot"/> back into a static tree (2026-10-29). Used by the main
/// menu (<see cref="MenuTreeShowcase"/>) and the greenhouse (<see cref="Greenhouse"/>).
///
/// <para>Model pieces — every leaf, flower, the pot — are saved as references, one per object.
/// Where the registry has a readable copy of the model, those pieces are merged per material and
/// leaf colour into a few meshes, so a tree costs a handful of draw calls instead of thousands of
/// objects. Pieces without a readable copy are placed one by one, as before.</para>
/// </summary>
public static class MenuTreeBuilder
{
    const int MaxColoursPerMaterial = 16;
    const int ExposureBands = 5;   // leaf groups kept apart by canopy exposure, so they can turn in order

    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor     = Shader.PropertyToID("_Color");

    /// <summary>The tree's materials, built once per snapshot; index-matched to
    /// <see cref="MenuTreeSnapshot.materials"/>.</summary>
    public static Material[] BuildMaterials(MenuTreeSnapshot snap)
    {
        var mats = new Material[snap.materials.Count];
        for (int i = 0; i < mats.Length; i++) mats[i] = BuildMaterial(snap.materials[i]);
        return mats;
    }

    /// <summary>Builds the tree under a new GameObject at the origin, rotated and scaled as the
    /// live tree root was. Returns null if nothing could be built.</summary>
    public static GameObject Build(MenuTreeSnapshot snap, Material[] mats, string name = "MenuTree")
    {
        if (snap == null || snap.parts.Count == 0) return null;

        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(Vector3.zero, snap.rootRotation);
        go.transform.localScale = snap.rootScale;

        var mpb    = new MaterialPropertyBlock();
        var pieces = new List<(CombineInstance ci, int mat, Color? tint, ShadowCastingMode cast, bool receive)>();
        int built  = 0;

        foreach (var p in snap.parts)
        {
            if (p.mesh != null)
            {
                AddPart(go.transform, BuildMesh(p.mesh), Matrix4x4.identity, Lookup(mats, p.materials),
                        p.hasTint ? p.tint : (Color?)null, p.cast, p.receive, mpb);
                built++;
                continue;
            }

            var twin = MenuTreeRegistry.ReadableTwin(p.meshKey);
            if (twin != null)
            {
                for (int s = 0; s < twin.subMeshCount && s < p.materials.Length; s++)
                {
                    if (p.materials[s] < 0) continue;
                    pieces.Add((new CombineInstance { mesh = twin, subMeshIndex = s, transform = p.localMatrix },
                                p.materials[s], p.hasTint ? p.tint : (Color?)null, p.cast, p.receive));
                }
                continue;
            }

            var mesh = MenuTreeRegistry.Resolve<Mesh>(p.meshKey);
            if (mesh == null) { Debug.LogWarning($"[MenuTree] Missing model {p.meshKey}; part skipped."); continue; }
            AddPart(go.transform, mesh, p.localMatrix, Lookup(mats, p.materials),
                    p.hasTint ? p.tint : (Color?)null, p.cast, p.receive, mpb);
            built++;
        }

        // Merge the model pieces per (material, colour cluster).
        if (pieces.Count > 0)
        {
            var clusters = ClusterTints(pieces.ConvertAll(x => (x.mat, x.tint)), out var centres);
            var exposure = LeafExposure(pieces.ConvertAll(x => (x.tint.HasValue, (Vector3)x.ci.transform.GetColumn(3))));
            var groups = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < pieces.Count; i++)
            {
                int band = pieces[i].tint.HasValue
                    ? Mathf.Min(ExposureBands - 1, Mathf.FloorToInt(exposure[i] * ExposureBands)) : -1;
                var key = (pieces[i].mat, clusters[i], band);
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<int>();
                l.Add(i);
            }
            foreach (var kv in groups)
            {
                var idx = kv.Value;
                var cis = new CombineInstance[idx.Count];
                for (int i = 0; i < idx.Count; i++) cis[i] = pieces[idx[i]].ci;
                var combined = new Mesh { indexFormat = IndexFormat.UInt32, name = "MenuTreeMerged" };
                combined.CombineMeshes(cis, mergeSubMeshes: true, useMatrices: true);
                var first = pieces[idx[0]];
                Color? tint = kv.Key.Item2 >= 0 ? centres[(kv.Key.Item1, kv.Key.Item2)] : (Color?)null;
                var part = AddPart(go.transform, combined, Matrix4x4.identity, new[] { Mat(mats, kv.Key.Item1) },
                                   tint, first.cast, first.receive, mpb);
                if (kv.Key.Item3 >= 0)
                    part.AddComponent<MenuTreeLeaf>().exposure = (kv.Key.Item3 + 0.5f) / ExposureBands;
                built++;
            }
        }

        if (built == 0) { Object.Destroy(go); return null; }
        return go;
    }

    // ── Parts ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 0–1 exposure per piece: height in the canopy and distance out from the trunk axis, both
    /// normalised over the leaf pieces (tree-root space; the trunk base is the origin). Sun reaches
    /// the top and the outer shell first — that's where autumn colour starts (2026-10-29).
    /// </summary>
    static float[] LeafExposure(List<(bool leaf, Vector3 pos)> items)
    {
        var result = new float[items.Count];
        float minY = float.MaxValue, maxY = float.MinValue, maxR = 0f;
        foreach (var (leaf, p) in items)
        {
            if (!leaf) continue;
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            maxR = Mathf.Max(maxR, new Vector2(p.x, p.z).magnitude);
        }
        for (int i = 0; i < items.Count; i++)
        {
            var (leaf, p) = items[i];
            if (!leaf) continue;
            float h = maxY > minY ? (p.y - minY) / (maxY - minY) : 0.5f;
            float r = maxR > 0f ? new Vector2(p.x, p.z).magnitude / maxR : 0.5f;
            result[i] = Mathf.Clamp01(0.55f * h + 0.45f * r);
        }
        return result;
    }

    static GameObject AddPart(Transform parent, Mesh mesh, Matrix4x4 local, Material[] materials, Color? tint,
                              ShadowCastingMode cast, bool receive, MaterialPropertyBlock mpb)
    {
        var part = new GameObject("Part");
        part.transform.SetParent(parent, false);
        part.transform.localPosition = local.GetPosition();
        part.transform.localRotation = local.rotation;
        part.transform.localScale    = local.lossyScale;

        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = part.AddComponent<MeshRenderer>();
        mr.sharedMaterials   = materials;
        mr.shadowCastingMode = cast;
        mr.receiveShadows    = receive;
        if (tint is Color c)
        {
            mpb.Clear();
            mpb.SetColor(IdBaseColor, c);
            mpb.SetColor(IdColor, c);
            mr.SetPropertyBlock(mpb);
        }
        return part;
    }

    static Material Mat(Material[] mats, int i) => i >= 0 && i < mats.Length ? mats[i] : null;

    static Material[] Lookup(Material[] mats, int[] idx)
    {
        var list = new Material[Mathf.Max(1, idx.Length)];
        for (int i = 0; i < idx.Length; i++) list[i] = Mat(mats, idx[i]);
        return list;
    }

    /// <summary>Per material, k-means the tints down to at most
    /// <see cref="MaxColoursPerMaterial"/>. Returns each piece's cluster (-1 = untinted).</summary>
    static int[] ClusterTints(List<(int mat, Color? tint)> items, out Dictionary<(int, int), Color> centresOut)
    {
        var result = new int[items.Count];
        for (int i = 0; i < result.Length; i++) result[i] = -1;
        centresOut = new Dictionary<(int, int), Color>();

        var byMat = new Dictionary<int, List<int>>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].tint == null) continue;
            if (!byMat.TryGetValue(items[i].mat, out var l)) byMat[items[i].mat] = l = new List<int>();
            l.Add(i);
        }

        foreach (var kv in byMat)
        {
            var list = kv.Value;
            var sorted = new List<int>(list);
            sorted.Sort((a, b) => Luma(items[a].tint.Value).CompareTo(Luma(items[b].tint.Value)));
            int k = Mathf.Min(MaxColoursPerMaterial, sorted.Count);
            var centres = new Color[k];
            for (int i = 0; i < k; i++)
                centres[i] = items[sorted[(int)((i + 0.5f) / k * sorted.Count)]].tint.Value;

            for (int iter = 0; iter < 12; iter++)
            {
                var sum   = new Vector4[k];
                var count = new int[k];
                foreach (int i in list)
                {
                    var c = items[i].tint.Value;
                    int best = 0; float bestD = float.MaxValue;
                    for (int j = 0; j < k; j++)
                    {
                        float dr = centres[j].r - c.r, dg = centres[j].g - c.g, db = centres[j].b - c.b, da = centres[j].a - c.a;
                        float d = dr * dr + dg * dg + db * db + da * da;
                        if (d < bestD) { bestD = d; best = j; }
                    }
                    result[i] = best;
                    sum[best] += (Vector4)c;
                    count[best]++;
                }
                for (int j = 0; j < k; j++)
                    if (count[j] > 0) centres[j] = sum[j] / count[j];
            }
            for (int j = 0; j < k; j++) centresOut[(kv.Key, j)] = centres[j];
        }
        return result;
    }

    static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    // ── Meshes and materials ──────────────────────────────────────────────────

    static Mesh BuildMesh(MenuTreeSnapshot.MeshData d)
    {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "MenuTreePart" };
        mesh.vertices = d.vertices;
        if (d.normals  != null) mesh.normals  = d.normals;
        if (d.tangents != null) mesh.tangents = d.tangents;
        if (d.colors   != null) mesh.colors   = d.colors;
        for (int c = 0; c < 4; c++)
            if (d.uvs[c] != null) mesh.SetUVs(c, d.uvs[c]);
        mesh.triangles = d.triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material BuildMaterial(MenuTreeSnapshot.MaterialRecipe r)
    {
        var sh = Shader.Find(r.shader);
        if (sh == null)
        {
            Debug.LogWarning($"[MenuTree] Shader '{r.shader}' isn't in this build; using URP Lit.");
            sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return null;
        }
        var m = new Material(sh);
        foreach (var (n, v) in r.floats)
        {
            if (!m.HasProperty(n)) continue;
            int pi = sh.FindPropertyIndex(n);
            if (pi >= 0 && sh.GetPropertyType(pi) == ShaderPropertyType.Int) m.SetInteger(n, (int)v);
            else m.SetFloat(n, v);
        }
        foreach (var (n, v) in r.colors)  if (m.HasProperty(n)) m.SetColor(n, v);
        foreach (var (n, v) in r.vectors) if (m.HasProperty(n)) m.SetVector(n, v);
        foreach (var (n, key, sc, of) in r.textures)
        {
            if (!m.HasProperty(n)) continue;
            var tex = MenuTreeRegistry.Resolve<Texture>(key);
            if (tex == null) continue;
            m.SetTexture(n, tex);
            m.SetTextureScale(n, sc);
            m.SetTextureOffset(n, of);
        }
        m.shaderKeywords = r.keywords;
        m.renderQueue    = r.renderQueue;
        return m;
    }
}
