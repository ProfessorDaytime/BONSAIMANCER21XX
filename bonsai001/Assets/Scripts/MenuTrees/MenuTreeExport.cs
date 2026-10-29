using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Exports a built menu tree as a Wavefront OBJ + MTL pair (2026-10-29, greenhouse "Export").
/// OBJ opens in Blender, most 3D tools and slicers for 3D printing.
///
/// <para>Each part becomes an object with one material. Materials carry the part's colour (base
/// colour times leaf tint), not its textures: a player build can't read most textures back from the
/// GPU. Coordinates are in the tree's own space, with the base of the tree at the origin; units are
/// the game's, so expect to scale it in the tool you open it with.</para>
/// </summary>
public static class MenuTreeExport
{
    static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int IdColor     = Shader.PropertyToID("_Color");

    public static string ExportRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bonsaimancer Exports");

    /// <summary>Writes <paramref name="tree"/> to a new folder and returns its path, or null.</summary>
    public static string ToObj(GameObject tree, string displayName)
    {
        if (tree == null) return null;
        string safe = string.IsNullOrWhiteSpace(displayName) ? "Bonsai" : displayName.Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        string dir = Path.Combine(ExportRoot, $"{safe} {DateTime.Now:yyyy-MM-dd HHmm}");

        try
        {
            Directory.CreateDirectory(dir);
            var ci  = CultureInfo.InvariantCulture;
            var obj = new StringBuilder();
            var mtl = new StringBuilder();
            obj.AppendLine("# Bonsaimancer tree export");
            obj.AppendLine("mtllib tree.mtl");

            var root   = tree.transform;
            var toRoot = Matrix4x4.TRS(root.position, root.rotation, Vector3.one).inverse;
            var mpb    = new MaterialPropertyBlock();
            int vBase = 1, tBase = 1, nBase = 1, part = 0, skipped = 0;

            foreach (var mf in tree.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var mr   = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null || !mr.enabled) continue;
                // A leaf or pot placed one by one is an unreadable model; use its readable copy.
                if (!mesh.isReadable) mesh = MenuTreeRegistry.ReadableTwin(MenuTreeRegistry.KeyOf(mesh));
                if (mesh == null) { skipped++; continue; }

                var m  = toRoot * mf.transform.localToWorldMatrix;
                var nm = m.inverse.transpose;
                var verts = mesh.vertices;
                var norms = mesh.normals;
                var uvs   = mesh.uv;

                Color tint = Color.white;
                if (mr.HasPropertyBlock())
                {
                    mr.GetPropertyBlock(mpb);
                    if (mpb.HasColor(IdBaseColor))  tint = mpb.GetColor(IdBaseColor);
                    else if (mpb.HasColor(IdColor)) tint = mpb.GetColor(IdColor);
                }

                obj.AppendLine($"o part_{part}");
                foreach (var v in verts)
                {
                    var p = m.MultiplyPoint3x4(v);
                    obj.AppendLine(string.Format(ci, "v {0:F5} {1:F5} {2:F5}", -p.x, p.y, p.z));   // OBJ is right-handed
                }
                bool hasN = norms != null && norms.Length == verts.Length;
                bool hasT = uvs   != null && uvs.Length   == verts.Length;
                if (hasN) foreach (var n in norms)
                {
                    var q = nm.MultiplyVector(n).normalized;
                    obj.AppendLine(string.Format(ci, "vn {0:F4} {1:F4} {2:F4}", -q.x, q.y, q.z));
                }
                if (hasT) foreach (var t in uvs) obj.AppendLine(string.Format(ci, "vt {0:F5} {1:F5}", t.x, t.y));

                var mats = mr.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    string mname = $"mat_{part}_{s}";
                    Color c = tint;
                    if (mat != null)
                    {
                        if (mat.HasProperty(IdBaseColor))  c *= mat.GetColor(IdBaseColor);
                        else if (mat.HasProperty(IdColor)) c *= mat.GetColor(IdColor);
                    }
                    mtl.AppendLine($"newmtl {mname}");
                    mtl.AppendLine(string.Format(ci, "Kd {0:F4} {1:F4} {2:F4}", c.r, c.g, c.b));
                    mtl.AppendLine(string.Format(ci, "d {0:F3}", c.a));
                    mtl.AppendLine();

                    obj.AppendLine($"usemtl {mname}");
                    var tris = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        // Mirroring X flips the winding, so write each triangle reversed.
                        obj.Append('f');
                        foreach (int k in new[] { tris[i], tris[i + 2], tris[i + 1] })
                        {
                            // v, vt and vn are numbered separately in OBJ, so each has its own base.
                            obj.Append(' ').Append(vBase + k);
                            if (hasT || hasN) obj.Append('/').Append(hasT ? (tBase + k).ToString() : "");
                            if (hasN) obj.Append('/').Append(nBase + k);
                        }
                        obj.AppendLine();
                    }
                }
                vBase += verts.Length;
                if (hasT) tBase += verts.Length;
                if (hasN) nBase += verts.Length;
                part++;
            }

            File.WriteAllText(Path.Combine(dir, "tree.obj"), obj.ToString());
            File.WriteAllText(Path.Combine(dir, "tree.mtl"), mtl.ToString());
            Debug.Log($"[MenuTree] Exported {part} parts to {dir}" + (skipped > 0 ? $" ({skipped} unreadable parts skipped)" : ""));
            return dir;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MenuTree] Export failed: {e}");
            return null;
        }
    }
}
