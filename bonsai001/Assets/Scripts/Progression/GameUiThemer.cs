using UnityEngine;

/// <summary>
/// DEPRECATED — superseded by the in-game UI theming built into buttonClicker (`ThemeRoot` /
/// `ThemePanel` / `ThemeTooltip` + `UiThemePainter`), which paints the game UI reliably from
/// `resolvedStyle`. This overlay-scanner couldn't beat the inline/code-set colours and is now a
/// no-op. Safe to delete the file and remove the component from the scene.
///
/// Kept as an empty component so any existing scene reference doesn't become a missing script.
/// See PLAN: "Themeable In-Game UI Rebuild".
/// </summary>
[System.Obsolete("Superseded by buttonClicker's UiThemePainter-based theming. Remove the component.")]
public class GameUiThemer : MonoBehaviour
{
    // Intentionally empty.
}
