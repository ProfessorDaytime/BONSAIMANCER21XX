using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A player's tree as the main menu shows it: meshes, materials, camera pose and lighting, saved
/// when they quit a playthrough (2026-10-29). Written by <see cref="MenuTreeCapture"/>, shown by
/// <see cref="MenuTreeShowcase.SpawnFromSnapshot"/>, kept on disk by <see cref="MenuTreeLibrary"/>.
///
/// <para>Like the editor-baked prefab, this is static set dressing: no TreeSkeleton, no sim, so a
/// later change to the save format or the simulation can't break the menu. The file is a gzipped
/// binary; <see cref="FormatVersion"/> lets a newer game refuse or upgrade an older file.</para>
/// </summary>
public class MenuTreeSnapshot
{
    public const int FormatVersion = 1;
    const uint Magic = 0x4D545231;   // "MTR1"

    // ── Materials ─────────────────────────────────────────────────────────────

    /// <summary>A material rebuilt from its shader and property values. Most of the tree's
    /// materials are made at runtime, so they can't be saved as references.</summary>
    public class MaterialRecipe
    {
        public string   shader;
        public int      renderQueue;
        public string[] keywords = new string[0];
        public List<(string name, float value)>   floats   = new List<(string, float)>();
        public List<(string name, Color value)>   colors   = new List<(string, Color)>();
        public List<(string name, Vector4 value)> vectors  = new List<(string, Vector4)>();
        public List<(string name, string key, Vector2 scale, Vector2 offset)> textures
            = new List<(string, string, Vector2, Vector2)>();
    }

    // ── Geometry ──────────────────────────────────────────────────────────────

    /// <summary>Vertex data for a mesh made at runtime (bark, soil, combined leaves).</summary>
    public class MeshData
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Color[]   colors;
        public Vector4[][] uvs = new Vector4[4][];   // channels 0–3; null when absent
        public int[]     triangles;
    }

    /// <summary>One renderer in the menu tree. Either its own <see cref="mesh"/> data (one
    /// submesh), or a <see cref="meshKey"/> naming a project model (see
    /// <see cref="MenuTreeRegistry"/>) with one material per submesh.</summary>
    public class Part
    {
        public MeshData  mesh;
        public string    meshKey;
        public Matrix4x4 localMatrix = Matrix4x4.identity;   // relative to the tree root
        public int[]     materials = new int[0];             // indices into MenuTreeSnapshot.materials
        public bool      hasTint;
        public Color     tint;
        public ShadowCastingMode cast = ShadowCastingMode.On;
        public bool      receive = true;
    }

    public List<MaterialRecipe> materials = new List<MaterialRecipe>();
    public List<Part>           parts     = new List<Part>();

    // ── Presentation (same meaning as the MenuTreeShowcase fields) ────────────

    public Quaternion rootRotation = Quaternion.identity;
    public Vector3    rootScale    = Vector3.one;
    public Vector3    cameraLocalPosition;
    public Quaternion cameraLocalRotation = Quaternion.identity;
    public float      fieldOfView = 60f, nearClip = 0.05f, farClip = 500f;
    public CameraClearFlags clearFlags = CameraClearFlags.SolidColor;
    public Color      backgroundColor = Color.black;
    public int        skyboxMaterial = -1;   // index into materials, or -1
    public AmbientMode ambientMode = AmbientMode.Flat;
    public Color      ambientSky, ambientEquator, ambientGround;
    public float      ambientIntensity = 1f;
    public List<MenuTreeShowcase.BakedLight> lights = new List<MenuTreeShowcase.BakedLight>();
    public Vector3    pivotLocal;

    // ── File IO ───────────────────────────────────────────────────────────────

    public void Write(string path)
    {
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
        using (var w  = new BinaryWriter(gz))
        {
            w.Write(Magic);
            w.Write(FormatVersion);

            WriteQuat(w, rootRotation); WriteV3(w, rootScale);
            WriteV3(w, cameraLocalPosition); WriteQuat(w, cameraLocalRotation);
            w.Write(fieldOfView); w.Write(nearClip); w.Write(farClip);
            w.Write((int)clearFlags); WriteColor(w, backgroundColor);
            w.Write(skyboxMaterial);
            w.Write((int)ambientMode);
            WriteColor(w, ambientSky); WriteColor(w, ambientEquator); WriteColor(w, ambientGround);
            w.Write(ambientIntensity);
            w.Write(lights.Count);
            foreach (var l in lights)
            {
                WriteQuat(w, l.localRotation); WriteColor(w, l.color); w.Write(l.intensity);
                w.Write((int)l.shadows); w.Write(l.shadowStrength);
            }
            WriteV3(w, pivotLocal);

            w.Write(materials.Count);
            foreach (var m in materials) WriteMaterial(w, m);

            w.Write(parts.Count);
            foreach (var p in parts)
            {
                w.Write(p.mesh != null);
                if (p.mesh != null) WriteMesh(w, p.mesh);
                else                WriteStr(w, p.meshKey);
                for (int i = 0; i < 16; i++) w.Write(p.localMatrix[i]);
                w.Write(p.materials.Length);
                foreach (int mi in p.materials) w.Write(mi);
                w.Write(p.hasTint); WriteColor(w, p.tint);
                w.Write((int)p.cast); w.Write(p.receive);
            }
        }
        // Replace atomically, so a crash mid-write never leaves a half file behind.
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    /// <summary>Reads a snapshot, or returns null if the file is missing, damaged, or from a newer
    /// version of the game.</summary>
    public static MenuTreeSnapshot Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r  = new BinaryReader(gz))
            {
                if (r.ReadUInt32() != Magic) return null;
                int version = r.ReadInt32();
                if (version > FormatVersion)
                {
                    Debug.LogWarning($"[MenuTree] {path} is format {version}; this build reads up to {FormatVersion}.");
                    return null;
                }

                var s = new MenuTreeSnapshot();
                s.rootRotation = ReadQuat(r); s.rootScale = ReadV3(r);
                s.cameraLocalPosition = ReadV3(r); s.cameraLocalRotation = ReadQuat(r);
                s.fieldOfView = r.ReadSingle(); s.nearClip = r.ReadSingle(); s.farClip = r.ReadSingle();
                s.clearFlags = (CameraClearFlags)r.ReadInt32(); s.backgroundColor = ReadColor(r);
                s.skyboxMaterial = r.ReadInt32();
                s.ambientMode = (AmbientMode)r.ReadInt32();
                s.ambientSky = ReadColor(r); s.ambientEquator = ReadColor(r); s.ambientGround = ReadColor(r);
                s.ambientIntensity = r.ReadSingle();
                int lc = r.ReadInt32();
                for (int i = 0; i < lc; i++)
                    s.lights.Add(new MenuTreeShowcase.BakedLight
                    {
                        localRotation  = ReadQuat(r),
                        color          = ReadColor(r),
                        intensity      = r.ReadSingle(),
                        shadows        = (LightShadows)r.ReadInt32(),
                        shadowStrength = r.ReadSingle(),
                    });
                s.pivotLocal = ReadV3(r);

                int mc = r.ReadInt32();
                for (int i = 0; i < mc; i++) s.materials.Add(ReadMaterial(r));

                int pc = r.ReadInt32();
                for (int i = 0; i < pc; i++)
                {
                    var p = new Part();
                    if (r.ReadBoolean()) p.mesh = ReadMesh(r);
                    else                 p.meshKey = ReadStr(r);
                    var m = new Matrix4x4();
                    for (int k = 0; k < 16; k++) m[k] = r.ReadSingle();
                    p.localMatrix = m;
                    p.materials = new int[r.ReadInt32()];
                    for (int k = 0; k < p.materials.Length; k++) p.materials[k] = r.ReadInt32();
                    p.hasTint = r.ReadBoolean(); p.tint = ReadColor(r);
                    p.cast = (ShadowCastingMode)r.ReadInt32(); p.receive = r.ReadBoolean();
                    s.parts.Add(p);
                }
                return s;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MenuTree] Couldn't read {path}: {e.Message}");
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static void WriteMaterial(BinaryWriter w, MaterialRecipe m)
    {
        WriteStr(w, m.shader);
        w.Write(m.renderQueue);
        w.Write(m.keywords.Length);
        foreach (var k in m.keywords) WriteStr(w, k);
        w.Write(m.floats.Count);
        foreach (var (n, v) in m.floats) { WriteStr(w, n); w.Write(v); }
        w.Write(m.colors.Count);
        foreach (var (n, v) in m.colors) { WriteStr(w, n); WriteColor(w, v); }
        w.Write(m.vectors.Count);
        foreach (var (n, v) in m.vectors) { WriteStr(w, n); WriteV4(w, v); }
        w.Write(m.textures.Count);
        foreach (var (n, k, sc, of) in m.textures)
        {
            WriteStr(w, n); WriteStr(w, k);
            w.Write(sc.x); w.Write(sc.y); w.Write(of.x); w.Write(of.y);
        }
    }

    static MaterialRecipe ReadMaterial(BinaryReader r)
    {
        var m = new MaterialRecipe { shader = ReadStr(r), renderQueue = r.ReadInt32() };
        m.keywords = new string[r.ReadInt32()];
        for (int i = 0; i < m.keywords.Length; i++) m.keywords[i] = ReadStr(r);
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) m.floats.Add((ReadStr(r), r.ReadSingle()));
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) m.colors.Add((ReadStr(r), ReadColor(r)));
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) m.vectors.Add((ReadStr(r), ReadV4(r)));
        n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            string name = ReadStr(r), key = ReadStr(r);
            var sc = new Vector2(r.ReadSingle(), r.ReadSingle());
            var of = new Vector2(r.ReadSingle(), r.ReadSingle());
            m.textures.Add((name, key, sc, of));
        }
        return m;
    }

    static void WriteMesh(BinaryWriter w, MeshData d)
    {
        w.Write(d.vertices.Length);
        foreach (var v in d.vertices) WriteV3(w, v);
        WriteOptional(w, d.normals,  v => WriteV3(w, v));
        WriteOptional(w, d.tangents, v => WriteV4(w, v));
        WriteOptional(w, d.colors,   c => WriteColor(w, c));
        for (int c = 0; c < 4; c++) WriteOptional(w, d.uvs[c], v => WriteV4(w, v));
        w.Write(d.triangles.Length);
        foreach (int t in d.triangles) w.Write(t);
    }

    static MeshData ReadMesh(BinaryReader r)
    {
        var d = new MeshData();
        d.vertices = new Vector3[r.ReadInt32()];
        for (int i = 0; i < d.vertices.Length; i++) d.vertices[i] = ReadV3(r);
        d.normals  = ReadOptional(r, () => ReadV3(r));
        d.tangents = ReadOptional(r, () => ReadV4(r));
        d.colors   = ReadOptional(r, () => ReadColor(r));
        for (int c = 0; c < 4; c++) d.uvs[c] = ReadOptional(r, () => ReadV4(r));
        d.triangles = new int[r.ReadInt32()];
        for (int i = 0; i < d.triangles.Length; i++) d.triangles[i] = r.ReadInt32();
        return d;
    }

    static void WriteOptional<T>(BinaryWriter w, T[] a, System.Action<T> each)
    {
        w.Write(a != null ? a.Length : -1);
        if (a != null) foreach (var x in a) each(x);
    }

    static T[] ReadOptional<T>(BinaryReader r, System.Func<T> each)
    {
        int n = r.ReadInt32();
        if (n < 0) return null;
        var a = new T[n];
        for (int i = 0; i < n; i++) a[i] = each();
        return a;
    }

    static void WriteStr(BinaryWriter w, string s) => w.Write(s ?? "");
    static string ReadStr(BinaryReader r) => r.ReadString();
    static void WriteV3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static Vector3 ReadV3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static void WriteV4(BinaryWriter w, Vector4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
    static Vector4 ReadV4(BinaryReader r) => new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static void WriteQuat(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    static Quaternion ReadQuat(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static void WriteColor(BinaryWriter w, Color c) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
    static Color ReadColor(BinaryReader r) => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
