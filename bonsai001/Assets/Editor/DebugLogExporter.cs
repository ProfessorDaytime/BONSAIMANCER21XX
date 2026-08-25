using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Debug menu for getting console output out of Unity and into a file that can be read, diffed or
/// pasted somewhere. Writes to <c>CurrentLogs.txt</c> in the PROJECT ROOT (beside Assets/), not
/// inside Assets/ — a .txt there would be imported as a TextAsset and churn the AssetDatabase on
/// every write.
///
/// Reads the console through UnityEditor's internal LogEntries API by reflection. That is the only
/// way to get the console's actual contents, but it is internal and can shift between Unity
/// versions, so every step is guarded and there is a fallback that copies the tail of Editor.log
/// instead. The fallback is noisier (it includes engine chatter the console filters out) but it
/// always works.
/// </summary>
public static class DebugLogExporter
{
    // Docs/CurrentLogs.txt — the file this project already used for shared logs. Deliberately not
    // inside Assets/ (a .txt there imports as a TextAsset and churns the AssetDatabase every write)
    // and deliberately not the project root, where an earlier version of this wrote a SECOND
    // CurrentLogs.txt that quietly shadowed the real one (2026-08-24).
    const string OutputFile = "CurrentLogs.txt";
    const string OutputDir  = "Docs";
    const int    FallbackTailLines = 4000;

    static string OutputPath
    {
        get
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir  = Path.Combine(root, OutputDir);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, OutputFile);
        }
    }

    [MenuItem("Debug/Copy Console → CurrentLogs.txt %#l")]
    public static void CopyConsole() => Export(errorsOnly: false);

    [MenuItem("Debug/Copy Console → CurrentLogs.txt (Errors + Warnings only)")]
    public static void CopyConsoleErrors() => Export(errorsOnly: true);

    [MenuItem("Debug/Open CurrentLogs.txt")]
    public static void OpenLog()
    {
        if (!File.Exists(OutputPath)) { Debug.LogWarning($"[DebugLog] No file yet at {OutputPath}"); return; }
        EditorUtility.RevealInFinder(OutputPath);
    }

    [MenuItem("Debug/Clear Console")]
    public static void ClearConsole()
    {
        var t = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
        t?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
    }

    static void Export(bool errorsOnly)
    {
        string text = ReadConsole(errorsOnly, out int count, out string how);
        if (text == null)
        {
            text = ReadEditorLogTail(out count);
            how  = $"Editor.log tail ({FallbackTailLines} lines) — console reflection unavailable";
        }

        var header = new StringBuilder();
        header.AppendLine($"# CurrentLogs.txt — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        header.AppendLine($"# source: {how}");
        header.AppendLine($"# entries: {count}{(errorsOnly ? "  (errors + warnings only)" : "")}");
        header.AppendLine();

        try
        {
            File.WriteAllText(OutputPath, header + text, Encoding.UTF8);
            Debug.Log($"[DebugLog] Wrote {count} entries → {OutputPath}  ({how})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DebugLog] Could not write {OutputPath}: {e.Message}");
        }
    }

    /// <summary>Pulls the console's entries via the internal LogEntries API. Returns null if the
    /// reflection does not line up with this Unity version, so the caller can fall back.</summary>
    static string ReadConsole(bool errorsOnly, out int count, out string how)
    {
        count = 0;
        how   = "Unity console";

        try
        {
            var asm          = typeof(Editor).Assembly;
            var entriesType  = asm.GetType("UnityEditor.LogEntries");
            var entryType    = asm.GetType("UnityEditor.LogEntry");
            if (entriesType == null || entryType == null) return null;

            var startMethod  = entriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
            var endMethod    = entriesType.GetMethod("EndGettingEntries",   BindingFlags.Static | BindingFlags.Public);
            var getEntry     = entriesType.GetMethod("GetEntryInternal",    BindingFlags.Static | BindingFlags.Public);
            var getCount     = entriesType.GetMethod("GetCount",            BindingFlags.Static | BindingFlags.Public);
            if (startMethod == null || endMethod == null || getEntry == null) return null;

            // Message text lives in `message` on newer versions and `condition` on older ones.
            var msgField  = entryType.GetField("message") ?? entryType.GetField("condition");
            var modeField = entryType.GetField("mode");
            if (msgField == null) return null;

            var sb = new StringBuilder();
            startMethod.Invoke(null, null);
            try
            {
                int total = getCount != null ? (int)getCount.Invoke(null, null) : 0;
                var entry = Activator.CreateInstance(entryType);

                for (int i = 0; i < total; i++)
                {
                    getEntry.Invoke(null, new object[] { i, entry });
                    string msg = msgField.GetValue(entry) as string;
                    if (string.IsNullOrEmpty(msg)) continue;

                    if (errorsOnly && modeField != null)
                    {
                        int mode = (int)modeField.GetValue(entry);
                        if (!IsErrorOrWarning(mode)) continue;
                    }

                    sb.AppendLine(msg.TrimEnd());
                    count++;
                }
            }
            finally { endMethod.Invoke(null, null); }

            return sb.ToString();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DebugLog] Console reflection failed ({e.GetType().Name}); using Editor.log instead.");
            return null;
        }
    }

    // LogEntry.mode is a bitfield; these bits cover the error and warning classes.
    static bool IsErrorOrWarning(int mode)
    {
        const int Error            = 1 << 0;
        const int Assert           = 1 << 1;
        const int Warning          = 1 << 2;
        const int ScriptingError   = 1 << 8;
        const int ScriptingWarning = 1 << 9;
        const int ScriptingAssert  = 1 << 10;
        const int ScriptCompileErr = 1 << 11;
        const int ScriptCompileWrn = 1 << 12;
        const int ScriptingExcept  = 1 << 17;
        return (mode & (Error | Assert | Warning | ScriptingError | ScriptingWarning |
                        ScriptingAssert | ScriptCompileErr | ScriptCompileWrn | ScriptingExcept)) != 0;
    }

    /// <summary>Fallback: the tail of Editor.log. Opened share-all because Unity holds it open.</summary>
    static string ReadEditorLogTail(out int count)
    {
        count = 0;
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Unity", "Editor", "Editor.log");

        if (!File.Exists(path)) return "(Editor.log not found)";

        try
        {
            var lines = new System.Collections.Generic.Queue<string>(FallbackTailLines);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (lines.Count == FallbackTailLines) lines.Dequeue();
                    lines.Enqueue(line);
                }
            }
            count = lines.Count;
            return string.Join("\n", lines);
        }
        catch (Exception e) { return $"(could not read Editor.log: {e.Message})"; }
    }
}
