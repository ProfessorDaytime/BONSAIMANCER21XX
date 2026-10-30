using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bonsai ▸ Export Flower and Fruit Shapes (2026-10-30): writes every procedural bud, flower and fruit
/// shape (<see cref="FlowerVisuals"/>, <see cref="BudVisuals"/>) as a Wavefront OBJ with vertex colours,
/// plus a README of sizes, vertex counts and conventions, so replacement models can be made to match
/// in Blender. Output goes next to the greenhouse exports, in Documents/Bonsaimancer Exports.
/// </summary>
public static class FlowerShapeExport
{
    [MenuItem("Bonsai/Export Flower and Fruit Shapes (OBJ)")]
    static void Export()
    {
        var species = new Dictionary<string, TreeSpecies>();
        foreach (string guid in AssetDatabase.FindAssets("t:TreeSpecies"))
        {
            var sp = AssetDatabase.LoadAssetAtPath<TreeSpecies>(AssetDatabase.GUIDToAssetPath(guid));
            if (sp != null) species[sp.speciesName] = sp;
        }

        string dir = Path.Combine(MenuTreeExport.ExportRoot, "Flower and Fruit Shapes");
        Directory.CreateDirectory(dir);
        var shapes = FlowerVisuals.BuildReferenceShapes(name => species.TryGetValue(name, out var s) ? s : null);

        var ci     = CultureInfo.InvariantCulture;
        var readme = new StringBuilder(Header);
        string section = null;
        foreach (var s in shapes)
        {
            string kind = s.name.StartsWith("part_") ? "PARTS — model these" : "TWIGS — whole twigs at one stage, for reference";
            if (kind != section) { readme.AppendLine().AppendLine(kind).AppendLine(); section = kind; }
            WriteObj(Path.Combine(dir, s.name + ".obj"), s.name, s.mesh);
            Vector3 size = s.mesh.bounds.size;
            readme.AppendLine(string.Format(ci, "{0}.obj  ({1} verts, {2} tris, {3:F3} x {4:F3} x {5:F3})",
                                            s.name, s.mesh.vertexCount, s.mesh.triangles.Length / 3, size.x, size.y, size.z));
            readme.AppendLine("    " + s.note);
            readme.AppendLine();
            Object.DestroyImmediate(s.mesh);
        }
        File.WriteAllText(Path.Combine(dir, "README.txt"), readme.ToString());
        Debug.Log($"[FlowerShapes] Exported {shapes.Count} shapes to {dir}");
        EditorUtility.RevealInFinder(Path.Combine(dir, "README.txt"));
    }

    /// <summary>One mesh as an OBJ: vertex colours on the v lines, normals, triangles. Unity is
    /// left-handed and OBJ right-handed, so X is mirrored and each triangle written reversed.</summary>
    static void WriteObj(string path, string name, Mesh mesh)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Bonsaimancer flower and fruit shape: " + name);
        sb.AppendLine("o " + name);
        var  v    = mesh.vertices;
        var  n    = mesh.normals;
        var  c    = mesh.colors;
        bool hasC = c != null && c.Length == v.Length, hasN = n != null && n.Length == v.Length;
        for (int i = 0; i < v.Length; i++)
            sb.AppendLine(hasC
                ? string.Format(ci, "v {0:F6} {1:F6} {2:F6} {3:F4} {4:F4} {5:F4}", -v[i].x, v[i].y, v[i].z, c[i].r, c[i].g, c[i].b)
                : string.Format(ci, "v {0:F6} {1:F6} {2:F6}", -v[i].x, v[i].y, v[i].z));
        if (hasN) foreach (var q in n) sb.AppendLine(string.Format(ci, "vn {0:F4} {1:F4} {2:F4}", -q.x, q.y, q.z));
        var t = mesh.triangles;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            int a = t[i] + 1, b = t[i + 2] + 1, d = t[i + 1] + 1;
            sb.AppendLine(hasN ? $"f {a}//{a} {b}//{b} {d}//{d}" : $"f {a} {b} {d}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    const string Header =
@"BONSAIMANCER — FLOWER AND FRUIT SHAPES
Exported from the game's own code (FlowerVisuals, BudVisuals): one OBJ per shape, with vertex colours.

IMPORTING INTO BLENDER
  File > Import > Wavefront (.obj) with the default settings (Forward -Z, Up Y): Unity's up (+Y) becomes
  Blender's +Z, and Unity's +Z (the way the parts point) becomes Blender's -Y.
  The colours come in as a colour attribute: Viewport Shading > Color > Attribute shows them.
  Sizes are game units: 1 unit = 1 Blender metre at import scale 1. A cherry blossom is about 0.05 across,
  a maple flower about 0.04; the twig the twig_* files hang from is 0.15 long.

PARTS AND TWIGS
  part_*  The pieces the game places, repeats and animates: it opens petals and sepals, grows fruit,
          ripens colours and lets pieces fall. These are what to model. The origin is where the piece
          attaches. It points along Unity +Z, or hangs down -Y, as each note says.
  twig_*  Whole twigs posed at one stage (winter buds, in bloom, in fruit), built from the parts, so you
          can see how they're arranged. The twig's tip is at the origin, leaning up and out along +X.

KEEPING PARTS LIGHT
  The game draws every flower and fruit on a tree as one mesh, so counts multiply: a mature cherry
  carries ~2,000 blossoms, each five petals. Rough budgets per part:
    petal, sepal, cone scale      4-16 verts
    samara, floret, bud           10-30
    cherry, berry, fig            20-40
    pod, catkin                   30-60
    whole cone                    80-200 (cones are few)
  Colour: the game colours each part from the species' bloom, fruit and bud colours, so a white or
  neutral model is fine. Painted vertex colours or a small texture can work too; say which.

LEAVES
  One sheet, no thickness: the leaf material should draw both faces (today it draws only the front).
  Pivot at the end of the stem, where it meets the twig. Model it lying the same way as your Japanese
  maple leaf: the game treats the leaf's local +Z as the blade and local Y as its width, which it
  squeezes to 20% while the leaf unfolds. About 60-150 verts for a simple to lobed leaf; a compound
  wisteria leaf up to ~250.
";
}
