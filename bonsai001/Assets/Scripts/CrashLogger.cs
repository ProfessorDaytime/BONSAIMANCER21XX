using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes every Unity log message to a plain-text file under
/// <c>persistentDataPath/logs/</c>, one file per session (PLAN "Release Prep" R5).
///
/// <para><b>Why:</b> a tester who hits a bug has nothing to send. Unity keeps its own
/// <c>Player.log</c>, but it's one rolling file in an obscure platform-specific folder, it's
/// overwritten by the next launch, and asking a tester to find it is asking too much. This
/// writes a per-session file next to their saves and gives the pause menu a button that opens
/// the folder, so "zip me that log" is a reasonable thing to ask. A remote crash reporter is
/// the right answer eventually; this is the version that costs nothing and works offline.</para>
///
/// <para>Installs itself — no scene wiring, so it is already listening before any scene's
/// <c>Awake</c> runs and catches errors during startup, which is when the interesting ones
/// happen. Runs in every build, debug or release: this is a player-facing diagnostic, not a
/// developer surface, so it is deliberately NOT behind <see cref="BonsaiDebug"/>.</para>
/// </summary>
public class CrashLogger : MonoBehaviour
{
    // ── Tuning ────────────────────────────────────────────────────────────────

    /// <summary>Session logs to keep. Older ones are deleted at startup so the folder can't grow
    /// without bound on a machine that launches the game hundreds of times.</summary>
    const int KeepSessions = 10;

    /// <summary>Lines buffered before a flush. Warnings and errors bypass this and flush at once,
    /// because the whole point is to survive the crash that follows them.</summary>
    const int FlushEveryLines = 32;

    /// <summary>Seconds between time-based flushes, so a quiet session still lands on disk.</summary>
    const float FlushInterval = 5f;

    /// <summary>Cap on one session file. A log loop (an error thrown every frame from Update) would
    /// otherwise fill the disk; past this the file is closed with a final note.</summary>
    const long MaxBytes = 8L * 1024 * 1024;

    /// <summary>Hard ceiling. Past <see cref="MaxBytes"/> only the lines that diagnose a broken
    /// session are still written — errors, [Focus], [Input] (2026-10-29): this game logs heavily,
    /// and a long session could otherwise hit the cap before the moment that matters.</summary>
    const long HardMaxBytes = 12L * 1024 * 1024;
    bool essentialOnly;

    // ── State ─────────────────────────────────────────────────────────────────

    static CrashLogger instance;

    /// <summary>Folder holding the session logs. Sits beside <c>saves/</c> so one "open folder"
    /// button gets a tester to everything worth sending.</summary>
    public static string LogDir => Path.Combine(Application.persistentDataPath, "logs");

    /// <summary>Full path of this session's log file, or null if logging failed to start.</summary>
    public static string CurrentLogPath { get; private set; }

    readonly StringBuilder buffer = new StringBuilder(8 * 1024);
    float nextFlushTime;
    long  bytesWritten;
    bool  stopped;

    // ── Install ───────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (instance != null) return;

        // No HideAndDontSave: an object with that flag can outlive a play-mode exit in the
        // editor and leak a second logger into the next session. Visible in the
        // DontDestroyOnLoad scene is also simply easier to debug.
        var go = new GameObject("~CrashLogger");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<CrashLogger>();
    }

    void Awake()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            PruneOldSessions();

            CurrentLogPath = Path.Combine(
                LogDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            // Header first: without the build and platform, a log from a tester is guesswork.
            buffer.AppendLine($"Bonsaimancer session log");
            buffer.AppendLine($"  started      {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local)");
            buffer.AppendLine($"  version      {Application.version}");
            buffer.AppendLine($"  unity        {Application.unityVersion}");
            buffer.AppendLine($"  platform     {Application.platform}");
            buffer.AppendLine($"  debug build  {Debug.isDebugBuild}  (BonsaiDebug.Enabled={BonsaiDebug.Enabled})");
            buffer.AppendLine($"  device       {SystemInfo.operatingSystem}");
            buffer.AppendLine($"  cpu          {SystemInfo.processorType} x{SystemInfo.processorCount}");
            buffer.AppendLine($"  gpu          {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            buffer.AppendLine($"  ram          {SystemInfo.systemMemorySize} MB");
            buffer.AppendLine($"  screen       {Screen.width}x{Screen.height} @{Screen.currentResolution.refreshRateRatio.value:F0}Hz {Screen.fullScreenMode}");
            buffer.AppendLine($"  data path    {Application.persistentDataPath}");
            buffer.AppendLine(new string('-', 78));
            Flush();
        }
        catch (Exception e)
        {
            // Never let the logger be the thing that breaks the game.
            Debug.LogWarning($"[CrashLogger] Couldn't open a log file: {e.Message}");
            CurrentLogPath = null;
            stopped = true;
            return;
        }

        Application.logMessageReceived += OnLog;
        nextFlushTime = Time.realtimeSinceStartup + FlushInterval;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        Flush();
    }

    void OnApplicationQuit()
    {
        buffer.AppendLine($"[{DateTime.Now:HH:mm:ss}] --- session ended normally ---");
        Flush();
    }

    // ── Capture ───────────────────────────────────────────────────────────────

    // Main-thread only (Application.logMessageReceived, not the ...Threaded variant), so the
    // buffer needs no lock. Logs raised on a worker thread won't appear — acceptable: this
    // project's threaded work doesn't log.
    void OnLog(string message, string stackTrace, LogType type)
    {
        if (stopped) return;

        bool severe = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        if (essentialOnly && !severe && !message.StartsWith("[Focus]") && !message.StartsWith("[Input]"))
            return;

        buffer.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ")
              .Append(Abbrev(type)).Append(' ').AppendLine(message);

        // Stack traces only for the severe ones. A trace on every Debug.Log would bury the
        // signal and blow the size cap in minutes.
        if (severe && !string.IsNullOrEmpty(stackTrace))
            buffer.AppendLine(stackTrace.TrimEnd());

        if (severe || buffer.Length > FlushEveryLines * 120)
            Flush();
    }

    void Update()
    {
        if (stopped || buffer.Length == 0) return;
        if (Time.realtimeSinceStartup < nextFlushTime) return;
        Flush();
        nextFlushTime = Time.realtimeSinceStartup + FlushInterval;
    }

    void Flush()
    {
        if (stopped || buffer.Length == 0 || string.IsNullOrEmpty(CurrentLogPath)) return;

        try
        {
            string text = buffer.ToString();
            buffer.Length = 0;
            File.AppendAllText(CurrentLogPath, text);

            bytesWritten += Encoding.UTF8.GetByteCount(text);
            if (bytesWritten > MaxBytes && !essentialOnly)
            {
                essentialOnly = true;
                File.AppendAllText(CurrentLogPath,
                    $"\n[{DateTime.Now:HH:mm:ss}] --- log size cap ({MaxBytes / 1024 / 1024} MB) reached; " +
                    "from here only errors, [Focus] and [Input] lines are written ---\n");
            }
            if (bytesWritten > HardMaxBytes)
            {
                File.AppendAllText(CurrentLogPath,
                    $"\n[{DateTime.Now:HH:mm:ss}] --- hard log cap ({HardMaxBytes / 1024 / 1024} MB) " +
                    "reached; logging stopped for this session ---\n");
                Application.logMessageReceived -= OnLog;
                stopped = true;
            }
        }
        catch (Exception e)
        {
            // Disk full, file locked, permissions — stop trying rather than log about logging.
            Application.logMessageReceived -= OnLog;
            stopped = true;
            buffer.Length = 0;
            Debug.LogWarning($"[CrashLogger] Write failed, logging stopped: {e.Message}");
        }
    }

    static string Abbrev(LogType t)
    {
        switch (t)
        {
            case LogType.Error:     return "ERROR  ";
            case LogType.Assert:    return "ASSERT ";
            case LogType.Exception: return "EXCEPT ";
            case LogType.Warning:   return "warn   ";
            default:                return "info   ";
        }
    }

    // ── Housekeeping ──────────────────────────────────────────────────────────

    static void PruneOldSessions()
    {
        try
        {
            var files = new List<FileInfo>();
            foreach (var f in new DirectoryInfo(LogDir).GetFiles("session_*.log")) files.Add(f);
            if (files.Count < KeepSessions) return;

            files.Sort((a, b) => b.Name.CompareTo(a.Name));   // name sorts chronologically
            for (int i = KeepSessions - 1; i < files.Count; i++)
            {
                try { files[i].Delete(); } catch { /* in use — leave it */ }
            }
        }
        catch { /* folder unreadable; the new session's own file is what matters */ }
    }

    // ── For the UI ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the log folder in the OS file browser, after flushing so the current session's file
    /// is complete and not just buffered. Wired to the pause menu's "Open Log Folder" button.
    /// </summary>
    public static void OpenLogFolder()
    {
        instance?.Flush();
        try
        {
            Directory.CreateDirectory(LogDir);
            Application.OpenURL("file:///" + LogDir.Replace('\\', '/'));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CrashLogger] Couldn't open {LogDir}: {e.Message}");
        }
    }
}
