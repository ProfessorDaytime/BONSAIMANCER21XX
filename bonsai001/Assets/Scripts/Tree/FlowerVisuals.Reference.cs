#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

// Reference shapes for modelling replacements in Blender (2026-10-30; editor menu
// Bonsai ▸ Export Flower and Fruit Shapes, FlowerShapeExport). Built by the same code the game draws
// with, on stand-in twigs, by components that never wake (so no mesh objects or materials exist).
public partial class FlowerVisuals
{
    /// <summary>One exported shape: a file name, a note for the README, and its mesh.</summary>
    public struct ReferenceShape
    {
        public string name, note;
        public Mesh   mesh;
    }

    const int RefYear = 2001;   // any year; not a leap year

    /// <summary>Every bud, flower and fruit shape. Parts sit at the origin where they attach,
    /// pointing along +Z or hanging down -Y: the pieces the game places, repeats and animates.
    /// Twigs are whole flowering twigs posed at one stage, tip at the origin, for reference.
    /// <paramref name="find"/> returns a species by name, for its colours and sizes.</summary>
    public static List<ReferenceShape> BuildReferenceShapes(Func<string, TreeSpecies> find)
    {
        var go = new GameObject("_FlowerReference") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var flowers = go.AddComponent<FlowerVisuals>();
            var buds    = go.AddComponent<BudVisuals>();
            return flowers.BuildReferences(find, buds);
        }
        finally { DestroyImmediate(go); }
    }

    List<ReferenceShape> BuildReferences(Func<string, TreeSpecies> find, BudVisuals buds)
    {
        var list = new List<ReferenceShape>();
        var temp = new List<TreeSpecies>();
        TreeSpecies Sp(string name)
        {
            var s = find(name);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<TreeSpecies>();   // defaults, if the species is missing
            temp.Add(s);
            return s;
        }
        void Take(string name, string note)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            geo.ApplyTo(m);
            list.Add(new ReferenceShape { name = name, note = note, mesh = m });
            geo.Clear();
        }
        void TakeMesh(string name, string note, Mesh m) => list.Add(new ReferenceShape { name = name, note = note, mesh = m });

        // A stand-in twig leaning up and out along +X, its tip at the origin.
        TreeNode Twig(int id, int fruitMask, bool flowerBud = false)
        {
            Vector3 d = new Vector3(0.6f, 0.8f, 0f).normalized;
            return new TreeNode(id, 3, -d * 0.15f, d, 0.006f, 0.15f, null)
            {
                length = 0.15f, isGrowing = false, bloomYear = RefYear, fruitMask = fruitMask, hasFlowerBud = flowerBud,
            };
        }

        var     fx   = Vector2.one;
        var     down = Vector3.down;
        var     jm   = Sp("Japanese Maple");
        var     tri  = Sp("Trident Maple");
        var     ch   = Sp("Cherry");
        var     wi   = Sp("Wisteria");
        var     elm  = Sp("Elm");
        var     bi   = Sp("Silver Birch");
        var     wl   = Sp("Weeping Willow");
        var     fi   = Sp("Ficus");
        var     ju   = Sp("Juniper");
        const int All = 0xFFFF;
        geo.Clear();

        // ── Parts ─────────────────────────────────────────────────────────────
        AddKite(geo, Vector3.zero, Vector3.forward, Vector3.right, SepalLength, SepalWidth, FlowerHeart, jm.bloomColor, fx);
        Take("part_maple_sepal", "One sepal of a maple flower, base at the origin, along +Z. The game sets five round the end of a stalk and opens them from 12° (a closed bud) to 75° (a star).");

        AddSpindle(geo, Vector3.zero, Vector3.forward, SeedLength, SeedWidth, Color.Lerp(jm.fruitColor, SeedTint, 0.35f), fx);
        AddWing(geo, Vector3.forward * (SeedLength * 0.7f), Vector3.forward, Vector3.right, WingLength, WingWidth, jm.fruitColor, fx);
        Take("part_maple_samara", "One samara: the nutlet at the origin (where the pair joins) and its wing along +Z, veined edge toward +X. The game mirrors it into a pair samaraSpread apart (140° Japanese, 40° trident), grows it from the flower, and spins it down when ripe.");

        AddPetalBasis(geo, Vector3.zero, Vector3.forward, Vector3.right, Vector3.up, 0.25f, PetalLength, PetalWidth,
                      Color.Lerp(ch.bloomColor, PetalBlush, 0.55f), ch.bloomColor, fx);
        Take("part_cherry_petal", "One cherry petal, base at the origin, along +Z, cupped toward +Y. The game sets five round a flower, opens them from 10° (a balloon) to 80°, and lets each go on its own to flutter down.");

        AddStar(geo, Vector3.zero, Vector3.up, 0.007f, Stamens, fx, 0f);
        Take("part_cherry_stamens", "A blossom's stamen tuft at its heart, facing +Y.");

        seg[0] = Vector3.zero; seg[1] = down * CherryStalk;
        AddTube(geo, seg, 2, StalkRadius, StalkRadius * 0.8f, StalkGreen, fx, fx);
        AddBerry(geo, seg[1], down, CherryRadius * ch.fruitSize, ch.fruitRipeColor, fx);
        Take("part_cherry_fruit", "A ripe cherry hanging on its stalk, the stalk's top at the origin. The stalk is the flower's; it bows lower as the cherry fills.");

        AddSpindle(geo, Vector3.zero, Vector3.forward, SpurBudLength, SpurBudWidth, ch.budColor, fx);
        Take("part_flower_bud", "A plump winter flower bud, base at the origin, along +Z: cherry size. Wisteria's are 1.4×, elm's 0.7×, willow's 0.6×. The game sets two or three in a rosette on each flower spur (wisteria one).");

        AddSpindle(geo, Vector3.zero, Vector3.forward, FloretSize * 0.9f, FloretSize * 0.45f, wi.bloomColor * 0.9f, fx);
        AddPetalBasis(geo, Vector3.zero, Vector3.Slerp(Vector3.forward, Vector3.up, 0.7f).normalized, Vector3.right, Vector3.forward,
                      0.2f, FloretSize * 1.1f, FloretSize * 1.2f, Color.Lerp(wi.bloomColor, Stamens, 0.3f), wi.bloomColor, fx);
        Take("part_wisteria_floret", "One pea flower: the keel along +Z (out from the raceme's stalk at the origin) and the banner petal standing up behind it. The game strings ~14 down each raceme, largest at the top, opening from the top down.");

        AddLathe(geo, Vector3.zero, down, PodLength * wi.fruitSize, PodWidth * wi.fruitSize, PodT, PodR, 6,
                 wi.fruitColor, wi.fruitColor * 0.92f, fx, fx, 0.35f);
        Take("part_wisteria_pod", "A velvety pod hanging from the origin, flattened across Z. One or two hang from a raceme's upper stalk.");

        AddKite(geo, Vector3.zero, Vector3.forward, Vector3.right, ElmSepal, ElmSepal * 0.6f, FlowerHeart, elm.bloomColor, fx);
        Take("part_elm_sepal", "One sepal of an elm flower tuft, along +Z. Set five at a time, like a maple flower, on a short stalk.");

        float er = ElmSamara * elm.fruitSize;
        AddDisc(geo, Vector3.forward * (er * 0.9f), Vector3.up, Vector3.forward, er, elm.fruitColor,
                Color.Lerp(elm.fruitColor, SeedTint, 0.5f), fx);
        Take("part_elm_samara", "A round samara lying flat (facing +Y), its stalk end at the origin and its notch toward +Z; the seed is the darker middle. It drifts down when ripe.");

        AddLathe(geo, Vector3.zero, down, CatkinLength, CatkinWidth, CatkinT, CatkinR, 6, bi.bloomColor, bi.bloomColor * 0.85f, fx, fx);
        Take("part_catkin", "A catkin hanging from the origin: birch's in spring (its winter catkins are this at 0.4× length), willow's the same shape in yellow-green. The colour bands suggest its scales.");

        ConeDims(bi, bi.fruitSize, out float bl, out float bw);
        AddCone(geo, Vector3.zero, down, bl, bw, ConeShape.Hanging, 0f, bi.fruitColor, fx);
        Take("part_birch_seed_catkin", "Birch's seed catkin hanging from the origin: a slender cone of scales, green in summer, brown when ripe.");

        AddLathe(geo, Vector3.zero, down, CatkinLength, CatkinWidth * 2.2f, CatkinT, CatkinR, 6,
                 wl.fruitRipeColor, Color.Lerp(wl.fruitRipeColor, Color.white, 0.3f), fx, fx);
        Take("part_willow_fluff_catkin", "A willow seed catkin bursting into fluff, hanging from the origin.");

        AddBall(geo, Vector3.zero, FluffRadius, FluffWhite, fx);
        Take("part_fluff", "A puff of willow seed fluff; the game lets puffs go to drift down.");

        AddLathe(geo, Vector3.zero, Vector3.forward, FigLength * fi.fruitSize, FigWidth * fi.fruitSize, PearT, PearR, 6,
                 fi.fruitRipeColor, fi.fruitRipeColor, fx, fx);
        Take("part_fig", "A fig, its base on the twig at the origin, pointing out along +Z. They sit in pairs along the twig.");

        AddBerry(geo, Vector3.zero, down, CherryRadius * ju.fruitSize, ju.fruitRipeColor, fx);
        Take("part_juniper_berry", "A juniper berry hanging from the origin (on a short stalk in game), pale on top like the bloom on a real berry.");

        AddKite(geo, Vector3.zero, Vector3.forward, Vector3.right, ConeLength * 0.22f, 0.012f, Color.gray * 0.75f, Color.gray, fx);
        Take("part_cone_scale", "One cone scale, along +Z. The game rings six per tier round a cone's core; they tilt out as a pine or spruce cone opens.");

        foreach (var (file, name) in new[] { ("pine", "Japanese Black Pine"), ("spruce", "Ezo Spruce"),
                                             ("cedar", "Atlas Cedar"), ("round", "Japanese Cedar") })
        {
            var s = Sp(name);
            ConeDims(s, s.fruitSize, out float l, out float w);
            Vector3 axis  = s.coneShape == ConeShape.Upright ? Vector3.up : down;
            bool    opens = s.coneShape == ConeShape.Ovoid || s.coneShape == ConeShape.Hanging;
            AddCone(geo, Vector3.zero, axis, l, w, s.coneShape, 0f, s.fruitColor, fx);
            Take($"part_cone_{file}", $"{name}'s cone ({s.coneShape}), attached at the origin, " +
                 (s.coneShape == ConeShape.Upright ? "standing up +Y." : "hanging down -Y.") +
                 " Green as it grows, then fruitRipeColor.");
            if (opens)
            {
                AddCone(geo, Vector3.zero, axis, l, w, s.coneShape, 1f, s.fruitRipeColor, fx);
                Take($"part_cone_{file}_open", $"{name}'s cone fully open, its scales spread, as it is in late autumn.");
            }
        }

        // ── Twigs, posed ──────────────────────────────────────────────────────
        var n = Twig(11, 0);
        var t = TimesFor(n, jm);
        AddCluster(n, jm, t, t.open + FlowerStagger + OpenDays + 0.5f, down, false);
        Take("twig_maple_in_flower", "A Japanese maple flower cluster in full bloom (mid-March), hanging from the twig tip at the origin.");

        n = Twig(12, All); t = TimesFor(n, jm);
        AddCluster(n, jm, t, t.fruitFull + 30f, down, false);
        Take("twig_maple_samaras", "A Japanese maple cluster in summer: pairs of red samaras.");

        n = Twig(13, All); t = TimesFor(n, tri);
        AddCluster(n, tri, t, t.fruitFull + 30f, down, false);
        Take("twig_trident_samaras", "A trident maple cluster in summer: nearly parallel samara pairs.");

        TakeMesh("twig_maple_buds_winter", "A maple twig tip in winter: the V-pair of buds at the tip and the smaller pair halfway back (BudVisuals).",
                 buds.BuildReference(Twig(14, 0), jm, false, 0f));
        TakeMesh("twig_maple_bud_opening", "A maple tip bud breaking in early March: four scales spreading open.",
                 buds.BuildReference(Twig(15, 0), jm, true, 0.45f));

        n = Twig(21, 0, true);
        AddWinterSpurs(n, ch, 1f, RefYear, down);
        Take("twig_cherry_winter_buds", "A cherry twig in winter: rosettes of plump flower buds on three spurs, one at the tip.");

        n = Twig(22, 0); t = TimesFor(n, ch);
        AddSpurTwig(n, ch, t, t.open - 0.3f, down, false);
        Take("twig_cherry_balloons", "A cherry twig in mid-February: pink balloons out on their stalks, about to open.");

        n = Twig(23, 0); t = TimesFor(n, ch);
        AddSpurTwig(n, ch, t, t.open + FlowerStagger + OpenDays + 1f, down, false);
        Take("twig_cherry_in_bloom", "A cherry twig in full bloom (late February).");

        n = Twig(24, All); t = TimesFor(n, ch);
        AddSpurTwig(n, ch, t, t.ripenEnd - DropFor(ch).lead - 0.5f, down, false);
        Take("twig_cherry_fruit", "A cherry twig in late May with every flower set as a cherry (in game about one in five sets).");

        n = Twig(31, 0, true);
        AddWinterSpurs(n, wi, 1f, RefYear, down);
        Take("twig_wisteria_winter_buds", "A wisteria twig in winter: one fat flower bud per spur.");

        n = Twig(32, 0); t = TimesFor(n, wi);
        AddSpurTwig(n, wi, t, t.open + RacemeOpenDays + OpenDays, down, false);
        Take("twig_wisteria_in_bloom", "A wisteria twig in April: two racemes in full bloom.");

        n = Twig(33, All); t = TimesFor(n, wi);
        AddSpurTwig(n, wi, t, t.fruitFull + 20f, down, false);
        Take("twig_wisteria_pods", "A wisteria twig in summer with pods (in game about a third of racemes set them).");

        n = Twig(41, 0); t = TimesFor(n, elm);
        AddSpurTwig(n, elm, t, t.open + FlowerStagger + OpenDays, down, false);
        Take("twig_elm_in_flower", "An elm twig in February: tiny dark-red flower tufts on bare wood.");

        n = Twig(42, All); t = TimesFor(n, elm);
        AddSpurTwig(n, elm, t, t.fruitFull, down, false);
        Take("twig_elm_samaras", "An elm twig at the end of March: clusters of round green samaras.");

        n = Twig(51, 0, true);
        AddWinterSpurs(n, bi, 1f, RefYear, down);
        Take("twig_birch_winter_catkins", "A birch twig tip in winter: stiff little catkins, out since autumn.");

        n = Twig(52, 0); t = TimesFor(n, bi);
        AddSpurTwig(n, bi, t, t.open + OpenDays, down, false);
        Take("twig_birch_catkins", "A birch twig tip in March: catkins lengthened, hanging and golden with pollen.");

        n = Twig(53, All); t = TimesFor(n, bi);
        AddSpurTwig(n, bi, t, t.fruitFull + 10f, down, false);
        Take("twig_birch_seed_catkins", "A birch twig tip in summer: hanging seed catkins.");

        n = Twig(61, 0); t = TimesFor(n, wl);
        AddSpurTwig(n, wl, t, t.open + OpenDays, down, false);
        Take("twig_willow_catkins", "A willow twig in March: yellow-green catkins.");

        n = Twig(62, All); t = TimesFor(n, wl);
        AddSpurTwig(n, wl, t, t.ripenEnd - DropFor(wl).lead - 0.5f, down, false);
        Take("twig_willow_fluff", "A willow twig in late May: seed catkins bursting into fluff.");

        n = Twig(71, All); t = TimesFor(n, fi);
        AddSpurTwig(n, fi, t, t.ripenEnd - DropFor(fi).lead - 0.5f, down, false);
        Take("twig_ficus_figs", "A ficus twig in September: figs in pairs along it, ripening purple.");

        n = Twig(72, All); t = TimesFor(n, ju);
        AddSpurTwig(n, ju, t, t.ripenEnd - DropFor(ju).lead - 0.5f, down, false);
        Take("twig_juniper_berries", "A juniper twig in October: berries, green to blue.");

        int id = 81;
        foreach (var (file, name) in new[] { ("pine", "Japanese Black Pine"), ("spruce", "Ezo Spruce"),
                                             ("cedar", "Atlas Cedar"), ("japanese_cedar", "Japanese Cedar"),
                                             ("dawn_redwood", "Dawn Redwood") })
        {
            var s = Sp(name);
            n = Twig(id++, All); t = TimesFor(n, s);
            AddSpurTwig(n, s, t, t.ripenEnd - 0.5f, down, false);
            Take($"twig_{file}_cones", $"A {name} twig as its cones ripen (cones sit just behind the tip, clear of the needles).");
        }

        foreach (var s in temp) DestroyImmediate(s);
        return list;
    }
}
#endif
