using UnityEngine;

// Geometry for FlowerVisuals. Closed shapes are wound to face out (Unity's front face: clockwise
// seen from outside, cross(v1-v0, v2-v0) outward) so the two-sided material lights each side from
// the side you're looking at; open sheets take their normal from their own first triangle.
public partial class FlowerVisuals
{
    // Wing outline as (along, across): a straight, veined outer edge and a rounded trailing edge.
    static readonly Vector2[] WingOutline =
    {
        new Vector2(0f,    0.30f), new Vector2(0.5f,  0.52f), new Vector2(0.9f, 0.40f),
        new Vector2(1f,    0.05f), new Vector2(0.86f, -0.38f), new Vector2(0.45f, -0.45f),
        new Vector2(0f,   -0.12f),
    };

    // Petal outline as (along, across) from its base: rounded, widest past halfway, notched.
    static readonly Vector2[] PetalOutline =
    {
        new Vector2(0.30f, -0.30f), new Vector2(0.72f, -0.48f), new Vector2(0.98f, -0.26f),
        new Vector2(0.88f,  0f),
        new Vector2(0.98f,  0.26f), new Vector2(0.72f,  0.48f), new Vector2(0.30f,  0.30f),
    };

    // Lathe profiles: ring k at fraction T[k] along the shape with relative radius R[k]. Ends of
    // zero radius close the shape.
    static readonly float[] PearT   = { 0f, 0f,    0.18f, 0.45f, 0.72f, 0.9f, 1f,   1f };
    static readonly float[] PearR   = { 0f, 0.25f, 0.6f,  0.92f, 1f,    0.8f, 0.3f, 0f };
    static readonly float[] PodT    = { 0f, 0.06f, 0.2f,  0.38f, 0.56f, 0.74f, 0.9f, 1f };
    static readonly float[] PodR    = { 0f, 0.6f,  0.95f, 0.82f, 0.95f, 0.82f, 0.6f, 0f };
    static readonly float[] CatkinT = { 0f, 0f,    0.08f, 0.2f, 0.35f, 0.5f, 0.65f, 0.8f,  0.92f, 1f };
    static readonly float[] CatkinR = { 0f, 0.35f, 0.75f, 1f,   1f,    1f,   0.95f, 0.85f, 0.55f, 0f };
    static readonly float[] OvoidT  = { 0f, 0f,   0.1f,  0.3f, 0.5f,  0.7f,  0.88f, 1f };
    static readonly float[] OvoidR  = { 0f, 0.3f, 0.75f, 1f,   0.95f, 0.75f, 0.45f, 0f };
    static readonly float[] HangT   = { 0f, 0f,    0.08f, 0.2f, 0.5f, 0.8f,  0.93f, 1f };
    static readonly float[] HangR   = { 0f, 0.35f, 0.75f, 0.9f, 0.9f, 0.85f, 0.5f,  0f };
    static readonly float[] UpT     = { 0f, 0f,   0.1f, 0.3f, 0.6f, 0.85f, 0.97f, 1f };
    static readonly float[] UpR     = { 0f, 0.6f, 0.9f, 1f,   1f,   0.9f,  0.6f,  0f };
    static readonly float[] RoundT  = { 0f, 0f,   0.12f, 0.35f, 0.65f, 0.88f, 1f };
    static readonly float[] RoundR  = { 0f, 0.3f, 0.8f,  1f,    1f,    0.8f,  0f };

    /// <summary>A cone's length and width for a species at <paramref name="size"/> (fruitSize ×
    /// growth): long and narrow hanging, barrel-shaped upright, as long as wide round.</summary>
    static void ConeDims(TreeSpecies sp, float size, out float len, out float wid)
    {
        float w = sp.coneShape switch { ConeShape.Hanging => 0.55f, ConeShape.Upright => 0.9f, _ => 1f };
        len = ConeLength * size * (sp.coneShape == ConeShape.Round ? 0.55f : 1f);
        wid = ConeWidth * size * w;
    }

    /// <summary>A thin three-sided stalk through the first <paramref name="count"/> points, tapering
    /// from r0 to r1, its sway easing from fx0 to fx1. Ring frames are carried along the curve so
    /// the stalk never twists.</summary>
    static void AddTube(Geo g, Vector3[] pts, int count, float r0, float r1, Color col, Vector2 fx0, Vector2 fx1)
    {
        const int Sides = 3;
        int     start = g.verts.Count;
        Vector3 u     = Vector3.zero;
        for (int k = 0; k < count; k++)
        {
            Vector3 d = (k < count - 1 ? pts[k + 1] - pts[k] : pts[k] - pts[k - 1]).normalized;
            if (k == 0)
            {
                u = Vector3.Cross(d, Vector3.up);
                if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(d, Vector3.right);
            }
            else u -= d * Vector3.Dot(u, d);
            u.Normalize();
            Vector3 v = Vector3.Cross(d, u);
            float   t = k / (float)(count - 1);
            float   r = Mathf.Lerp(r0, r1, t);
            Vector2 f = Vector2.Lerp(fx0, fx1, t);
            for (int i = 0; i < Sides; i++)
            {
                float   ang    = i * Mathf.PI * 2f / Sides;
                Vector3 radial = u * Mathf.Cos(ang) + v * Mathf.Sin(ang);
                g.Vert(pts[k] + radial * r, radial, col, f);
            }
        }
        for (int k = 0; k < count - 1; k++)
            for (int i = 0; i < Sides; i++)
            {
                int a0 = start + k * Sides + i, a1 = start + k * Sides + (i + 1) % Sides;
                int b0 = a0 + Sides, b1 = a1 + Sides;
                g.tris.Add(a0); g.tris.Add(a1); g.tris.Add(b0);
                g.tris.Add(a1); g.tris.Add(b1); g.tris.Add(b0);
            }
    }

    /// <summary>A surface of revolution along <paramref name="dir"/> (a fig, a pod, a catkin, a
    /// cone's core): ring k at t[k] of <paramref name="len"/> with radius r[k] × wid / 2, squashed
    /// across by <paramref name="flatten"/>. Rings alternate col0 / col1, which reads as scales on a
    /// catkin; sway eases from fx0 to fx1 along it.</summary>
    static void AddLathe(Geo g, Vector3 root, Vector3 dir, float len, float wid, float[] t, float[] r,
                         int sides, Color col0, Color col1, Vector2 fx0, Vector2 fx1, float flatten = 1f)
    {
        if (len < 0.0005f) return;
        Vector3 u = Vector3.Cross(dir, Vector3.up);
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(dir, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(dir, u);
        int start = g.verts.Count;
        for (int k = 0; k < t.Length; k++)
        {
            Vector3 c   = root + dir * (t[k] * len);
            float   rad = r[k] * wid * 0.5f;
            Color   col = (k & 1) == 0 ? col0 : col1;
            Vector2 f   = Vector2.Lerp(fx0, fx1, t[k]);
            for (int i = 0; i < sides; i++)
            {
                float   ang    = i * Mathf.PI * 2f / sides;
                Vector3 radial = u * Mathf.Cos(ang) + v * (Mathf.Sin(ang) * flatten);
                g.Vert(c + radial * rad, radial.normalized, col, f);
            }
        }
        for (int k = 0; k < t.Length - 1; k++)
            for (int i = 0; i < sides; i++)
            {
                int a0 = start + k * sides + i, a1 = start + k * sides + (i + 1) % sides;
                int b0 = a0 + sides, b1 = a1 + sides;
                g.tris.Add(a0); g.tris.Add(a1); g.tris.Add(b0);
                g.tris.Add(a1); g.tris.Add(b1); g.tris.Add(b0);
            }
    }

    /// <summary>A flat kite from <paramref name="root"/> along <paramref name="dir"/>, widest 45% of
    /// the way: a sepal, or a cone scale.</summary>
    static void AddKite(Geo g, Vector3 root, Vector3 dir, Vector3 tan, float len, float wid, Color c0, Color c1, Vector2 fx)
    {
        Vector3 v1  = root + dir * (0.45f * len) - tan * (0.5f * wid);
        Vector3 v2  = root + dir * len;
        Vector3 v3  = root + dir * (0.45f * len) + tan * (0.5f * wid);
        Vector3 nrm = Vector3.Cross(v1 - root, v2 - root).normalized;   // matches the winding below
        int     b   = g.verts.Count;
        g.Vert(root, nrm, c0, fx);
        g.Vert(v1, nrm, c1, fx);
        g.Vert(v2, nrm, c1, fx);
        g.Vert(v3, nrm, c1, fx);
        g.tris.Add(b); g.tris.Add(b + 1); g.tris.Add(b + 2);
        g.tris.Add(b); g.tris.Add(b + 2); g.tris.Add(b + 3);
    }

    /// <summary>A small flower facing along <paramref name="dir"/>: five sepals, closed into a
    /// pointed bud at theta ≈ 12° and spread into a star at 75°, pale at the heart.</summary>
    static void AddFlower(Geo g, Vector3 at, Vector3 dir, float theta, float len, float wid,
                          Color heart, Color sepal, Vector2 fx, float az)
    {
        if (len < 0.0004f) return;
        float c = Mathf.Cos(theta * Mathf.Deg2Rad), s = Mathf.Sin(theta * Mathf.Deg2Rad);
        for (int k = 0; k < 5; k++)
        {
            Vector3 radial = BudVisuals.Side(dir, az + 72f * k);
            AddKite(g, at, dir * c + radial * s, Vector3.Cross(dir, radial).normalized, len, wid, heart, sepal, fx);
        }
    }

    /// <summary>One petal on a flower facing along <paramref name="face"/>: turned
    /// <paramref name="az"/> round it and opened <paramref name="theta"/> away from it.</summary>
    static void AddPetal(Geo g, Vector3 at, Vector3 face, float az, float theta, float cup, float len, float wid,
                         Color baseCol, Color edgeCol, Vector2 fx)
    {
        if (len < 0.0006f) return;
        Vector3 radial = BudVisuals.Side(face, az);
        float   c = Mathf.Cos(theta * Mathf.Deg2Rad), s = Mathf.Sin(theta * Mathf.Deg2Rad);
        AddPetalBasis(g, at, face * c + radial * s, Vector3.Cross(face, radial).normalized, face,
                      cup, len, wid, baseCol, edgeCol, fx);
    }

    /// <summary>A notched petal from <paramref name="at"/> along <paramref name="along"/>, curling
    /// toward <paramref name="up"/> by <paramref name="cup"/>: a fan from its base.</summary>
    static void AddPetalBasis(Geo g, Vector3 at, Vector3 along, Vector3 across, Vector3 up, float cup,
                              float len, float wid, Color baseCol, Color edgeCol, Vector2 fx)
    {
        if (len < 0.0006f) return;
        Vector3 P(Vector2 q) => at + along * (q.x * len) + across * (q.y * wid) + up * (cup * q.x * q.x * len);
        Vector3 nrm   = Vector3.Cross(P(PetalOutline[0]) - at, P(PetalOutline[1]) - at).normalized;
        Color   notch = Color.Lerp(edgeCol, Color.white, 0.3f);
        int     b     = g.verts.Count;
        g.Vert(at, nrm, baseCol, fx);
        for (int k = 0; k < PetalOutline.Length; k++)
            g.Vert(P(PetalOutline[k]), nrm, k == 3 ? notch : edgeCol, fx);
        for (int k = 0; k < PetalOutline.Length - 1; k++) { g.tris.Add(b); g.tris.Add(b + 1 + k); g.tris.Add(b + 2 + k); }
    }

    /// <summary>A flat five-pointed tuft facing <paramref name="face"/> (a blossom's stamens).</summary>
    static void AddStar(Geo g, Vector3 at, Vector3 face, float radius, Color col, Vector2 fx, float az)
    {
        if (radius < 0.0005f) return;
        Vector3 centre = at + face * (radius * 0.4f);
        Vector3 p0     = at + BudVisuals.Side(face, az + 36f) * radius;
        Vector3 p1     = at + BudVisuals.Side(face, az + 108f) * radius;
        Vector3 nrm    = Vector3.Cross(p0 - centre, p1 - centre).normalized;
        int     b      = g.verts.Count;
        g.Vert(centre, nrm, Color.Lerp(col, Color.white, 0.2f), fx);
        for (int k = 0; k < 5; k++) g.Vert(at + BudVisuals.Side(face, az + 72f * k + 36f) * radius, nrm, col, fx);
        for (int k = 0; k < 5; k++) { g.tris.Add(b); g.tris.Add(b + 1 + k); g.tris.Add(b + 1 + (k + 1) % 5); }
    }

    /// <summary>A berry or cherry hanging from <paramref name="top"/> along <paramref name="down"/>:
    /// a little sphere dimpled where the stalk goes in, pale on top.</summary>
    static void AddBerry(Geo g, Vector3 top, Vector3 down, float r, Color col, Vector2 fx) =>
        AddSphere(g, top + down * (r * 0.85f), down, r, col, fx, true);

    /// <summary>A round puff centred on <paramref name="centre"/> (seed fluff).</summary>
    static void AddBall(Geo g, Vector3 centre, float r, Color col, Vector2 fx) =>
        AddSphere(g, centre, Vector3.down, r, col, fx, false);

    static void AddSphere(Geo g, Vector3 c, Vector3 down, float r, Color col, Vector2 fx, bool dimple)
    {
        if (r < 0.0008f) return;
        const int Around = 6;
        Vector3 u = BudVisuals.Side(down, 0f), v = Vector3.Cross(down, u);
        int start = g.verts.Count;
        g.Vert(c - down * (r * (dimple ? 0.7f : 1f)), -down, dimple ? col * 0.85f : col, fx);
        for (int ring = 0; ring < 3; ring++)
        {
            float a = (50f + 45f * ring) * Mathf.Deg2Rad;   // from the top down
            for (int i = 0; i < Around; i++)
            {
                float   b = (i + (ring % 2) * 0.5f) * Mathf.PI * 2f / Around;
                Vector3 n = -down * Mathf.Cos(a) + (u * Mathf.Cos(b) + v * Mathf.Sin(b)) * Mathf.Sin(a);
                g.Vert(c + n * r, n, ring == 0 ? Color.Lerp(col, Color.white, 0.15f) : col, fx);
            }
        }
        g.Vert(c + down * r, down, col * 0.9f, fx);
        int top0 = start, ring0 = start + 1, bottom = start + 1 + 3 * Around;
        for (int i = 0; i < Around; i++)
        {
            int i1 = (i + 1) % Around;
            g.TriOut(top0, ring0 + i, ring0 + i1, c);
            for (int ring = 0; ring < 2; ring++)
            {
                int a0 = ring0 + ring * Around, b0 = a0 + Around;
                g.TriOut(a0 + i, b0 + i, a0 + i1, c);
                g.TriOut(a0 + i1, b0 + i, b0 + i1, c);
            }
            g.TriOut(bottom, ring0 + 2 * Around + i1, ring0 + 2 * Around + i, c);
        }
    }

    /// <summary>A small closed spindle along <paramref name="dir"/>: a bud, a nutlet, a keel.</summary>
    static void AddSpindle(Geo g, Vector3 root, Vector3 dir, float len, float wid, Color col, Vector2 fx)
    {
        const int Sides = 5;
        Vector3 u = Vector3.Cross(dir, Vector3.up);
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(dir, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(dir, u);

        int start = g.verts.Count;
        g.Vert(root, -dir, col, fx);
        for (int i = 0; i < Sides; i++)
        {
            float   ang    = i * Mathf.PI * 2f / Sides;
            Vector3 radial = u * Mathf.Cos(ang) + v * Mathf.Sin(ang);
            g.Vert(root + dir * (len * 0.45f) + radial * (wid * 0.5f), radial, col, fx);
        }
        g.Vert(root + dir * len, dir, Color.Lerp(col, Color.white, 0.1f), fx);

        int tip = start + Sides + 1;
        for (int i = 0; i < Sides; i++)
        {
            int r0 = start + 1 + i, r1 = start + 1 + (i + 1) % Sides;
            g.tris.Add(start); g.tris.Add(r1); g.tris.Add(r0);
            g.tris.Add(r0);    g.tris.Add(r1); g.tris.Add(tip);
        }
    }

    /// <summary>A spur's rosette of plump winter flower buds (one bud stands straight out).</summary>
    static void AddSpurBuds(Geo g, TreeNode n, int k, Vector3 pos, Vector3 dir, float size, int count, Color col, Vector2 fx)
    {
        if (size < 0.02f) return;
        float az0 = Hash01(n.id, 2, 60 + k) * 360f;
        for (int b = 0; b < count; b++)
        {
            Vector3 side = BudVisuals.Side(dir, az0 + b * 360f / count);
            Vector3 bdir = count == 1 ? dir : (dir + side * 0.45f).normalized;
            AddSpindle(g, pos + side * (SpurBudWidth * 0.25f * size), bdir, SpurBudLength * size, SpurBudWidth * size, col, fx);
        }
    }

    /// <summary>A flat samara wing from <paramref name="b"/> along <paramref name="along"/>, its
    /// veined edge toward <paramref name="across"/>: a fan around a centre point.</summary>
    static void AddWing(Geo g, Vector3 b, Vector3 along, Vector3 across, float len, float wid, Color col, Vector2 fx)
    {
        Vector3 P(Vector2 q) => b + along * (q.x * len) + across * (q.y * wid);
        Vector3 centre = b + along * (0.45f * len);
        Vector3 nrm    = Vector3.Cross(P(WingOutline[0]) - centre, P(WingOutline[1]) - centre).normalized;
        Color   vein   = col * 0.8f, thin = Color.Lerp(col, Color.white, 0.15f);

        int c = g.verts.Count;
        g.Vert(centre, nrm, col, fx);
        foreach (var q in WingOutline) g.Vert(P(q), nrm, q.y > 0.2f ? vein : thin, fx);
        int m = WingOutline.Length;
        for (int j = 0; j < m; j++) { g.tris.Add(c); g.tris.Add(c + 1 + j); g.tris.Add(c + 1 + (j + 1) % m); }
    }

    /// <summary>A round papery samara (elm): a flat disc in the plane across <paramref name="normal"/>,
    /// notched at the end toward <paramref name="up"/>, with the seed darker in the middle.</summary>
    static void AddDisc(Geo g, Vector3 centre, Vector3 normal, Vector3 up, float r, Color col, Color seed, Vector2 fx)
    {
        if (r < 0.0008f) return;
        const int N = 10;
        Vector3 u = (up - normal * Vector3.Dot(up, normal)).normalized;
        Vector3 v = Vector3.Cross(normal, u);
        Vector3 P(int k)
        {
            float a = k * Mathf.PI * 2f / N;
            float rr = k == 0 ? 0.7f : 1f;   // the notch at the end
            return centre + (u * (Mathf.Cos(a) * 1.1f) + v * Mathf.Sin(a)) * (r * rr);
        }
        Vector3 nrm = Vector3.Cross(P(0) - centre, P(1) - centre).normalized;
        int     b   = g.verts.Count;
        g.Vert(centre, nrm, seed, fx);
        for (int k = 0; k < N; k++) g.Vert(P(k), nrm, Color.Lerp(col, Color.white, 0.12f), fx);
        for (int k = 0; k < N; k++) { g.tris.Add(b); g.tris.Add(b + 1 + k); g.tris.Add(b + 1 + (k + 1) % N); }
    }

    /// <summary>A scaly cone from <paramref name="at"/> along <paramref name="axis"/>: a core with
    /// rings of overlapping scales that spread as it <paramref name="open"/>s.</summary>
    static void AddCone(Geo g, Vector3 at, Vector3 axis, float len, float wid, ConeShape shape, float open, Color col, Vector2 fx)
    {
        if (len < 0.002f) return;
        float[] t = shape switch { ConeShape.Hanging => HangT, ConeShape.Upright => UpT, ConeShape.Round => RoundT, _ => OvoidT };
        float[] r = shape switch { ConeShape.Hanging => HangR, ConeShape.Upright => UpR, ConeShape.Round => RoundR, _ => OvoidR };
        Color dark = col * 0.75f, light = Color.Lerp(col, Color.white, 0.15f);
        AddLathe(g, at, axis, len, wid * 0.85f, t, r, 6, dark, col * 0.9f, fx, fx);

        Vector3 u = Vector3.Cross(axis, Vector3.up);
        if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(axis, Vector3.right);
        u.Normalize();
        Vector3 v   = Vector3.Cross(axis, u);
        float   phi = Mathf.Lerp(25f, 75f, open) * Mathf.Deg2Rad;
        const int S = 6;
        for (int k = 2; k < t.Length - 1; k++)
        {
            float rad = r[k] * wid * 0.5f;
            if (rad <= 0f) continue;
            Vector3 c  = at + axis * (t[k] * len);
            float   sl = len * 0.22f, sw = 2f * Mathf.PI * rad / S * 0.95f;
            for (int i = 0; i < S; i++)
            {
                float   a      = (i + 0.5f * (k & 1)) * Mathf.PI * 2f / S;
                Vector3 radial = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                Vector3 sdir   = axis * Mathf.Cos(phi) + radial * Mathf.Sin(phi);
                AddKite(g, c + radial * (rad * 0.9f), sdir, Vector3.Cross(axis, radial).normalized, sl, sw, dark, light, fx);
            }
        }
    }
}
