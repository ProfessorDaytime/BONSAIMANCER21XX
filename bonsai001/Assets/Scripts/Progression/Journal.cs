using System;
using System.Collections.Generic;

/// <summary>Encyclopedia section a journal entry belongs to.</summary>
public enum JournalCategory { Technique, Phenomenon, Species }

/// <summary>
/// One Journal / Encyclopedia entry — a short piece of bonsai knowledge that unlocks as you
/// encounter it. Unlock is a predicate over the <see cref="ProgressionProfile"/> (a milestone
/// reached, a tool unlocked, a species grown), so no extra saved state is needed beyond the
/// milestone/tool/journal lists the profile already keeps.
///
/// This is the zen reward layer (Slice 4) — read-only flavour, no scoring. See
/// Docs/PROGRESSION_DESIGN.md §4.
/// </summary>
public class JournalEntry
{
    public readonly string          id, title, body;
    public readonly JournalCategory category;
    public readonly Func<ProgressionProfile, bool> unlocked;

    public JournalEntry(JournalCategory cat, string id, string title, string body,
                        Func<ProgressionProfile, bool> unlocked)
    {
        this.category = cat;
        this.id       = id;
        this.title    = title;
        this.body     = body;
        this.unlocked = unlocked;
    }
}

/// <summary>Static encyclopedia registry. Built once; the JournalPanel renders it by category.</summary>
public static class Journal
{
    static List<JournalEntry> entries;

    public static IReadOnlyList<JournalEntry> Entries { get { Ensure(); return entries; } }

    // Helpers for unlock predicates.
    static bool Milestone(ProgressionProfile p, string id) => p != null && p.milestones.Contains(id);
    static bool Tool(ProgressionProfile p, string id)      => p != null && p.unlockedTools.Contains(id);

    static void Ensure()
    {
        if (entries != null) return;
        entries = new List<JournalEntry>();

        // ── Techniques — unlocked by first use (milestone) OR by a Career unlock. ──
        void Tech(string id, string toolId, string milestoneId, string title, string body)
            => entries.Add(new JournalEntry(JournalCategory.Technique, id, title, body,
                   p => Milestone(p, milestoneId) || Tool(p, toolId)));

        Tech("tech_prune", "trim", "first_trim", "Pruning",
            "Bonsai is shaped as much by what you remove as what you grow. A clean cut just above an " +
            "outward-facing bud redirects energy and keeps internodes short. Seal large cuts to speed callus.");
        Tech("tech_wire", "wire", "first_wire", "Wiring",
            "Wrapping wire lets you set a branch's direction. Wood 'remembers' once the wire has held it a " +
            "while — then remove it before it bites into the swelling bark and scars.");
        Tech("tech_pinch", "pinch", "first_trim", "Pinching",
            "Pinching soft new shoots back to the first pair of leaves builds ramification — the fine, " +
            "twiggy division that makes a canopy read as old and dense.");
        Tech("tech_repot", "soil", "first_repot", "Repotting",
            "Every few years the root ball is lifted, the old soil combed out, and the roots trimmed so " +
            "fresh free-draining mix can breathe. A pot-bound tree stalls and yellows.");
        Tech("tech_advanced", "advanced", "first_repot", "Air Layering & Rock Planting",
            "Advanced moves: air-layering grows roots partway up a trunk to harvest a new tree, and " +
            "root-over-rock (Ishitsuki) drapes living roots across stone before burying them to thicken.");

        // ── Phenomena — unlocked when first observed. ──
        entries.Add(new JournalEntry(JournalCategory.Phenomenon, "phen_bloom", "Flowering",
            "Flowering species set buds the previous autumn and open them in spring — some, like cherry, " +
            "on bare wood before the leaves. Bloom is a sign of a mature, well-fed tree.",
            p => Milestone(p, "first_bloom")));
        entries.Add(new JournalEntry(JournalCategory.Phenomenon, "phen_fruit", "Fruiting",
            "After bloom, fruiting species set fruit that ripens through summer into autumn — berries, " +
            "figs, samaras, or the cones of conifers — then drops to begin again.",
            p => Milestone(p, "first_fruit")));

        // ── Species — unlocked by growing each (species_<slug> milestone). ──
        void Species(string display)
            => entries.Add(new JournalEntry(JournalCategory.Species, "sp_" + Slug(display), display,
                   $"You have grown a {display}. Each species carries its own pace, bark, foliage, and habits.",
                   p => Milestone(p, "species_" + Slug(display))));

        Species("Japanese Black Pine"); Species("Japanese White Pine"); Species("Scots Pine");
        Species("Ezo Spruce");          Species("Alberta Spruce");
        Species("Atlas Cedar");         Species("Japanese Cedar");
        Species("Dawn Redwood");        Species("Swamp Cypress");        Species("Juniper");
        Species("Japanese Maple");      Species("Cherry");               Species("Wisteria");
        Species("Ficus");               Species("Elm");                  Species("Silver Birch");
        Species("Weeping Willow");
    }

    /// <summary>Matches the milestone-id slugging in ProgressionManager.TryGrowSpeciesMilestone.</summary>
    public static string Slug(string s) => s.Replace(" ", "_").ToLowerInvariant();
}
