using System;
using System.Collections.Generic;

/// <summary>
/// Rolling log of auto-care actions and seasonal health narratives (PLAN item D).
///
/// Static so any system can log without holding references:
///   CareLog.Add("Wire", "Wired branch toward 240° — training toward its slot", nodeId);
///
/// Capped at MaxEntries (oldest dropped). Persisted through SaveData.careLog —
/// TreeSkeleton snapshots on save and restores on load. The Stats overlay shows
/// the most recent entries in the CareLogPanel.
/// </summary>
[Serializable]
public class CareLogEntry
{
    public int    year;
    public int    month;
    public int    day;
    public string action;   // short tag: Trim / Wire / Unwire / Pinch / Paste / Defoliate / Pot / Season…
    public int    nodeId;   // -1 when not node-specific
    public string reason;   // templated human-readable sentence
}

public static class CareLog
{
    public const int MaxEntries = 200;

    static readonly List<CareLogEntry> entries = new List<CareLogEntry>();

    public static IReadOnlyList<CareLogEntry> Entries => entries;

    /// <summary>Fired after every Add/Restore/Clear so UI can refresh lazily.</summary>
    public static event Action OnChanged;

    public static void Add(string action, string reason, int nodeId = -1)
    {
        entries.Add(new CareLogEntry
        {
            year   = GameManager.year,
            month  = GameManager.month,
            day    = GameManager.day,
            action = action,
            nodeId = nodeId,
            reason = reason,
        });
        if (entries.Count > MaxEntries)
            entries.RemoveRange(0, entries.Count - MaxEntries);
        OnChanged?.Invoke();
    }

    public static void Clear()
    {
        entries.Clear();
        OnChanged?.Invoke();
    }

    /// <summary>Copy for serialization into SaveData.</summary>
    public static List<CareLogEntry> Snapshot() => new List<CareLogEntry>(entries);

    /// <summary>Replace contents from a loaded save (null-safe for old saves).</summary>
    public static void Restore(List<CareLogEntry> saved)
    {
        entries.Clear();
        if (saved != null)
        {
            entries.AddRange(saved);
            if (entries.Count > MaxEntries)
                entries.RemoveRange(0, entries.Count - MaxEntries);
        }
        OnChanged?.Invoke();
    }

    static readonly string[] MonthAbbrev =
    { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    /// <summary>"2032 Jun — reason" line used by the UI panel.</summary>
    public static string Format(CareLogEntry e)
    {
        string m = e.month >= 1 && e.month <= 12 ? MonthAbbrev[e.month] : e.month.ToString();
        return $"{e.year} {m} — {e.reason}";
    }
}
