using System.Text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SaveSlots.SaveSlotsCode.Core;

namespace SaveSlots.SaveSlotsCode.Game;

/// <summary>
/// Parking, activating and trashing runs. Parked files are never overwritten or deleted: each copy of a
/// run gets its own file, making a run active leaves its parked copy in place, and Trash moves files
/// to a trash folder. Copies of the active run are hidden from the list, and are moved to the trash
/// only once the game has saved newer progress of that run, which proves the swap took.
/// </summary>
public static class SlotActions
{
    /// <summary>True while this mod is itself deleting the active save, so the snapshot hook stands aside.</summary>
    public static bool Busy { get; private set; }

    /// <summary>The id the game hosts with: Steam's, or the dev id under -fastmp, as the menu's Load picks it.</summary>
    public static ulong LocalPlayerId
    {
        get
        {
            try
            {
                var platform = SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp") ? PlatformType.Steam : PlatformType.None;
                return PlatformUtil.GetLocalPlayerId(platform);
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Null when a swap is safe now; otherwise why not. Swapping is only safe on the multiplayer menu
    /// with nothing else going on: once Load is pressed the game holds the run in memory and writes
    /// it back, which would overwrite a swap.
    /// </summary>
    public static string? WhyNotNow(NMultiplayerSubmenu? submenu)
    {
        if (Busy)
        {
            return "Busy with the last change.";
        }
        if (!SlotStore.Manager.IsProfileInitialized)
        {
            return "The game is still loading your profile.";
        }
        if (RunManager.Instance?.IsInProgress == true)
        {
            return "A run is in progress.";
        }
        if (SlotStore.Manager.CurrentRunSaveTask != null)
        {
            return "The game is saving.";
        }
        if (NModalContainer.Instance?.OpenModal != null)
        {
            return "Close the open popup first.";
        }
        if (submenu == null || NGame.Instance?.MainMenu?.SubmenuStack.Peek() != submenu)
        {
            return "Only available on the multiplayer menu.";
        }
        if (MultiplayerMenu.IsLoading(submenu))
        {
            return "Hosting has started.";
        }
        return null;
    }

    /// <summary>The "Park this run" button: parks the active run and frees the game's slot.</summary>
    public static string ParkActiveRun()
    {
        var (text, run) = ReadParkable();
        SlotStore.Park(text, run);
        SlotStore.ParkBackup(text);
        PlayerNames.Remember(run);
        ClearActive();
        ModLog.Info($"Parked multiplayer run {run.Key} ({run.Players.Count} players)");

        if (CloudStillHasActive())
        {
            ModLog.Warn($"Parked run {run.Key}, but Steam Cloud kept it as the active save");
            return "Parked, but Steam Cloud kept the old save, so it comes back at the next launch unless you host first.";
        }
        return "Parked. Host now starts a new run.";
    }

    /// <summary>
    /// The active run's text, or a refusal before anything changes: a damaged primary file (the picker
    /// would be showing its older .backup, and clearing the slot would delete the newer progress) or a
    /// save that cannot be read at all.
    /// </summary>
    private static (string Text, RunSummary Run) ReadParkable()
    {
        if (SlotStore.ActiveDamaged())
        {
            throw new InvalidOperationException("The game's save file is damaged, so it was left alone. Press Load once so the game repairs it or sets it aside, then try again.");
        }
        var text = SlotStore.ReadActive()
            ?? throw new InvalidOperationException("The game's multiplayer save cannot be read, so it was left alone. Press Load once so the game sets it aside.");
        return (text, RunSummary.TryParse(text)!);
    }

    private static bool CloudStillHasActive()
    {
        try
        {
            return SlotStore.Cloud is { } cloud && cloud.FileExists(SlotStore.ActivePath);
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not check Steam Cloud after parking: {ex.Message}");
            return false;
        }
    }

    /// <summary>Clears the game's multiplayer slot (here and in Steam Cloud) after its run has been parked.</summary>
    private static void ClearActive()
    {
        Busy = true;
        try
        {
            using (SlotStore.Manager.BeginSaveBatch())
            {
                SlotStore.Manager.DeleteCurrentMultiplayerRun();
            }
        }
        finally
        {
            Busy = false;
        }

        if (SlotStore.Manager.HasMultiplayerRunSave)
        {
            throw new InvalidOperationException("The game's multiplayer save could not be cleared. The run is also parked.");
        }
    }

    /// <summary>
    /// Makes a parked run the game's multiplayer save. Everything that could refuse is checked before
    /// anything changes: you are in the run, the game can load it with the installed version and mods,
    /// it has not been won or lost here or in Steam Cloud, and the active run can be parked. Then, in
    /// order: the active run is parked (only adds a file), an abandon record is moved aside, the slot is
    /// cleared, and the run is written through the game's store and read back. If a step after the
    /// record moved fails, the record is put back. The parked copy stays where it is.
    /// </summary>
    public static string Activate(Slot slot)
    {
        if (!SlotRules.CanActivate(slot.State) || slot.Summary == null)
        {
            throw new InvalidOperationException(SlotText.StateNote(slot.State) ?? "This run cannot be made active.");
        }

        var text = SlotStore.Local.ReadFile(slot.Path) ?? throw new InvalidOperationException("The parked run could not be read.");
        var run = RunSummary.TryParse(text);
        if (run == null || run.StartTime != slot.Summary.StartTime || run.SaveTime != slot.Summary.SaveTime)
        {
            throw new InvalidOperationException("The parked file changed since the list was drawn. Nothing was changed.");
        }

        var host = LocalPlayerId;
        if (host == 0)
        {
            throw new InvalidOperationException("Steam is not ready yet, so the game could not host this run. Nothing was changed.");
        }
        if (!run.HasPlayer(host))
        {
            throw new InvalidOperationException(SlotText.StateNote(SlotState.NotYourRun));
        }
        SaveCheck.Verify(text, host);

        var ending = SlotStore.EndingAnywhere(run.StartTime);
        if (ending == RunEnding.Finished)
        {
            throw new InvalidOperationException(SlotText.StateNote(SlotState.Finished));
        }

        (string Text, RunSummary Run)? active = SlotStore.Manager.HasMultiplayerRunSave ? ReadParkable() : null;
        if (active is { } same && same.Run.StartTime == run.StartTime && ending == RunEnding.None
            && same.Run.SchemaVersion <= SlotStore.Manager.GetLatestSchemaVersion<SerializableRun>()
            && SlotStore.AgreedActive(same.Run, out _) is { } agreed && agreed.SaveTime >= run.SaveTime)
        {
            return "This run is already active. Load continues it.";
        }

        // Every check has passed. Additive steps first, then the ones that change the game's files.
        if (active is { } current)
        {
            SlotStore.Park(current.Text, current.Run);
            SlotStore.ParkBackup(current.Text);
            PlayerNames.Remember(current.Run);
        }
        var record = ending == RunEnding.Abandoned ? ReviveHistory(run.StartTime) : null;

        var path = SlotStore.ActivePath;
        try
        {
            if (active != null)
            {
                ClearActive();
            }
            SlotStore.Local.CreateDirectory(path[..path.LastIndexOf('/')]);
            using (SlotStore.Manager.BeginSaveBatch())
            {
                SlotStore.Store.WriteFile(path, Encoding.UTF8.GetBytes(text));
            }
            if (!SameCopy(RunSummary.TryParse(SlotStore.ReadActive()), run))
            {
                throw new InvalidOperationException("The run did not read back from the game's multiplayer slot. It is still parked.");
            }
        }
        catch
        {
            if (record != null)
            {
                RestoreHistory(run.StartTime, record);
            }
            throw;
        }

        if (SlotStore.Cloud is { } cloud)
        {
            RunSummary? inCloud;
            try
            {
                inCloud = RunSummary.TryParse(cloud.FileExists(path) ? cloud.ReadFile(path) : null);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Activated run {run.Key}, but could not read it back from Steam Cloud: {ex.Message}");
                return "Done, but Steam Cloud could not be checked just now. Load continues this run.";
            }
            if (!SameCopy(inCloud, run))
            {
                ModLog.Warn($"Activated run {run.Key} on this PC, but Steam Cloud did not take the write");
                throw new InvalidOperationException("Steam Cloud refused the save (it may be full), so the game would remove this run at the next launch. It is still parked: free some Steam Cloud space or turn off Steam Cloud for the game, then make it active again.");
            }
        }

        ModLog.Info($"Activated multiplayer run {run.Key}");
        return "Done. Load continues this run.";
    }

    /// <summary>Moves every copy of a parked run to the trash folder.</summary>
    public static string Trash(Slot slot, IReadOnlyList<Slot> all)
    {
        foreach (var copy in SlotRules.CopiesOf(all, s => s.Summary, slot))
        {
            SlotStore.MoveToTrash(copy);
        }
        return "Moved to the trash folder.";
    }

    /// <summary>
    /// Moves parked copies of the active run that the game has since saved past into the trash. They
    /// are hidden from the list already; this keeps the folder from growing a copy per swap.
    /// </summary>
    public static void TrashSuperseded(IReadOnlyList<Slot> all, RunSummary? active)
    {
        foreach (var stale in SlotRules.Superseded(all, s => s.Summary, active))
        {
            try
            {
                SlotStore.MoveToTrash(stale);
                ModLog.Info($"Moved {stale.FileName} to the trash: the game has saved that run since");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Could not move {stale.FileName} to the trash: {ex.Message}");
            }
        }
    }

    private static bool SameCopy(RunSummary? a, RunSummary b) => a != null && a.StartTime == b.StartTime && a.SaveTime == b.SaveTime;

    /// <summary>
    /// Moves an abandon record aside (a copy is kept in the mod's folder) and removes it from the game's
    /// history here and in Steam Cloud, then checks both. If either still has it, the game would delete
    /// the run again at the next launch, so the record is put back and the swap is refused. Returns the
    /// record, so a later failure can put it back; null when there was none.
    /// </summary>
    private static string? ReviveHistory(long startTime)
    {
        var path = SlotStore.HistoryPath(startTime);
        var cloud = SlotStore.Cloud;
        var record = SlotStore.Local.FileExists(path) ? SlotStore.Local.ReadFile(path)
            : cloud != null && cloud.FileExists(path) ? cloud.ReadFile(path)
            : null;
        if (record == null)
        {
            return null;
        }
        if (HistoryRecord.EndingOf(record) != RunEnding.Abandoned)
        {
            throw new InvalidOperationException(SlotText.StateNote(SlotState.Finished));
        }

        SlotStore.WriteLocal(SlotStore.FreePath(SlotStore.RevivedHistoryDir, startTime.ToString(System.Globalization.CultureInfo.InvariantCulture), ".run"), record);
        bool removed;
        try
        {
            using (SlotStore.Manager.BeginSaveBatch())
            {
                SlotStore.Store.DeleteFile(path);
                if (cloud != null && cloud.FileExists(path))
                {
                    cloud.DeleteFile(path);
                }
            }
            removed = !SlotStore.Local.FileExists(path) && cloud?.FileExists(path) != true;
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not move the abandon record of run {startTime} aside: {ex.Message}");
            removed = false; // cannot tell what Steam Cloud holds, so assume the record is still there
        }

        if (!removed)
        {
            RestoreHistory(startTime, record);
            throw new InvalidOperationException("The abandon record could not be removed here and from Steam Cloud, so the game would delete this run again. Nothing was changed.");
        }
        ModLog.Info($"Moved the abandon record of run {startTime} aside so it can continue");
        return record;
    }

    /// <summary>Puts an abandon record back where the game keeps it, after a swap that did not complete.</summary>
    private static void RestoreHistory(long startTime, string record)
    {
        var path = SlotStore.HistoryPath(startTime);
        try
        {
            SlotStore.Local.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using (SlotStore.Manager.BeginSaveBatch())
            {
                SlotStore.Store.WriteFile(path, Encoding.UTF8.GetBytes(record));
            }
            ModLog.Info($"Put the abandon record of run {startTime} back");
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not put the abandon record of run {startTime} back; a copy is in {SlotStore.RevivedHistoryDir}: {ex}");
        }
    }

    /// <summary>
    /// Called just before the game deletes the active multiplayer save for its own reasons (Abandon,
    /// or a run ending). Keeps a parked copy unless the run was won or lost, so an abandon can be
    /// undone from the picker. A damaged primary file is kept too, beside its readable backup.
    /// </summary>
    public static void SnapshotBeforeGameDeletes()
    {
        if (Busy || !SlotStore.Manager.IsProfileInitialized)
        {
            return;
        }

        try
        {
            var path = SlotStore.ActivePath;
            foreach (var candidate in new[] { path, path + ".backup" })
            {
                var text = SlotStore.Store.FileExists(candidate) ? SlotStore.Store.ReadFile(candidate) : null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                var run = RunSummary.TryParse(text);
                if (run != null && SlotStore.EndingOf(run.StartTime) == RunEnding.Finished)
                {
                    return; // won or lost: nothing to pick up again
                }

                SlotStore.ParkAnything(text, "damaged");
                if (run != null)
                {
                    PlayerNames.Remember(run);
                    ModLog.Info($"Kept a copy of multiplayer run {run.Key} before the game removed it");
                    return; // a readable save is enough; its .backup is older
                }
                ModLog.Info($"Kept a copy of the damaged {candidate} before the game removed it");
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not keep a copy of the multiplayer run before it was removed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called just before the game renames a multiplayer save it cannot load to *.corrupt (on Load or
    /// Abandon). Keeps a parked copy, so the run is listed (as unreadable or from a newer version)
    /// instead of only sitting in the saves folder. <paramref name="path"/> limits it to one file.
    /// </summary>
    public static void SnapshotBeforeGameSetsAside(string? path = null)
    {
        if (Busy || !SlotStore.Manager.IsProfileInitialized)
        {
            return;
        }

        try
        {
            var active = SlotStore.ActivePath;
            var ours = new[] { active, active + ".backup" };
            foreach (var candidate in path == null ? ours : ours.Where(p => SamePath(p, path)))
            {
                // The game shows an error popup but does not redraw the menu, whatever the file held.
                SlotPicker.RequestRefresh();
                var text = SlotStore.Store.FileExists(candidate) ? SlotStore.Store.ReadFile(candidate) : null;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    SlotStore.ParkAnything(text, "set-aside");
                    ModLog.Info($"Kept a copy of {candidate} before the game set it aside as unloadable");
                }
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not keep a copy of the multiplayer save the game set aside: {ex.Message}");
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
