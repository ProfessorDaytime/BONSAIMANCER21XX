using UnityEngine;

/// <summary>
/// DEPRECATED — superseded by Settings ▸ Display (PLAN "Release Prep" R2).
///
/// <para>This was a raw <c>OnGUI</c> dialog that asked "Play fullscreen?" once at startup and then
/// destroyed itself. Window mode is now a real, persisted setting owned by
/// <see cref="GameSettings"/> and applied before the first scene loads, so the prompt is both
/// redundant and the most amateur-looking thing a new player met.</para>
///
/// <para>Kept as an empty self-removing component rather than deleted, because it is still
/// attached in <c>Game001.unity</c> and deleting the file would turn that into a missing-script
/// reference. Same approach as <c>GameUiThemer</c>. <b>Safe to delete the file once the component
/// has been removed from the scene.</b></para>
/// </summary>
[System.Obsolete("Superseded by Settings ▸ Display (GameSettings.Mode). Remove the component.")]
public class FullscreenPrompt : MonoBehaviour
{
    void Awake()
    {
        if (BonsaiDebug.Enabled)
            Debug.Log("[FullscreenPrompt] Deprecated component removed itself — window mode is now " +
                      "Settings ▸ Display. Delete it from the scene at your convenience.");

        // Remove just this component; the GameObject may carry others.
        Destroy(this);
    }
}
