using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>The keyboard actions a player can rebind (PLAN "Release Prep" R2).</summary>
public enum BindAction
{
    PrevTool, NextTool, PutToolAway, SlowDown, SpeedUp, UndoTrim, CinematicCamera, SkipAnimation,
    ResetCamera,
}

/// <summary>
/// Rebindable keyboard controls (PLAN "Release Prep" R2, 2026-10-27).
///
/// <para>The game's keyboard input is a handful of direct key reads, not an Input System actions
/// asset, so this is a lookup table rather than an actions-asset rebinding: each
/// <see cref="BindAction"/> maps to one <see cref="Key"/>, stored as <c>set.key.&lt;Action&gt;</c>
/// in PlayerPrefs (so Settings ▸ Reset to Defaults clears them along with everything else).
/// Call sites ask <see cref="Pressed"/> / <see cref="Held"/> instead of naming a key.</para>
///
/// <para>Not rebindable, deliberately: Esc (the way back out of every menu, including this one),
/// F12 (screenshot), the mouse, and the development-only F1 / digit / F keys. Undo keeps its Ctrl modifier; only the
/// key that goes with Ctrl is rebound.</para>
/// </summary>
public static class KeyBindings
{
    const string PREFIX = "set.key.";

    static readonly Dictionary<BindAction, Key> Defaults = new Dictionary<BindAction, Key>
    {
        { BindAction.PrevTool,        Key.A          },
        { BindAction.NextTool,        Key.D          },
        { BindAction.PutToolAway,     Key.S          },
        { BindAction.SlowDown,        Key.LeftArrow  },
        { BindAction.SpeedUp,         Key.RightArrow },
        { BindAction.UndoTrim,        Key.Z          },
        { BindAction.CinematicCamera, Key.C          },
        { BindAction.SkipAnimation,   Key.Enter      },
        { BindAction.ResetCamera,     Key.R          },
    };

    /// <summary>Keys a binding may never take: they already mean something fixed.</summary>
    static readonly HashSet<Key> Reserved = new HashSet<Key>
    {
        Key.Escape, Key.F1, Key.F12, Key.LeftCtrl, Key.RightCtrl, Key.LeftMeta, Key.RightMeta,
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
        Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
    };

    /// <summary>
    /// True while Settings is waiting for the player to press a key for a binding. The Esc
    /// handlers check this so that Esc cancels the capture instead of closing the menu.
    /// </summary>
    public static bool Capturing { get; set; }

    public static IEnumerable<BindAction> All => Defaults.Keys;

    public static Key Get(BindAction a) =>
        (Key)PlayerPrefs.GetInt(PREFIX + a, (int)Defaults[a]);

    /// <summary>
    /// Binds <paramref name="key"/> to <paramref name="a"/>. If another action already had that
    /// key, the two swap, so no action is ever left unbound or doubled up. Returns false for a
    /// reserved key.
    /// </summary>
    public static bool Set(BindAction a, Key key)
    {
        if (Reserved.Contains(key) || key == Key.None) return false;

        Key old = Get(a);
        foreach (var other in All)
            if (other != a && Get(other) == key)
                PlayerPrefs.SetInt(PREFIX + other, (int)old);

        PlayerPrefs.SetInt(PREFIX + a, (int)key);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>The PlayerPrefs keys used, for Reset to Defaults.</summary>
    public static IEnumerable<string> PrefKeys()
    {
        foreach (var a in All) yield return PREFIX + a;
    }

    public static bool Pressed(BindAction a) => Control(a)?.wasPressedThisFrame ?? false;
    public static bool Held(BindAction a)    => Control(a)?.isPressed ?? false;

    static KeyControl Control(BindAction a)
    {
        var kb = Keyboard.current;
        if (kb == null) return null;
        var key = Get(a);
        return key == Key.None ? null : kb[key];
    }

    /// <summary>The key as the player's keyboard labels it ("Z", "Left Arrow", "Enter").</summary>
    public static string KeyName(Key key)
    {
        var kb = Keyboard.current;
        string name = kb != null && key != Key.None ? kb[key].displayName : null;
        return string.IsNullOrEmpty(name) ? key.ToString() : name;
    }

    /// <summary>The binding as shown in Settings and hints, including Undo's Ctrl.</summary>
    public static string Display(BindAction a) =>
        a == BindAction.UndoTrim ? "Ctrl+" + KeyName(Get(a)) : KeyName(Get(a));

    public static string Describe(BindAction a) => a switch
    {
        BindAction.PrevTool        => "Previous tool",
        BindAction.NextTool        => "Next tool",
        BindAction.PutToolAway     => "Put the tool away",
        BindAction.SlowDown        => "Slow down time (hold)",
        BindAction.SpeedUp         => "Speed up time (hold)",
        BindAction.UndoTrim        => "Undo a trim (5 second window)",
        BindAction.CinematicCamera => "Cinematic camera on / off",
        BindAction.SkipAnimation   => "Skip the wire animation",
        BindAction.ResetCamera     => "Reset the camera",
        _                          => a.ToString(),
    };

    /// <summary>The first key pressed this frame, or None. Used while capturing a new binding.</summary>
    public static Key KeyPressedThisFrame()
    {
        var kb = Keyboard.current;
        if (kb == null) return Key.None;
        foreach (var k in kb.allKeys)
            if (k != null && k.wasPressedThisFrame) return k.keyCode;
        return Key.None;
    }
}
