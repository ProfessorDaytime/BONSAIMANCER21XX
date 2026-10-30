using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Turns the live tree into a <see cref="MenuTreeSnapshot"/> (2026-10-29). The runtime counterpart
/// of the editor-only <c>MenuTreeBaker</c>, and it collects the same things: bark, leaves, flowers,
/// pot, soil, table, an Ishitsuki rock and the equipped decoration.
///
/// <list type="bullet">
/// <item>Readable meshes (bark, soil, the leaves' twisted copies) are combined per material and
/// leaf colour, in the tree root's local space, and saved as vertex data.</item>
/// <item>Unreadable project models (pots, tables, rocks, decorations) are saved as a reference plus
/// a transform; <see cref="MenuTreeRegistry"/> turns the reference back into the model.</item>
/// <item>Every material is saved as a recipe (shader + property values), since most are made at
/// runtime.</item>
/// <item>Leaves are snapped to their windless rest pose first, as in the baker, so no leaf is
/// captured mid-gust and off its twig.</item>
/// </list>
/// </summary>
public static class MenuTreeCapture
{
    const int MaxColoursPerMaterial = 16;

    static readonly HashSet<string> ExcludedNames = new HashSet<string>
    {
        "_TreeHighlight", "_WireAimPreview", "_SawGroove", "_SawDust", "_SawSplinters",
    };

    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor     = Shader.PropertyToID("_Color");

    class Piece
    {
        public CombineInstance   ci;
        public Material          mat;
        public Color?            tint;
        public int               cluster = -1;
        public ShadowCastingMode cast;
        public bool              receive;
    }

    /// <summary>Captures the scene's tree, or returns null if there is none to capture.</summary>
    public static MenuTreeSnapshot Capture(TreeSkeleton skeleton)
    {
        if (skeleton == null || skeleton.root == null) return null;
        var root = skeleton.transform;

        foreach (var leaf in root.GetComponentsInChildren<Leaf>()) leaf.SnapToRest();

        var snap   = new MenuTreeSnapshot();
        var matIdx = new Dictionary<Material, int>();
        int Recipe(Material m)
        {
            if (!matIdx.TryGetValue(m, out int i))
            {
                i = snap.materials.Count;
                snap.materials.Add(ToRecipe(m));
                matIdx[m] = i;
            }
            return i;
        }

        var toRoot  = root.worldToLocalMatrix;
        var mpb     = new MaterialPropertyBlock();
        var pieces  = new List<Piece>();
        int skipped = 0;

        foreach (var r in CollectRenderers(root))
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            var mats = r.sharedMaterials;
            if (mats.Length == 0) continue;

            Color? tint = null;
            if (r.HasPropertyBlock())
            {
                r.GetPropertyBlock(mpb);
                if (mpb.HasColor(IdBaseColor))  tint = mpb.GetColor(IdBaseColor);
                else if (mpb.HasColor(IdColor)) tint = mpb.GetColor(IdColor);
            }
            var m = toRoot * r.localToWorldMatrix;

            if (mesh.isReadable)
            {
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats[Mathf.Min(s, mats.Length - 1)];
                    if (mat == null) continue;
                    pieces.Add(new Piece
                    {
                        ci      = new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m },
                        mat     = mat,
                        tint    = tint,
                        cast    = r.shadowCastingMode,
                        receive = r.receiveShadows,
                    });
                }
                continue;
            }

            string key = MenuTreeRegistry.KeyOf(mesh);
            if (key == null)
            {
                skipped++;
                Debug.LogWarning($"[MenuTree] Skipped '{r.name}': its mesh is neither readable nor a known asset.");
                continue;
            }
            var idx = new int[mesh.subMeshCount];
            for (int s = 0; s < idx.Length; s++)
            {
                var mat = mats[Mathf.Min(s, mats.Length - 1)];
                idx[s] = mat != null ? Recipe(mat) : -1;
            }
            snap.parts.Add(new MenuTreeSnapshot.Part
            {
                meshKey     = key,
                localMatrix = m,
                materials   = idx,
                hasTint     = tint.HasValue,
                tint        = tint ?? Color.white,
                cast        = r.shadowCastingMode,
                receive     = r.receiveShadows,
            });
        }

        ClusterTints(pieces);

        // Combine the readable pieces per (material, colour cluster).
        var groups = new Dictionary<(Material, int), List<Piece>>();
        foreach (var p in pieces)
        {
            var key = (p.mat, p.cluster);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Piece>();
            list.Add(p);
        }
        int verts = 0;
        foreach (var list in groups.Values)
        {
            var first = list[0];
            var cis = new CombineInstance[list.Count];
            for (int i = 0; i < list.Count; i++) cis[i] = list[i].ci;
            var combined = new Mesh { indexFormat = IndexFormat.UInt32 };
            combined.CombineMeshes(cis, mergeSubMeshes: true, useMatrices: true);
            verts += combined.vertexCount;
            snap.parts.Add(new MenuTreeSnapshot.Part
            {
                mesh      = ToData(combined),
                materials = new[] { Recipe(first.mat) },
                hasTint   = first.tint.HasValue,
                tint      = first.tint ?? Color.white,
                cast      = first.cast,
                receive   = first.receive,
            });
            Object.Destroy(combined);
        }

        CapturePresentation(snap, root, skeleton, Recipe);
        Debug.Log($"[MenuTree] Captured {snap.parts.Count} parts, {verts:N0} combined verts, " +
                  $"{snap.materials.Count} materials" + (skipped > 0 ? $", {skipped} skipped" : ""));
        return snap;
    }

    /// <summary>A small PNG of the current view for the menu-tree gallery, or null.</summary>
    public static byte[] Thumbnail(int width = 320, int height = 180)
    {
        var cam = Camera.main;
        if (cam == null) return null;
        var rt   = RenderTexture.GetTemporary(width, height, 24);
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.Destroy(tex);
            return png;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MenuTree] Thumbnail failed: {e.Message}");
            return null;
        }
        finally
        {
            cam.targetTexture = prev;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    // ── Collection (mirrors MenuTreeBaker) ────────────────────────────────────

    static List<MeshRenderer> CollectRenderers(Transform root)
    {
        var seen   = new HashSet<MeshRenderer>();
        var result = new List<MeshRenderer>();

        void AddUnder(Transform t)
        {
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
                if (Include(r, root) && seen.Add(r)) result.Add(r);
        }

        AddUnder(root);

        // The pot, soil surface and table aren't children of the tree root.
        var pot = root.GetComponent<PotSoil>();
        if (pot != null && pot.potVisual != null) AddUnder(pot.potVisual);
        foreach (var name in new[] { "_PotSoilSurface", "PlanterTable", "TinyTable01" })
        {
            var go = GameObject.Find(name);
            if (go != null) AddUnder(go.transform);
        }
        foreach (var rock in Object.FindObjectsByType<RockPlacer>())
            if (rock.isActiveAndEnabled) AddUnder(rock.transform);
        foreach (var t in Object.FindObjectsByType<Transform>())
            if (t.name.StartsWith("Decoration_") && t.gameObject.activeInHierarchy) AddUnder(t);

        return result;
    }

    static bool Include(MeshRenderer r, Transform root)
    {
        if (!r.enabled || !r.gameObject.activeInHierarchy) return false;
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;
        if (r.GetComponentInParent<Weed>() != null) return false;
        for (var t = r.transform; t != null && t != root.parent; t = t.parent)
            if (ExcludedNames.Contains(t.name)) return false;
        return true;
    }

    // ── Leaf colours (k-means, as in MenuTreeBaker) ───────────────────────────

    static void ClusterTints(List<Piece> pieces)
    {
        var byMat = new Dictionary<Material, List<Piece>>();
        foreach (var p in pieces)
        {
            if (p.tint == null) continue;
            if (!byMat.TryGetValue(p.mat, out var l)) byMat[p.mat] = l = new List<Piece>();
            l.Add(p);
        }

        foreach (var list in byMat.Values)
        {
            var sorted = new List<Piece>(list);
            sorted.Sort((a, b) => Luma(a.tint.Value).CompareTo(Luma(b.tint.Value)));
            int k = Mathf.Min(MaxColoursPerMaterial, sorted.Count);
            var centres = new Color[k];
            for (int i = 0; i < k; i++)
                centres[i] = sorted[(int)((i + 0.5f) / k * sorted.Count)].tint.Value;

            for (int iter = 0; iter < 12; iter++)
            {
                var sum   = new Vector4[k];
                var count = new int[k];
                foreach (var p in list)
                {
                    int best = Nearest(centres, p.tint.Value);
                    p.cluster = best;
                    sum[best] += (Vector4)p.tint.Value;
                    count[best]++;
                }
                for (int i = 0; i < k; i++)
                    if (count[i] > 0) centres[i] = sum[i] / count[i];
            }
            foreach (var p in list) p.tint = centres[p.cluster];
        }
    }

    static int Nearest(Color[] centres, Color c)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < centres.Length; i++)
        {
            float dr = centres[i].r - c.r, dg = centres[i].g - c.g, db = centres[i].b - c.b, da = centres[i].a - c.a;
            float d = dr * dr + dg * dg + db * db + da * da;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    // ── Conversion ────────────────────────────────────────────────────────────

    static MenuTreeSnapshot.MeshData ToData(Mesh mesh)
    {
        var d = new MenuTreeSnapshot.MeshData
        {
            vertices  = mesh.vertices,
            normals   = mesh.HasVertexAttribute(VertexAttribute.Normal)  ? mesh.normals  : null,
            tangents  = mesh.HasVertexAttribute(VertexAttribute.Tangent) ? mesh.tangents : null,
            colors    = mesh.HasVertexAttribute(VertexAttribute.Color)   ? mesh.colors   : null,
            triangles = mesh.triangles,
        };
        var uv = new List<Vector4>();
        for (int c = 0; c < 4; c++)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + c)) continue;
            mesh.GetUVs(c, uv);
            d.uvs[c] = uv.ToArray();
        }
        return d;
    }

    static MenuTreeSnapshot.MaterialRecipe ToRecipe(Material m)
    {
        var shader = m.shader;
        var r = new MenuTreeSnapshot.MaterialRecipe
        {
            shader      = shader.name,
            renderQueue = m.renderQueue,
            keywords    = m.shaderKeywords,
        };
        int n = shader.GetPropertyCount();
        for (int i = 0; i < n; i++)
        {
            string name = shader.GetPropertyName(i);
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    r.floats.Add((name, m.GetFloat(name))); break;
                case ShaderPropertyType.Int:
                    r.floats.Add((name, m.GetInteger(name))); break;
                case ShaderPropertyType.Color:
                    r.colors.Add((name, m.GetColor(name))); break;
                case ShaderPropertyType.Vector:
                    r.vectors.Add((name, m.GetVector(name))); break;
                case ShaderPropertyType.Texture:
                    string key = MenuTreeRegistry.KeyOf(m.GetTexture(name));
                    if (key != null)
                        r.textures.Add((name, key, m.GetTextureScale(name), m.GetTextureOffset(name)));
                    break;
            }
        }
        return r;
    }

    static void CapturePresentation(MenuTreeSnapshot s, Transform root, TreeSkeleton skeleton,
                                    System.Func<Material, int> recipe)
    {
        s.rootRotation = root.rotation;
        s.rootScale    = root.lossyScale;
        var invRot = Quaternion.Inverse(root.rotation);

        var cam = Camera.main;
        if (cam != null)
        {
            s.cameraLocalPosition = root.InverseTransformPoint(cam.transform.position);
            s.cameraLocalRotation = invRot * cam.transform.rotation;
            s.fieldOfView     = cam.fieldOfView;
            s.nearClip        = cam.nearClipPlane;
            s.farClip         = cam.farClipPlane;
            s.clearFlags      = cam.clearFlags;
            s.backgroundColor = cam.backgroundColor;
        }
        if (s.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
            s.skyboxMaterial = recipe(RenderSettings.skybox);

        s.ambientMode      = RenderSettings.ambientMode;
        s.ambientSky       = RenderSettings.ambientSkyColor;
        s.ambientEquator   = RenderSettings.ambientEquatorColor;
        s.ambientGround    = RenderSettings.ambientGroundColor;
        s.ambientIntensity = RenderSettings.ambientIntensity;

        foreach (var l in Object.FindObjectsByType<Light>())
        {
            if (l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
            Color color = l.color;
            float intensity = l.intensity, shadowStrength = l.shadowStrength;
            WeatherVisuals.ClearSky(l, ref color, ref intensity, ref shadowStrength);   // not the storm's dimming
            s.lights.Add(new MenuTreeShowcase.BakedLight
            {
                localRotation  = invRot * l.transform.rotation,
                color          = color,
                intensity      = intensity,
                shadows        = l.shadows,
                shadowStrength = shadowStrength,
            });
        }

        // Pivot on the tree itself (bark + foliage), not the pot and table below it.
        var bounds = new Bounds();
        bool any = false;
        void Grow(Renderer r)
        {
            if (r == null || !r.enabled) return;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        Grow(skeleton.GetComponent<MeshRenderer>());
        foreach (var leaf in root.GetComponentsInChildren<Leaf>())
            foreach (var r in leaf.GetComponentsInChildren<MeshRenderer>()) Grow(r);
        s.pivotLocal = root.InverseTransformPoint(any ? bounds.center : root.position);
    }
}
