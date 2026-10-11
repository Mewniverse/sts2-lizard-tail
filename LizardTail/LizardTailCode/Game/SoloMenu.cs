using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The game's main menu, which is where the solo save lives: with a solo run in progress it shows
/// Continue and Abandon Run in place of Singleplayer, and nowhere else offers either. The hooks here
/// put a picker for solo runs on it.
/// </summary>
public static class SoloMenu
{
    /// <summary>The column of text buttons (Continue, Abandon Run, Singleplayer, Multiplayer, ...).</summary>
    public static Control? TextButtons(NMainMenu menu) => menu.GetNodeOrNull<Control>("MainMenuTextButtons");
}

/// <summary>The main menu as the solo picker's host.</summary>
public sealed class SoloHost(NMainMenu menu) : IPickerHost
{
    public SaveKind Kind => SaveKind.Solo;

    public Control Menu => menu;

    public void Add(SlotPicker picker)
    {
        menu.AddChild(picker);
        // Just above the text buttons, so the submenus, the blur behind them and the patch notes all
        // cover it as they cover the buttons.
        if (SoloMenu.TextButtons(menu) is { } buttons && buttons.GetParent() == menu)
        {
            menu.MoveChild(picker, buttons.GetIndex() + 1);
        }
    }

    /// <summary>The game hides its text buttons while a submenu is open; the patch notes draw over them.</summary>
    public bool Shown => SoloMenu.TextButtons(menu)?.Visible != false && menu.PatchNotesScreen?.IsOpen != true;

    public bool StartsClosed => true;

    public string? WhyNotHere()
    {
        if (NGame.Instance?.MainMenu != menu || menu.SubmenuStack?.SubmenusOpen != false || menu.PatchNotesScreen?.IsOpen == true)
        {
            return "Only available on the main menu.";
        }
        return null;
    }

    /// <summary>
    /// The game's own redraw: it loads the solo save again, which Continue then uses, and shows Continue
    /// and Abandon Run or Singleplayer to match. Without it Continue would start the run read before the swap.
    /// </summary>
    public void RefreshButtons() => menu.RefreshButtons();

    /// <summary>The main menu remembers only which of its own text buttons was last pressed.</summary>
    public Control? LastFocused
    {
        get => null;
        set { }
    }

    public Control? DefaultFocus
    {
        get
        {
            try
            {
                return menu.DefaultFocusedControl;
            }
            catch (InvalidOperationException)
            {
                return null; // no text button is both shown and enabled
            }
        }
    }

    public IReadOnlyList<Control> EntryButtons =>
        SoloMenu.TextButtons(menu)?.GetChildren().OfType<NMainMenuTextButton>().Cast<Control>().ToList() ?? [];

    public Control? ExitButton => DefaultFocus;

    public Control? Tiles => null;
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class AttachSoloPickerPatch
{
    private static void Postfix(NMainMenu __instance)
    {
        try
        {
            SlotPicker.AttachTo(new SoloHost(__instance));
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not add the save picker to the main menu: {ex}");
        }
    }
}

/// <summary>The game re-checks its solo save after Abandon Run and after the timeline closes; the picker follows.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.RefreshButtons))]
public static class RefreshOnMainMenuButtonsPatch
{
    private static void Postfix(NMainMenu __instance) => SlotPicker.Of(__instance)?.Refresh();
}

/// <summary>
/// Keeps a copy of a solo run the game is about to delete (Abandon Run on the main menu or in a run),
/// so it can be picked up again. The game also comes through here when a run is won or lost; those are
/// not copied.
/// </summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
public static class SnapshotBeforeSoloDeletePatch
{
    private static void Prefix() => SlotActions.SnapshotBeforeGameDeletes(SaveKind.Solo);
}
