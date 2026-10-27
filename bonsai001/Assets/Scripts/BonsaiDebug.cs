using UnityEngine;

/// <summary>
/// The project's single developer-surface switch (PLAN "Release Prep" R5).
///
/// <para>Everything that exists for development and must not reach a player reads
/// <see cref="Enabled"/>: the F1 state readout, the growth/ishitsuki/debug tabs of the pause
/// menu, the GL gizmo overlays, the tipping test harness, and the verbose per-action logs.
/// One switch, so a release build can't ship with one of them accidentally left on.</para>
///
/// <para><b>Why the gate is needed even though the gizmos default to off:</b> they're gated by
/// serialized inspector bools, which live in the scene file. Tick one to debug something,
/// forget to untick it, and it ships. Gating the draw call on this flag as well means a stray
/// ticked box can only ever affect a development build.</para>
///
/// <para><b>When it's on:</b> in the editor, in a Unity "Development Build", or in any build
/// compiled with the <c>BONSAI_DEBUG</c> define (Project Settings → Player → Scripting Define
/// Symbols). A plain release build gets <c>false</c>.</para>
///
/// <para><b>Why <c>static readonly</c> and not <c>const</c>:</b> a <c>const</c> folds at every
/// call site, so every <c>if (BonsaiDebug.Enabled)</c> body becomes provably unreachable in one
/// configuration or the other and the compiler reports CS0162 on all of them — dozens of
/// warnings that would train everyone to ignore the warning list. A <c>static readonly</c> costs
/// one field read, which the JIT folds anyway, and keeps the build warning-clean. Guarded code
/// therefore still ships in the binary; it just never runs, which is the point. Argument
/// evaluation is still skipped, so interpolated strings inside a guard cost nothing.</para>
/// </summary>
public static class BonsaiDebug
{
#if BONSAI_DEBUG || UNITY_EDITOR || DEVELOPMENT_BUILD
    public static readonly bool Enabled = true;
#else
    public static readonly bool Enabled = false;
#endif

    /// <summary>
    /// A log that costs nothing in a release build. Prefer this over a bare
    /// <c>Debug.Log($"…")</c> on any hot path: the argument is only built when the guard is
    /// true, so the string interpolation is skipped too.
    /// </summary>
    public static void Log(string message)
    {
        if (Enabled) Debug.Log(message);
    }
}
