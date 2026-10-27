using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Async scene load with a real progress bar and a fade (PLAN "Release Prep" R1).
///
/// <para>Replaces the blind <c>yield return new WaitForSeconds(1.5f)</c> the old main menu used
/// before calling <c>LoadSceneAsync</c> — a fixed wait that was simultaneously too long on a
/// fast machine and too short on a slow one, with no indication anything was happening.</para>
///
/// <para>Builds its own full-screen overlay into the panel it is handed, so it needs no scene
/// setup and no prefab.</para>
/// </summary>
public class LoadingScreen
{
    const float FADE_IN  = 0.25f;
    const float FADE_OUT = 0.35f;

    /// <summary>Unity reports progress in [0, 0.9] while loading and parks there until
    /// activation is allowed, so the bar is rescaled to make 0.9 read as full.</summary>
    const float UNITY_PROGRESS_CEILING = 0.9f;

    VisualElement root;
    VisualElement barFill;
    Label         statusLabel;

    /// <summary>
    /// Fades in, loads <paramref name="sceneName"/> asynchronously while driving a progress bar,
    /// then activates it. Run as a coroutine from any MonoBehaviour that survives the load
    /// (the overlay belongs to the outgoing scene's panel, so it dies with the scene — which is
    /// exactly when it should).
    /// </summary>
    public IEnumerator Run(VisualElement parent, string sceneName, string flavourText = null)
    {
        Build(parent, flavourText);

        // Fade in over the menu before the load starts, so the transition reads as deliberate
        // rather than as a freeze.
        float t = 0f;
        while (t < FADE_IN)
        {
            t += Time.unscaledDeltaTime;
            root.style.opacity = Mathf.Clamp01(t / FADE_IN);
            yield return null;
        }
        root.style.opacity = 1f;

        var op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (op.progress < UNITY_PROGRESS_CEILING)
        {
            SetProgress(op.progress / UNITY_PROGRESS_CEILING);
            yield return null;
        }

        SetProgress(1f);
        if (statusLabel != null) statusLabel.text = "Ready";

        // One frame at 100% so the bar doesn't visibly skip the end.
        yield return null;

        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;
    }

    void SetProgress(float p01)
    {
        if (barFill == null) return;
        barFill.style.width = new StyleLength(new Length(Mathf.Clamp01(p01) * 100f, LengthUnit.Percent));
    }

    void Build(VisualElement parent, string flavourText)
    {
        var th = UiTheme.Current;

        root = new VisualElement();
        root.style.position = Position.Absolute;
        root.style.top = root.style.left = root.style.right = root.style.bottom = 0;
        root.style.backgroundColor = new StyleColor(new Color(th.panelBg.r, th.panelBg.g, th.panelBg.b, 1f));
        root.style.alignItems      = Align.Center;
        root.style.justifyContent  = Justify.Center;
        root.style.opacity         = 0f;
        parent.Add(root);

        var title = new Label { text = "BONSAIMANCER" };
        title.style.fontSize = 28;
        title.style.color    = new StyleColor(th.accent);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 28;
        root.Add(title);

        statusLabel = new Label { text = string.IsNullOrEmpty(flavourText) ? "Loading…" : flavourText };
        statusLabel.style.fontSize = 13;
        statusLabel.style.color    = new StyleColor(th.textSub);
        statusLabel.style.marginBottom = 10;
        root.Add(statusLabel);

        var barBg = new VisualElement();
        barBg.style.width  = 260;
        barBg.style.height = 6;
        barBg.style.backgroundColor = new StyleColor(th.cardBg);
        barBg.style.borderTopLeftRadius = barBg.style.borderTopRightRadius =
        barBg.style.borderBottomLeftRadius = barBg.style.borderBottomRightRadius = 3;
        root.Add(barBg);

        barFill = new VisualElement();
        barFill.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
        barFill.style.width  = new StyleLength(new Length(0f, LengthUnit.Percent));
        barFill.style.backgroundColor = new StyleColor(th.accent);
        barFill.style.borderTopLeftRadius = barFill.style.borderTopRightRadius =
        barFill.style.borderBottomLeftRadius = barFill.style.borderBottomRightRadius = 3;
        barBg.Add(barFill);
    }

    /// <summary>Fades the overlay back out, for a load that was cancelled before it began.</summary>
    public IEnumerator FadeOutAndRemove()
    {
        if (root == null) yield break;

        float t = 0f;
        while (t < FADE_OUT)
        {
            t += Time.unscaledDeltaTime;
            root.style.opacity = 1f - Mathf.Clamp01(t / FADE_OUT);
            yield return null;
        }

        root.RemoveFromHierarchy();
        root = null;
    }
}
