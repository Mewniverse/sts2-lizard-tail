using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Migrations;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The game's multiplayer menu (Host / Load / Join / Abandon), and the hooks that put the picker on it.
/// </summary>
public static class MultiplayerMenu
{
    private static readonly AccessTools.FieldRef<NMultiplayerSubmenu, Control?> LoadingOverlay =
        AccessTools.FieldRefAccess<NMultiplayerSubmenu, Control?>("_loadingOverlay");
    private static readonly AccessTools.FieldRef<NMultiplayerSubmenu, NSubmenuButton?> LoadButton =
        AccessTools.FieldRefAccess<NMultiplayerSubmenu, NSubmenuButton?>("_loadButton");
    private static readonly AccessTools.FieldRef<NMultiplayerSubmenu, NSubmenuButton?> JoinButtonField =
        AccessTools.FieldRefAccess<NMultiplayerSubmenu, NSubmenuButton?>("_joinButton");
    private static readonly AccessTools.FieldRef<NSubmenu, Control?> LastFocusedField =
        AccessTools.FieldRefAccess<NSubmenu, Control?>("_lastFocusedControl");
    private static readonly MethodInfo? UpdateButtonsMethod =
        AccessTools.Method(typeof(NMultiplayerSubmenu), "UpdateButtons");

    /// <summary>True once Load or Host has been pressed and the game is starting a lobby.</summary>
    public static bool IsLoading(NMultiplayerSubmenu submenu) => LoadingOverlay(submenu)?.Visible == true;

    /// <summary>
    /// After the save changed under it: show Load/Abandon or Host to match, and re-enable Load (the
    /// game disables it after a broken save). Focus is left to the picker, which keeps it in place.
    /// </summary>
    public static void RefreshButtons(NMultiplayerSubmenu submenu)
    {
        UpdateButtonsMethod?.Invoke(submenu, null);
        if (SaveManager.Instance.HasMultiplayerRunSave)
        {
            LoadButton(submenu)?.Enable();
        }
    }

    /// <summary>The loading overlay, so the picker can sit beneath it.</summary>
    public static Control? Overlay(NMultiplayerSubmenu submenu) => LoadingOverlay(submenu);

    /// <summary>The row of big tiles (Host or Load, Abandon, Join).</summary>
    public static Control? ButtonContainer(NMultiplayerSubmenu submenu) => submenu.GetNodeOrNull<Control>("ButtonContainer");

    /// <summary>The Join tile, the one beside the picker, which the controller moves right from.</summary>
    public static Control? JoinButton(NMultiplayerSubmenu submenu) => JoinButtonField(submenu);

    /// <summary>
    /// What the game refocuses when the menu is shown again (backing out of a lobby screen). It holds
    /// whatever had focus when the menu was hidden, which can be a picker button since rebuilt.
    /// </summary>
    public static ref Control? LastFocused(NMultiplayerSubmenu submenu) => ref LastFocusedField(submenu);
}

/// <summary>The multiplayer menu as the multiplayer picker's host.</summary>
public sealed class MultiplayerHost(NMultiplayerSubmenu submenu) : IPickerHost
{
    public SaveKind Kind => SaveKind.Multiplayer;

    public Control Menu => submenu;

    public void Add(SlotPicker picker)
    {
        submenu.AddChild(picker);
        // Under the game's loading overlay, so it is covered while a lobby starts.
        if (MultiplayerMenu.Overlay(submenu) is { } overlay && overlay.GetParent() == submenu)
        {
            submenu.MoveChild(picker, overlay.GetIndex());
        }
    }

    public bool Shown => true; // the submenu itself is hidden behind the screens it opens

    public bool StartsClosed => false;

    public string? WhyNotHere()
    {
        if (NGame.Instance?.MainMenu?.SubmenuStack.Peek() != submenu)
        {
            return "Only available on the multiplayer menu.";
        }
        if (MultiplayerMenu.IsLoading(submenu))
        {
            return "Hosting has started.";
        }
        return null;
    }

    public void RefreshButtons() => MultiplayerMenu.RefreshButtons(submenu);

    public Control? LastFocused
    {
        get => MultiplayerMenu.LastFocused(submenu);
        set => MultiplayerMenu.LastFocused(submenu) = value;
    }

    public Control? DefaultFocus => submenu.DefaultFocusedControl;

    public IReadOnlyList<Control> EntryButtons => ExitButton is { } join ? [join] : [];

    public Control? ExitButton => MultiplayerMenu.JoinButton(submenu) is { } join && GodotObject.IsInstanceValid(join) ? join : null;

    public Control? Tiles => MultiplayerMenu.ButtonContainer(submenu);
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
public static class AttachPickerPatch
{
    private static void Postfix(NMultiplayerSubmenu __instance)
    {
        try
        {
            OldVersion.StandDown(__instance);
            SlotPicker.AttachTo(new MultiplayerHost(__instance));
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not add the save picker to the multiplayer menu: {ex}");
        }
    }
}

/// <summary>
/// Lizard Tail was called Save Slots before v0.2.0 (mod id SaveSlots). A copy of that version left in
/// the mods folder loads as well, and two pickers would manage the same saves. Removes its patches the
/// first time the multiplayer menu opens, and its picker from that first menu.
/// </summary>
public static class OldVersion
{
    private const string HarmonyId = "SaveSlots";
    private const string PickerName = "SaveSlotsPicker";
    private static bool _checked;

    public static void StandDown(NMultiplayerSubmenu submenu)
    {
        if (!_checked)
        {
            _checked = true;
            var harmony = new Harmony(LizardTailMod.ModId);
            var removed = 0;
            foreach (var method in Harmony.GetAllPatchedMethods().ToList())
            {
                if (Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true)
                {
                    harmony.Unpatch(method, HarmonyPatchType.All, HarmonyId);
                    removed++;
                }
            }
            if (removed > 0)
            {
                ModLog.Warn($"The old Save Slots version is also installed; switched off its {removed} patches. Delete mods/SaveSlots to remove it.");
            }
        }
        // Its picker may already be on this menu: its patch on the same method can run before or after this one.
        Callable.From(() => submenu.GetNodeOrNull(PickerName)?.QueueFree()).CallDeferred();
    }
}

/// <summary>Shown again (backing out of a lobby, host or join screen): the save may have changed.</summary>
[HarmonyPatch(typeof(NMultiplayerSubmenu), "OnSubmenuShown")]
public static class RefreshOnShownPatch
{
    private static void Postfix(NMultiplayerSubmenu __instance) => SlotPicker.Of(__instance)?.Refresh();
}

/// <summary>The game re-checks its save after Abandon; the picker follows.</summary>
[HarmonyPatch(typeof(NMultiplayerSubmenu), "UpdateButtons")]
public static class RefreshOnButtonsPatch
{
    private static void Postfix(NMultiplayerSubmenu __instance) => SlotPicker.Of(__instance)?.Refresh();
}

/// <summary>Keeps a copy of a run the game is about to delete (Abandon), so it can be picked up again.</summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentMultiplayerRun))]
public static class SnapshotBeforeDeletePatch
{
    private static void Prefix() => SlotActions.SnapshotBeforeGameDeletes(SaveKind.Multiplayer);
}

/// <summary>Keeps a copy of a multiplayer save that failed the game's load check, before it is renamed to *.corrupt.</summary>
[HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.RenameBrokenMultiplayerRunSave))]
public static class SnapshotBeforeRenamePatch
{
    private static void Prefix() => SlotActions.SnapshotBeforeGameSetsAside(SaveKind.Multiplayer);
}

/// <summary>
/// Keeps a copy of a run save the game could not read or migrate, before it is renamed to *.corrupt.
/// The game routes every save type through here; only the multiplayer and solo saves and their backups
/// are copied, each into its own kind's folder.
/// </summary>
[HarmonyPatch(typeof(MigrationManager), "PreserveCorruptFile")]
public static class SnapshotBeforePreservePatch
{
    private static void Prefix(string savePath)
    {
        foreach (var kind in Enum.GetValues<SaveKind>())
        {
            SlotActions.SnapshotBeforeGameSetsAside(kind, savePath);
        }
    }
}
