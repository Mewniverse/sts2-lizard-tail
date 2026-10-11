using Godot;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The game menu a picker sits on, and what the picker needs from it: the multiplayer menu for
/// multiplayer runs (<see cref="MultiplayerHost"/>), the main menu for solo ones (<see cref="SoloHost"/>).
/// </summary>
public interface IPickerHost
{
    /// <summary>Which of the game's saves this menu continues.</summary>
    SaveKind Kind { get; }

    /// <summary>The menu's node, which the picker is a child of.</summary>
    Control Menu { get; }

    /// <summary>Adds the picker to the menu, beneath whatever the menu draws over its own buttons.</summary>
    void Add(SlotPicker picker);

    /// <summary>False while the menu's own buttons are hidden behind another screen; the picker hides with them.</summary>
    bool Shown { get; }

    /// <summary>
    /// True when the panel starts as its title and a Show button. The main menu is the game's front
    /// page, so the solo panel stays out of the way until asked for.
    /// </summary>
    bool StartsClosed { get; }

    /// <summary>Why a swap is unsafe on this menu right now, or null. Asked after the checks every menu shares.</summary>
    string? WhyNotHere();

    /// <summary>After the save changed under the menu: has the game redraw its own buttons to match.</summary>
    void RefreshButtons();

    /// <summary>
    /// What the menu refocuses when it is shown again, where it keeps such a thing. It can hold a picker
    /// button since rebuilt, so the picker repoints it.
    /// </summary>
    Control? LastFocused { get; set; }

    /// <summary>Where the menu itself would put focus.</summary>
    Control? DefaultFocus { get; }

    /// <summary>The game's buttons a controller moves right from, into the picker.</summary>
    IReadOnlyList<Control> EntryButtons { get; }

    /// <summary>The game's button a controller lands on moving left, out of the picker.</summary>
    Control? ExitButton { get; }

    /// <summary>The menu's row of big tiles, when it has one that must make room for the picker.</summary>
    Control? Tiles { get; }
}
