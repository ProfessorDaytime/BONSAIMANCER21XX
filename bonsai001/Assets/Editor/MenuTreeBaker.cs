using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Bakes the live tree into a static prefab for the main menu (PLAN "Release Prep" R3).
///
/// <para>Run during Play mode, from <b>Bonsai ▸ Bake Menu Tree</b> or the <b>Bake as Menu Tree</b>
/// button in Settings ▸ Debug. Everything visible on the tree's root — bark, leaves, flowers, pot,
/// soil, table — plus an active rock and the equipped decoration is combined into a handful of
/// meshes, one per material, and saved with a <see cref="MenuTreeShowcase"/> prefab under
/// <c>Assets/Resources/MenuTree/</c>. Assets written by AssetDatabase survive leaving Play mode.
/// Re-baking replaces the previous bake.</para>
///
/// <para>Details that matter:</para>
/// <list type="bullet">
/// <item>Meshes are combined in the tree root's <b>local</b> space. The bark shader's wind flex reads
/// object-space position, so re-origining the mesh would change how it bends.</item>
/// <item>Leaves are snapped to their windless rest pose first. Bark sways on the GPU (not in the
/// mesh) while leaves sway on the CPU (their transforms), so a mid-gust bake would leave every
/// leaf off its twig.</item>
/// <item>Leaf colour is per-leaf, set through a MaterialPropertyBlock, which a combined mesh can't
/// carry. Each material's leaf colours are reduced to at most <see cref="MaxColoursPerMaterial"/>
/// representative colours (k-means), each becoming its own material copy.</item>
/// <item>Wires (LineRenderers) aren't baked — a displayed bonsai is normally unwired, and a
/// camera-facing line baked from one angle looks wrong from others.</item>
/// </list>
/// </summary>
public static class MenuTreeBaker
{
    const string OUT_DIR     = "Assets/Resources/MenuTree";
    const string MESH_DIR    = OUT_DIR + "/Meshes";
    const string MAT_DIR     = OUT_DIR + "/Materials";
    const string PREFAB_PATH = OUT_DIR + "/MenuTree.prefab";
    const string TEMP_DIR    = "Assets/_MenuTreeBakeTemp";

    const int MaxColoursPerMaterial = 16;

    // Helper objects that live under the tree root but aren't the tree.
    static readonly HashSet<string> ExcludedNames = new HashSet<string>
    {
        "_TreeHighlight", "_WireAimPreview", "_SawGroove",
    };

    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor     = Shader.PropertyToID("_Color");

    [InitializeOnLoadMethod]
    static void Hook()
    {
        MenuTreeShowcase.BakeRequested -= Bake;
        MenuTreeShowcase.BakeRequested += Bake;
    }

    [MenuItem("Bonsai/Bake Menu Tree", priority = 110)]
    static void BakeFromMenu() => Bake();

    [MenuItem("Bonsai/Bake Menu Tree", true)]
    static bool BakeValidate() => Application.isPlaying;

    [MenuItem("Bonsai/Clear Menu Tree", priority = 111)]
    static void ClearFromMenu()
    {
        if (!AssetDatabase.IsValidFolder(OUT_DIR)) return;
        if (!EditorUtility.DisplayDialog("Clear menu tree?",
                $"Deletes {OUT_DIR}. The main menu goes back to having no tree.", "Delete", "Cancel"))
            return;
        AssetDatabase.DeleteAsset(OUT_DIR);
    }

    // ── One combine source: a submesh of a renderer, with the colour its property block gave it ──
    class Piece
    {
        public CombineInstance  ci;
        public Material         mat;
        public Color?           tint;
        public int              cluster = -1;
        public ShadowCastingMode cast;
        public bool             receive;
    }

    public static void Bake()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Bake Menu Tree", "Enter Play mode with the tree you want first.", "OK");
            return;
        }

        var skeleton = Object.FindAnyObjectByType<TreeSkeleton>();
        if (skeleton == null)
        {
            EditorUtility.DisplayDialog("Bake Menu Tree", "No tree in the scene.", "OK");
            return;
        }
        var root = skeleton.transform;

        foreach (var leaf in root.GetComponentsInChildren<Leaf>()) leaf.SnapToRest();

        var renderers = CollectRenderers(root);
        List<Piece> pieces;
        int unreadable;
        try     { pieces = BuildPieces(root, renderers, out unreadable); }
        finally { DeleteTempModels(); }
        if (pieces.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Menu Tree", "Nothing visible to bake.", "OK");
            return;
        }

        int colourGroups = ClusterTints(pieces);

        // Group by (material, colour cluster).
        var groups = new Dictionary<(Material, int), List<Piece>>();
        foreach (var p in pieces)
        {
            var key = (p.mat, p.cluster);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Piece>();
            list.Add(p);
        }

        // Fresh output folders; a re-bake replaces the old one wholesale.
        if (AssetDatabase.IsValidFolder(MESH_DIR)) AssetDatabase.DeleteAsset(MESH_DIR);
        if (AssetDatabase.IsValidFolder(MAT_DIR))  AssetDatabase.DeleteAsset(MAT_DIR);
        Directory.CreateDirectory(MESH_DIR);
        Directory.CreateDirectory(MAT_DIR);
        AssetDatabase.Refresh();

        var prefabRoot = new GameObject("MenuTree");
        // Same rotation and scale as the live root, so everything captured relative to it
        // (camera, lights, pivot) maps back exactly.
        prefabRoot.transform.SetPositionAndRotation(Vector3.zero, root.rotation);
        prefabRoot.transform.localScale = root.lossyScale;

        int totalVerts = 0, index = 0;
        var savedMats = new Dictionary<Material, Material>();   // runtime material → saved copy
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var kv in groups)
            {
                var list = kv.Value;
                var first = list[0];

                var cis = new CombineInstance[list.Count];
                for (int i = 0; i < list.Count; i++) cis[i] = list[i].ci;

                var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = $"MenuTree_{index:00}_{Safe(first.mat.name)}" };
                mesh.CombineMeshes(cis, mergeSubMeshes: true, useMatrices: true);
                mesh.RecalculateBounds();
                totalVerts += mesh.vertexCount;
                AssetDatabase.CreateAsset(mesh, $"{MESH_DIR}/{mesh.name}.asset");

                var mat = ResolveMaterial(first, list, index, savedMats);

                var part = new GameObject(mesh.name);
                part.transform.SetParent(prefabRoot.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = part.AddComponent<MeshRenderer>();
                mr.sharedMaterial    = mat;
                mr.shadowCastingMode = first.cast;
                mr.receiveShadows    = first.receive;
                index++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        var showcase = prefabRoot.AddComponent<MenuTreeShowcase>();
        Capture(showcase, root, skeleton);

        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
        Object.DestroyImmediate(prefabRoot);
        AssetDatabase.SaveAssets();

        string msg = $"Baked {renderers.Count} renderers into {index} meshes ({totalVerts:N0} verts, " +
                     $"{colourGroups} leaf colour groups).\n\n{PREFAB_PATH}\n\nThe main menu shows it next launch.";
        if (unreadable > 0)
            msg += $"\n\n{unreadable} renderer(s) were skipped: their mesh is a runtime copy of a model " +
                   "without Read/Write, so its shape can't be read. See the Console.";
        Debug.Log("[MenuTreeBaker] " + msg.Replace("\n\n", " "));
        EditorUtility.DisplayDialog("Menu tree baked", msg, "OK");
    }

    // ── Collection ────────────────────────────────────────────────────────────

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

        // The pot, its soil surface and the table are separate scene objects, NOT children of the
        // tree root (the tree's components sit on the Platform001 prefab root, which holds only the
        // tree). Find each one explicitly.
        var pot = root.GetComponent<PotSoil>();
        if (pot != null && pot.potVisual != null) AddUnder(pot.potVisual);
        foreach (var name in new[] { "_PotSoilSurface", "PlanterTable", "TinyTable01" })
        {
            var go = GameObject.Find(name);   // active objects only
            if (go != null) AddUnder(go.transform);
        }

        // Things that sit with the tree but aren't parented to it: an Ishitsuki rock and the
        // equipped decoration (DecorationManager names it "Decoration_<item>").
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

    static List<Piece> BuildPieces(Transform root, List<MeshRenderer> renderers, out int unreadable)
    {
        var pieces   = new List<Piece>();
        var mpb      = new MaterialPropertyBlock();
        var toRoot   = root.worldToLocalMatrix;
        var readable = new Dictionary<Mesh, Mesh>();
        unreadable   = 0;

        foreach (var r in renderers)
        {
            var mesh = Readable(r.GetComponent<MeshFilter>().sharedMesh, readable);
            if (mesh == null)
            {
                unreadable++;
                Debug.LogWarning($"[MenuTreeBaker] Skipped '{r.name}': its mesh can't be read.", r);
                continue;
            }
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
        }
        return pieces;
    }

    // ── Unreadable models ─────────────────────────────────────────────────────

    /// <summary>
    /// A CPU-readable version of <paramref name="mesh"/>. CombineMeshes needs to read vertex data,
    /// and most of the project's models (leaves, pots, tables) are imported without Read/Write.
    /// Turning Read/Write on would change the game, not just the bake — Leaf only gives each leaf
    /// its own twisted copy when the model is readable — so instead the model file is copied to a
    /// temp folder, the COPY is imported readable, and the mesh is cloned out of it. The temp folder
    /// is deleted straight after. Returns null for a runtime copy of an unreadable mesh, which has
    /// no model file to re-import.
    /// </summary>
    static Mesh Readable(Mesh mesh, Dictionary<Mesh, Mesh> cache)
    {
        if (mesh.isReadable) return mesh;
        if (cache.TryGetValue(mesh, out var done)) return done;

        Mesh result = null;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
        {
            if (!AssetDatabase.IsValidFolder(TEMP_DIR))
                AssetDatabase.CreateFolder("Assets", TEMP_DIR.Substring("Assets/".Length));
            string tmp = $"{TEMP_DIR}/{Path.GetFileName(path)}";
            if (AssetDatabase.LoadMainAssetAtPath(tmp) == null)
            {
                AssetDatabase.CopyAsset(path, tmp);
                var imp = (ModelImporter)AssetImporter.GetAtPath(tmp);
                imp.isReadable = true;
                imp.SaveAndReimport();
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(tmp))
                if (o is Mesh m && m.name == mesh.name)
                {
                    result = Object.Instantiate(m);   // in-memory clone; independent of the temp file
                    result.name = mesh.name;
                    break;
                }
        }
        cache[mesh] = result;
        return result;
    }

    static void DeleteTempModels()
    {
        if (AssetDatabase.IsValidFolder(TEMP_DIR)) AssetDatabase.DeleteAsset(TEMP_DIR);
    }

    // ── Leaf colours ──────────────────────────────────────────────────────────

    /// <summary>
    /// Per material, k-means the property-block colours down to at most
    /// <see cref="MaxColoursPerMaterial"/> and stamps each tinted piece with its cluster.
    /// Returns the number of colour groups made.
    /// </summary>
    static int ClusterTints(List<Piece> pieces)
    {
        int groups = 0;
        var byMat  = new Dictionary<Material, List<Piece>>();
        foreach (var p in pieces)
        {
            if (p.tint == null) continue;
            if (!byMat.TryGetValue(p.mat, out var l)) byMat[p.mat] = l = new List<Piece>();
            l.Add(p);
        }

        foreach (var list in byMat.Values)
        {
            // Seed from colours spread across the brightness range, so dark and light leaves
            // both get a representative.
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

            // Store the centre on each piece so ResolveMaterial can use it.
            foreach (var p in list) p.tint = centres[p.cluster];
            groups += k;
        }
        return groups;
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

    // ── Materials ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A material that can live in an asset. Project materials are referenced as they are. A
    /// material created at runtime (most of the tree's are) is saved as a copy, and a leaf colour
    /// group gets a copy with its colour applied.
    /// </summary>
    static Material ResolveMaterial(Piece first, List<Piece> group, int index, Dictionary<Material, Material> saved)
    {
        var src = first.mat;
        bool isAsset = AssetDatabase.Contains(src);

        if (first.tint == null)
        {
            if (isAsset) return src;
            if (saved.TryGetValue(src, out var done)) return done;
        }

        var copy = new Material(src) { name = $"MenuTree_{index:00}_{Safe(src.name)}" };
        if (first.tint is Color c)
        {
            if (copy.HasProperty(IdBaseColor)) copy.SetColor(IdBaseColor, c);
            if (copy.HasProperty(IdColor))     copy.SetColor(IdColor, c);
        }
        WarnAboutRuntimeTextures(copy);
        AssetDatabase.CreateAsset(copy, $"{MAT_DIR}/{copy.name}.mat");
        if (first.tint == null) saved[src] = copy;
        return copy;
    }

    static void WarnAboutRuntimeTextures(Material m)
    {
        foreach (var name in m.GetTexturePropertyNames())
        {
            var tex = m.GetTexture(name);
            if (tex != null && !AssetDatabase.Contains(tex))
                Debug.LogWarning($"[MenuTreeBaker] '{m.name}' uses a runtime texture on {name}; it won't survive the bake.");
        }
    }

    // ── Camera, lights, ambient ───────────────────────────────────────────────

    static void Capture(MenuTreeShowcase s, Transform root, TreeSkeleton skeleton)
    {
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

        if (RenderSettings.skybox != null && AssetDatabase.Contains(RenderSettings.skybox))
            s.skybox = RenderSettings.skybox;
        s.ambientMode      = RenderSettings.ambientMode;
        s.ambientSky       = RenderSettings.ambientSkyColor;
        s.ambientEquator   = RenderSettings.ambientEquatorColor;
        s.ambientGround    = RenderSettings.ambientGroundColor;
        s.ambientIntensity = RenderSettings.ambientIntensity;

        var lights = new List<MenuTreeShowcase.BakedLight>();
        foreach (var l in Object.FindObjectsByType<Light>())
        {
            if (l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
            lights.Add(new MenuTreeShowcase.BakedLight
            {
                localRotation  = invRot * l.transform.rotation,
                color          = l.color,
                intensity      = l.intensity,
                shadows        = l.shadows,
                shadowStrength = l.shadowStrength,
            });
        }
        s.lights = lights.ToArray();

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

    static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(" (Instance)", "").Replace(' ', '_');
    }
}
