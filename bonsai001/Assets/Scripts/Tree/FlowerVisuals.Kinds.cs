using UnityEngine;

// Per-kind flowers and fruit for FlowerVisuals: maple clusters and samaras, then everything that
// sits on flower spurs (cherry, elm, wisteria, catkins, and the fruit of species without showy
// flowers).
public partial class FlowerVisuals
{
    // ── Maples (Corymb) ───────────────────────────────────────────────────────

    void AddCluster(TreeNode n, TreeSpecies sp, in Times t, float now, Vector3 down, bool spawn)
    {
        float e = Smooth01((now - t.emerge) / EmergeDays);
        if (e <= 0f) return;
        bool  fruits = n.fruitMask != 0;
        float g      = fruits ? Smooth01((now - t.fruitStart) / Mathf.Max(1f, t.fruitFull - t.fruitStart)) : 0f;
        float ripe   = Smooth01((now - t.ripenStart) / Mathf.Max(1f, t.ripenEnd - t.ripenStart));
        var   drop   = DropFor(sp);

        // Peduncle: out of the opening bud along the shoot, drooping as it lengthens and as the
        // fruit gains weight.
        Vector3 axis  = n.growDirection.normalized;
        Vector3 side  = BudVisuals.Side(axis, BudVisuals.BaseAzimuth(n) + 45f);
        Vector3 a     = n.tipPosition + side * (n.radius * 0.5f);
        Vector3 out0  = (axis * 0.55f + side * 0.45f).normalized;
        float   len   = PeduncleLength * Mathf.Lerp(0.15f, 1f, e);
        float   droop = Mathf.Lerp(0.1f, 0.5f, e) + 0.5f * g;   // held out among the leaves; fruit pulls it down
        Vector3 p1    = a + out0 * (len * 0.5f);
        Vector3 p2    = a + out0 * (len * 0.55f) + down * (len * droop);
        Vector2 fx    = FlexOf(n);
        Color   stalk = Color.Lerp(Color.Lerp(sp.bloomColor, sp.youngBarkColor, 0.5f),
                                   Color.Lerp(sp.fruitRipeColor, sp.matureBarkColor, 0.5f), ripe);

        bool any = false;
        for (int i = 0; i < t.flowers; i++)
        {
            float hi     = Hash01(n.id, n.bloomYear, 10 + i);
            bool  fruit  = ((n.fruitMask >> i) & 1) != 0;
            float open0  = t.open + hi * FlowerStagger;
            float o      = Smooth01((now - open0) / OpenDays);
            float w      = Mathf.Clamp01((now - (open0 + OpenDays + sp.bloomDurationDays)) / WitherDays);
            float drop0  = fruit ? DropDay(n, i, 0, t, drop) : 0f;
            float drop1  = fruit ? DropDay(n, i, 1, t, drop) : 0f;
            bool  flower = w < 1f;
            bool  show0  = fruit && g > 0f && now < drop0, show1 = fruit && g > 0f && now < drop1;
            bool  spawn0 = spawn && fruit && lastBuildNow < drop0 && drop0 <= now;
            bool  spawn1 = spawn && fruit && lastBuildNow < drop1 && drop1 <= now;
            if (!flower && !show0 && !show1 && !spawn0 && !spawn1) continue;

            // Pedicel: a short stalk off the peduncle's end; the cluster spreads as it emerges and
            // fruit pulls it straight down.
            float   az     = i * 137.508f + hi * 40f;
            Vector3 radial = BudVisuals.Side(down, az);
            float   spread = Mathf.Lerp(0.3f, 0.8f, e) * Mathf.Lerp(0.8f, 1.1f, hi);
            Vector3 ped    = (down * (1f - 0.5f * spread) + radial * spread).normalized;
            if (fruit) ped = Vector3.Slerp(ped, down, 0.45f * g).normalized;
            float   plen   = PedicelLength * (0.7f + 0.6f * Hash01(n.id, n.bloomYear, 30 + i)) * Mathf.Lerp(0.2f, 1f, e);
            Vector3 f      = p2 + ped * plen;
            Vector2 fxEnd  = Hang(fx, 1f);

            if (flower || show0 || show1)
            {
                any = true;
                seg[0] = p2; seg[1] = f;
                AddTube(geo, seg, 2, PedicelRadius * Mathf.Lerp(0.5f, 1f, e), PedicelRadius * 0.8f, stalk, Hang(fx, 0.7f), fxEnd);
            }
            if (flower)
            {
                float size  = e * (fruit ? Mathf.Lerp(1f, 0.45f, w) : 1f - w);
                float theta = Mathf.Lerp(12f, 75f, o) + 25f * w;   // opens, then folds back as it withers
                Color sepal = Color.Lerp(Color.Lerp(sp.bloomColor * 0.9f, sp.bloomColor, o), Withered, w);
                Color heart = Color.Lerp(sepal, FlowerHeart, 0.8f * o * (1f - w));
                AddFlower(geo, f, ped, theta, SepalLength * size, SepalWidth * size, heart, sepal, fxEnd, az);
            }
            if (show0 || show1 || spawn0 || spawn1)
                AddSamaraPair(sp, f, down, az, g, ripe, show0, show1, spawn0, spawn1, fxEnd);
        }

        if (!any) return;
        for (int k = 0; k < 4; k++) path[k] = Bezier(a, p1, p2, k / 3f);   // the peduncle's curve
        AddTube(geo, path, 4, PeduncleRadius * Mathf.Lerp(0.5f, 1f, e), PedicelRadius, stalk, fx, Hang(fx, 0.55f));
    }

    /// <summary>A pair of samaras joined at <paramref name="joint"/>, wings spread
    /// <c>samaraSpread</c> apart in one plane, across the pedicel's swing.</summary>
    void AddSamaraPair(TreeSpecies sp, Vector3 joint, Vector3 down, float az, float g, float ripe,
                       bool show0, bool show1, bool spawn0, bool spawn1, Vector2 fx)
    {
        Vector3 h     = BudVisuals.Side(down, az + 90f);
        Vector3 plane = Vector3.Cross(h, down).normalized;
        float   half  = sp.samaraSpread * 0.5f * Mathf.Deg2Rad;
        Color   wing  = Color.Lerp(sp.fruitColor, sp.fruitRipeColor, ripe);
        Color   seed  = Color.Lerp(wing, SeedTint, 0.35f);
        float   sLen  = SeedLength * Mathf.Lerp(0.35f, 1f, g), sWid = SeedWidth * Mathf.Lerp(0.4f, 1f, g);
        float   wLen  = WingLength * Smooth01(g / 0.85f),     wWid = WingWidth * Smooth01((g - 0.1f) / 0.9f);
        for (int k = 0; k < 2; k++)
        {
            float   s     = k == 0 ? -1f : 1f;
            Vector3 dir   = (down * Mathf.Cos(half) + h * (s * Mathf.Sin(half))).normalized;
            Vector3 outer = Vector3.Cross(plane, dir).normalized;
            if (Vector3.Dot(outer, h * s) < 0f) outer = -outer;
            if (k == 0 ? show0 : show1)
            {
                AddSpindle(geo, joint, dir, sLen, sWid, seed, fx);
                if (wLen > 0.0005f && wWid > 0.0002f)
                    AddWing(geo, joint + dir * (sLen * 0.7f), dir, outer, wLen, wWid, wing, fx);
            }
            if (k == 0 ? spawn0 : spawn1) QueuePiece(joint, dir, outer, Piece.Samara, sp.fruitRipeColor, sp);
        }
    }

    // ── Flower spurs ──────────────────────────────────────────────────────────

    static float GrowDays(TreeSpecies sp) => sp.bloomType switch
    {
        BloomType.Raceme => RacemeGrowDays,
        BloomType.Catkin => CatkinGrowDays,
        BloomType.None   => 1f,
        _                => BalloonDays,
    };

    /// <summary>A twig's winter flower buds (or birch's catkins, out all winter), before they're spent.</summary>
    void AddWinterSpurs(TreeNode n, TreeSpecies sp, float size, int year, Vector3 down)
    {
        bool catkins = sp.bloomType == BloomType.Catkin && sp.fruitType == FruitType.Cone;
        int  spurs   = SpurCount(sp), i = 0;
        for (int k = 0; k < spurs; k++)
        {
            SpurAt(n, k, out var pos, out var dir, out var on, out float f);
            Vector2 fx = FlexAlong(on, f);
            if (!catkins) { AddWinterBud(n, sp, k, pos, dir, size, sp.budColor, fx); continue; }
            int count = PerSpur(n, sp, k, spurs, year);
            for (int j = 0; j < count; j++, i++)
            {
                Vector3 d = CatkinDir(n, year, i, j, count, dir, down, 0f, sp);
                AddLathe(geo, pos, d, CatkinWinter * size, CatkinWidth * 0.8f, CatkinT, CatkinR, 6,
                         sp.budColor, sp.budColor * 0.85f, fx, Hang(fx, 0.5f));
            }
        }
    }

    /// <summary>A spur's winter flower buds: a big single bud for a raceme, small rosettes for elm
    /// and willow, plump rosettes for cherry.</summary>
    void AddWinterBud(TreeNode n, TreeSpecies sp, int k, Vector3 pos, Vector3 dir, float size, Color col, Vector2 fx)
    {
        float scale = sp.bloomType switch { BloomType.Raceme => 1.4f, BloomType.Tuft => 0.7f, BloomType.Catkin => 0.6f, _ => 1f };
        int   count = sp.bloomType == BloomType.Raceme ? 1 : Hash01(n.id, 1, 60 + k) < 0.5f ? 2 : 3;
        AddSpurBuds(geo, n, k, pos, dir, size * scale, count, col, fx);
    }

    void AddSpurTwig(TreeNode n, TreeSpecies sp, in Times t, float now, Vector3 down, bool spawn)
    {
        var c = new Ctx { n = n, sp = sp, t = t, drop = DropFor(sp), now = now, down = down, spawn = spawn };
        float swellK = Smooth01((now - t.swell) / Mathf.Max(1f, t.emerge - t.swell));
        c.e    = Smooth01((now - t.emerge) / GrowDays(sp));
        c.g    = n.fruitMask != 0 ? Smooth01((now - t.fruitStart) / Mathf.Max(1f, t.fruitFull - t.fruitStart)) : 0f;
        c.ripe = Smooth01((now - t.ripenStart) / Mathf.Max(1f, t.ripenEnd - t.ripenStart));
        Color budCol = Color.Lerp(sp.budColor, sp.budSwellColor, swellK);
        // Cones sit just behind the tip, clear of the needle tuft there.
        int offset = sp.bloomType == BloomType.None && sp.fruitType == FruitType.Cone ? 1 : 0;

        int i = 0;
        for (int k = 0; k < t.spurs; k++)
        {
            SpurAt(n, k + offset, out c.spur, out c.dir, out var on, out float f);
            c.fx = FlexAlong(on, f);
            // Winter buds swell, then shrink away as the flowers push out (birch's catkins carry on).
            if (HasWinterBuds(sp) && sp.bloomType != BloomType.Catkin)
                AddWinterBud(n, sp, k, c.spur, c.dir, (1f + 0.35f * swellK) * (1f - c.e), budCol, c.fx);
            c.count = PerSpur(n, sp, k, t.spurs, n.bloomYear);
            for (c.j = 0; c.j < c.count && i < t.flowers; c.j++, i++)
            {
                c.i     = i;
                c.hi    = Hash01(n.id, n.bloomYear, 10 + i);
                c.fruit = ((n.fruitMask >> i) & 1) != 0;
                switch (sp.bloomType)
                {
                    case BloomType.Blossom: AddCherryFlower(c); break;
                    case BloomType.Tuft:    AddElmFlower(c);    break;
                    case BloomType.Raceme:  AddRaceme(c);       break;
                    case BloomType.Catkin:  AddCatkin(c);       break;
                    default:                AddBareFruit(c);    break;
                }
            }
        }
    }

    // ── Cherry (Blossom) ──────────────────────────────────────────────────────

    /// <summary>A pink balloon on a long stalk that opens into five notched petals, which let go one
    /// by one; a fertilised flower swells into a cherry that hangs its stalk down.</summary>
    void AddCherryFlower(in Ctx c)
    {
        if (c.e <= 0f) return;
        var   sp      = c.sp;
        float open0   = c.t.open + c.hi * FlowerStagger;
        float o       = Smooth01((c.now - open0) / OpenDays);
        float fall    = open0 + OpenDays + sp.bloomDurationDays;   // the petals start to let go
        float gone    = fall + PetalDrift;
        float wither  = Mathf.Clamp01((c.now - gone) / WitherDays);
        bool  fruitOn = FruitOn(c, 0, out bool letGo);
        bool  heartOn = c.now < gone + (c.fruit ? 2f : WitherDays);

        float   az     = c.j * 360f / c.count + c.hi * 50f;
        Vector3 radial = BudVisuals.Side(c.dir, az);
        Vector3 ped    = Vector3.Slerp((c.dir * 0.6f + radial * 0.8f).normalized, c.down,
                                       0.25f + (c.fruit ? 0.55f * c.g : 0f)).normalized;
        float   plen   = CherryStalk * (0.75f + 0.5f * Hash01(c.n.id, c.n.bloomYear, 30 + c.i)) * Mathf.Lerp(0.15f, 1f, c.e);
        Vector3 fpos   = c.spur + ped * plen;
        Vector2 fxEnd  = Hang(c.fx, 1f);
        if (heartOn || fruitOn)
        {
            seg[0] = c.spur; seg[1] = fpos;
            AddTube(geo, seg, 2, StalkRadius, StalkRadius * 0.8f, StalkGreen, Hang(c.fx, 0.5f), fxEnd);
        }

        Color edge  = sp.bloomColor, blush = Color.Lerp(sp.bloomColor, PetalBlush, 0.55f);
        float theta = Mathf.Lerp(10f, 80f, o), cup = 0.25f * o, az0 = c.hi * 360f;
        for (int p = 0; p < 5; p++)
        {
            float pd = fall + Hash01(c.n.id, c.n.bloomYear, 100 + c.i * 5 + p) * PetalDrift;
            if (c.now < pd)
                AddPetal(geo, fpos, ped, az0 + 72f * p, theta, cup, PetalLength * c.e, PetalWidth * c.e, blush, edge, fxEnd);
            else if (c.spawn && lastBuildNow < pd)
                QueuePetal(fpos, ped, az0 + 72f * p, theta, sp);
        }
        // Heart: stamens while in flower, withering once the petals have gone.
        if (heartOn && o > 0f)
            AddStar(geo, fpos + ped * 0.002f, ped, 0.007f * c.e * (1f - wither) * Mathf.Clamp01(o * 2f),
                    Color.Lerp(Stamens, Withered, wither), fxEnd, az0);
        if (fruitOn)
            AddBerry(geo, fpos, c.down, CherryRadius * Mathf.Max(0.1f, sp.fruitSize) * Mathf.Lerp(0.15f, 1f, c.g),
                     FruitColour(sp, c.ripe), fxEnd);
        if (letGo) QueuePiece(fpos, c.down, radial, Piece.Cherry, sp.fruitRipeColor, sp);
    }

    void QueuePetal(Vector3 at, Vector3 face, float az, float theta, TreeSpecies sp)
    {
        Vector3 radial = BudVisuals.Side(face, az);
        float   c = Mathf.Cos(theta * Mathf.Deg2Rad), s = Mathf.Sin(theta * Mathf.Deg2Rad);
        Vector3 along  = face * c + radial * s;
        QueuePiece(at + along * (PetalLength * 0.5f), along, Vector3.Cross(face, radial), Piece.Petal, sp.bloomColor, sp);
    }

    // ── Elm (Tuft) ────────────────────────────────────────────────────────────

    /// <summary>A tiny dark tuft of a flower on bare wood; a fertilised one becomes a round samara,
    /// a flat disc with the seed in the middle, that drifts down when ripe.</summary>
    void AddElmFlower(in Ctx c)
    {
        if (c.e <= 0f) return;
        var   sp    = c.sp;
        float open0 = c.t.open + c.hi * FlowerStagger;
        float o     = Smooth01((c.now - open0) / OpenDays);
        float w     = Mathf.Clamp01((c.now - (open0 + OpenDays + sp.bloomDurationDays)) / WitherDays);
        bool  on    = FruitOn(c, 0, out bool letGo);
        if (w >= 1f && !on && !letGo) return;

        float   az     = c.j * 360f / c.count + c.hi * 50f;
        Vector3 radial = BudVisuals.Side(c.dir, az);
        Vector3 ped    = Vector3.Slerp((c.dir * 0.7f + radial * 0.7f).normalized, c.down,
                                       0.15f + (c.fruit ? 0.35f * c.g : 0f)).normalized;
        Vector3 f      = c.spur + ped * (ElmStalk * Mathf.Lerp(0.3f, 1f, c.e));
        Vector2 fxEnd  = Hang(c.fx, 1f);
        if (w < 1f || on)
        {
            seg[0] = c.spur; seg[1] = f;
            AddTube(geo, seg, 2, StalkRadius * 0.7f, StalkRadius * 0.5f, StalkGreen, c.fx, fxEnd);
        }
        if (w < 1f)
        {
            float size  = c.e * (1f - 0.6f * w);
            Color sepal = Color.Lerp(sp.bloomColor, Withered, w);
            Color heart = Color.Lerp(sepal, FlowerHeart, 0.5f * o * (1f - w));
            AddFlower(geo, f, ped, Mathf.Lerp(15f, 70f, o) + 20f * w, ElmSepal * size, ElmSepal * 0.6f * size,
                      heart, sepal, fxEnd, az);
        }
        if (on || letGo)
        {
            float   r      = ElmSamara * Mathf.Max(0.1f, sp.fruitSize) * Mathf.Lerp(0.15f, 1f, c.g);
            Vector3 centre = f + ped * (r * 0.9f);
            Vector3 normal = (radial + c.down * 0.3f).normalized;
            Color   col    = FruitColour(sp, c.ripe);
            if (on)    AddDisc(geo, centre, normal, ped, r, col, Color.Lerp(col, SeedTint, 0.5f), fxEnd);
            if (letGo) QueuePiece(centre, ped, Vector3.Cross(normal, ped), Piece.Disc, sp.fruitRipeColor, sp);
        }
    }

    // ── Wisteria (Raceme) ─────────────────────────────────────────────────────

    /// <summary>A long hanging raceme: it lengthens out of its fat winter bud, its pea flowers open
    /// from the top down and drop, and a fertilised one hangs one or two velvety pods.</summary>
    void AddRaceme(in Ctx c)
    {
        if (c.e <= 0f) return;
        var     sp      = c.sp;
        float   rl      = RacemeLength * (0.8f + 0.4f * c.hi) * Mathf.Lerp(0.15f, 1f, c.e);
        Vector3 out0    = Vector3.Slerp(c.dir, c.down, 0.35f).normalized;
        Vector3 p0      = c.spur, p1 = c.spur + out0 * (rl * 0.15f), p2 = c.spur + out0 * (rl * 0.2f) + c.down * rl;
        Vector2 fxEnd   = Hang(c.fx, 1f);
        int     florets = Mathf.Clamp(sp.flowersPerCluster, 4, 24);
        Color   bud     = Color.Lerp(sp.budSwellColor, sp.bloomColor, 0.35f);
        float   lastFall = 0f;
        bool    any     = false;

        for (int m = 0; m < florets; m++)
        {
            float   u      = 0.12f + 0.86f * m / (florets - 1f);
            float   hm     = Hash01(c.n.id, c.n.bloomYear, 200 + c.i * 32 + m);
            float   open0  = c.t.open + u * RacemeOpenDays + hm;
            float   fall   = open0 + OpenDays + sp.bloomDurationDays + hm * 3f;
            Vector3 p      = Bezier(p0, p1, p2, u);
            Vector3 radial = BudVisuals.Side(c.down, m * 137.508f + c.hi * 90f);
            lastFall = Mathf.Max(lastFall, fall);
            if (c.now >= fall)
            {
                if (c.spawn && lastBuildNow < fall && hm < 0.5f)   // about half drop a petal to flutter down
                    QueuePiece(p + radial * FloretSize, radial, Vector3.Cross(c.down, radial), Piece.Petal, sp.bloomColor, sp);
                continue;
            }
            float size = Mathf.Lerp(1f, 0.55f, u) * Mathf.Clamp01(c.e * 1.6f - u * 0.6f);   // the tip florets form last
            float fl   = FloretSize * size;
            if (fl < 0.001f) continue;
            any = true;
            float   o    = Smooth01((c.now - open0) / OpenDays);
            Vector2 fxm  = Hang(c.fx, Mathf.Lerp(0.3f, 1f, u));
            Color   col  = Color.Lerp(bud, sp.bloomColor, o);
            Vector3 face = (radial * 0.85f + c.down * 0.3f).normalized;
            // Keel: a small closed spindle pointing out and down; the banner stands up behind it as it opens.
            AddSpindle(geo, p, face, fl * 0.9f, fl * 0.45f, col * 0.9f, fxm);
            if (o > 0f)
            {
                Vector3 up     = -c.down;
                Vector3 banner = Vector3.Slerp(face, up, Mathf.Lerp(0.2f, 0.7f, o)).normalized;
                AddPetalBasis(geo, p, banner, Vector3.Cross(up, radial).normalized, radial, 0.2f,
                              fl * 1.1f * o, fl * 1.2f * o, Color.Lerp(col, Stamens, 0.3f), col, fxm);
            }
        }

        bool pod0 = FruitOn(c, 0, out bool let0), pod1 = false, let1 = false;
        if (c.hi < 0.4f) pod1 = FruitOn(c, 1, out let1);   // some racemes set a second pod
        for (int s = 0; s < 2; s++)
        {
            bool on = s == 0 ? pod0 : pod1, let = s == 0 ? let0 : let1;
            if (!on && !let) continue;
            float   u    = 0.22f + 0.14f * s;
            Vector3 at   = Bezier(p0, p1, p2, u);
            Vector3 side = BudVisuals.Side(c.down, c.hi * 360f + s * 140f);
            Vector3 d    = (c.down + side * 0.25f).normalized;
            float   size = Mathf.Max(0.1f, sp.fruitSize) * Mathf.Lerp(0.15f, 1f, c.g);
            Color   col  = FruitColour(sp, c.ripe);
            if (on)  AddLathe(geo, at, d, PodLength * size, PodWidth * size, PodT, PodR, 6, col, col * 0.92f, Hang(c.fx, u), fxEnd, 0.35f);
            if (let) QueuePiece(at, d, side, Piece.Pod, sp.fruitRipeColor, sp);
        }

        // The stalk, while florets or pods hang on; a bare one withers away after its last floret.
        float wither = Mathf.Clamp01((c.now - lastFall) / WitherDays);
        if (!any && wither >= 1f && !pod0 && !pod1) return;
        Color stalkCol = Color.Lerp(StalkGreen, Withered, c.fruit ? c.ripe : wither);
        for (int k = 0; k < 4; k++) path[k] = Bezier(p0, p1, p2, k / 3f);
        AddTube(geo, path, 4, StalkRadius * 1.6f, StalkRadius, stalkCol, c.fx, fxEnd);
    }

    // ── Catkins (birch, willow) ───────────────────────────────────────────────

    /// <summary>Where catkin <paramref name="i"/> on a spur points: out from the spur, stiff in
    /// winter (e = 0), hanging (birch) or arching (willow) once it has loosened (e = 1).</summary>
    static Vector3 CatkinDir(TreeNode n, int year, int i, int j, int count, Vector3 dir, Vector3 down, float e, TreeSpecies sp)
    {
        float   hi     = Hash01(n.id, year, 10 + i);
        Vector3 radial = BudVisuals.Side(dir, j * 360f / count + hi * 50f);
        float   droop  = Mathf.Lerp(0.3f, sp.fruitType == FruitType.Fluff ? 0.45f : 0.85f, e);
        return Vector3.Slerp((dir * 0.5f + radial * 0.8f).normalized, down, droop).normalized;
    }

    /// <summary>A male catkin lengthens, goes golden as it sheds pollen, then drops; a female one
    /// fills out into a seed catkin: birch's hangs and ripens brown, willow's bursts into fluff.</summary>
    void AddCatkin(in Ctx c)
    {
        var     sp    = c.sp;
        bool    birch = sp.fruitType == FruitType.Cone;
        float   open0 = c.t.open + c.hi * FlowerStagger;
        float   o     = Smooth01((c.now - open0) / OpenDays);
        Vector2 fxEnd = Hang(c.fx, 1f);

        if (!c.fruit)
        {
            float   fall = open0 + OpenDays + sp.bloomDurationDays;
            Vector3 d    = CatkinDir(c.n, c.n.bloomYear, c.i, c.j, c.count, c.dir, c.down, c.e, sp);
            float   len  = Mathf.Lerp(birch ? CatkinWinter : 0f, CatkinLength, c.e);
            Color   col  = Color.Lerp(birch ? sp.budColor : sp.budSwellColor, sp.bloomColor, o);
            if (c.now < fall)
            {
                if (len > 0.002f)
                    AddLathe(geo, c.spur, d, len, CatkinWidth, CatkinT, CatkinR, 6, col, col * 0.85f, c.fx, fxEnd);
            }
            else if (c.spawn && lastBuildNow < fall)
                QueuePiece(c.spur + d * (len * 0.5f), d, BudVisuals.Side(d, 0f), Piece.Catkin, col, sp);
            return;
        }

        bool  on  = FruitOn(c, 0, out bool letGo);
        Color col2 = FruitColour(sp, c.ripe);
        if (!on && !letGo)
        {
            // In flower, before it starts to fill: a small catkin standing up from the spur.
            if (c.g <= 0f && c.e > 0f)
            {
                Vector3 up = Vector3.Slerp(c.dir, -c.down, 0.3f).normalized;
                AddLathe(geo, c.spur, up, CatkinLength * 0.25f * c.e, CatkinWidth * 0.8f, CatkinT, CatkinR, 6,
                         sp.fruitColor, sp.fruitColor * 0.85f, c.fx, fxEnd);
            }
            return;
        }
        if (birch)
        {
            // The seed catkin tips over to hang as it lengthens through the summer, green to brown.
            ConeDims(sp, Mathf.Max(0.1f, sp.fruitSize) * Mathf.Lerp(0.25f, 1f, c.g), out float len, out float wid);
            Vector3 d = Vector3.Slerp(Vector3.Slerp(c.dir, -c.down, 0.3f), c.down, Mathf.Clamp01(c.g * 1.5f)).normalized;
            if (on)    AddCone(geo, c.spur, d, len, wid, ConeShape.Hanging, 0f, col2, fxEnd);
            if (letGo) QueuePiece(c.spur, d, BudVisuals.Side(d, 0f), Piece.Cone, sp.fruitRipeColor, sp);
        }
        else
        {
            // Willow: the seed catkin fattens and turns white as it bursts into fluff.
            float   len  = CatkinLength * Mathf.Lerp(0.3f, 1f, c.g);
            Vector3 d    = CatkinDir(c.n, c.n.bloomYear, c.i, c.j, c.count, c.dir, c.down, 1f, sp);
            float   puff = 1f + 1.2f * c.ripe;
            if (on)
                AddLathe(geo, c.spur, d, len, CatkinWidth * puff, CatkinT, CatkinR, 6, col2,
                         Color.Lerp(col2, Color.white, 0.3f), c.fx, fxEnd);
            if (letGo)
                for (int p = 0; p < 3; p++)
                    QueuePiece(c.spur + d * (len * (0.3f + 0.3f * p)), d, BudVisuals.Side(d, 0f), Piece.Fluff, FluffWhite, sp);
        }
    }

    // ── Fruit without showy flowers (figs, berries, cones) ────────────────────

    void AddBareFruit(in Ctx c)
    {
        if (!c.fruit) return;
        bool on = FruitOn(c, 0, out bool letGo);
        if (!on && !letGo) return;
        var     sp     = c.sp;
        float   size   = Mathf.Max(0.1f, sp.fruitSize) * Mathf.Lerp(0.15f, 1f, c.g);
        Vector3 radial = BudVisuals.Side(c.dir, c.j * 360f / c.count + c.hi * 50f);
        Color   col    = FruitColour(sp, c.ripe);
        Vector2 fxEnd  = Hang(c.fx, 1f);
        switch (sp.fruitType)
        {
            case FruitType.Fig:
            {
                // Figs sit right on the twig, in pairs, pointing out.
                Vector3 d = (radial + c.dir * 0.3f).normalized;
                if (on)    AddLathe(geo, c.spur, d, FigLength * size, FigWidth * size, PearT, PearR, 6, col, col, c.fx, c.fx);
                if (letGo) QueuePiece(c.spur, d, BudVisuals.Side(d, 0f), Piece.Fig, sp.fruitRipeColor, sp);
                break;
            }
            case FruitType.Cone:
            {
                ConeDims(sp, size, out float len, out float wid);
                Vector3 at = c.spur, d;
                float   stalk = 0f;
                switch (sp.coneShape)
                {
                    case ConeShape.Hanging: d = c.down; stalk = 0.008f; break;                                    // spruces
                    case ConeShape.Upright: d = -c.down; at -= c.down * (len * 0.1f); break;                      // cedars sit on top
                    case ConeShape.Round:   d = Vector3.Slerp(c.dir, c.down, 0.6f).normalized; stalk = 0.012f; break;
                    default:                d = Vector3.Slerp(c.dir, c.down, 0.55f).normalized; break;            // pines
                }
                if (stalk > 0f)
                {
                    Vector3 end = at + d * stalk;
                    if (on)
                    {
                        seg[0] = at; seg[1] = end;
                        AddTube(geo, seg, 2, StalkRadius, StalkRadius * 0.8f, Color.Lerp(StalkGreen, Withered, c.ripe), c.fx, fxEnd);
                    }
                    at = end;
                }
                // Pine and spruce cones open their scales as they ripen and dry.
                float open = sp.coneShape == ConeShape.Ovoid || sp.coneShape == ConeShape.Hanging
                    ? Smooth01((c.now - (c.t.ripenEnd - 5f)) / 25f) : 0.2f * c.ripe;
                if (on)    AddCone(geo, at, d, len, wid, sp.coneShape, open, col, fxEnd);
                if (letGo) QueuePiece(at, d, BudVisuals.Side(d, 0f), Piece.Cone, sp.fruitRipeColor, sp);
                break;
            }
            default:
            {
                // Berries on short stalks along the twig.
                Vector3 d   = Vector3.Slerp((c.dir * 0.5f + radial).normalized, c.down, 0.3f).normalized;
                Vector3 top = c.spur + d * 0.006f;
                if (on)
                {
                    seg[0] = c.spur; seg[1] = top;
                    AddTube(geo, seg, 2, StalkRadius * 0.7f, StalkRadius * 0.6f, StalkGreen, c.fx, fxEnd);
                    AddBerry(geo, top, c.down, CherryRadius * size, col, fxEnd);
                }
                if (letGo) QueuePiece(top, c.down, radial, Piece.Berry, sp.fruitRipeColor, sp);
                break;
            }
        }
    }
}
