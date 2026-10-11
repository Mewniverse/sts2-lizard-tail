using System.Text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// Parking, activating and trashing runs, solo or multiplayer. Parked files are never overwritten or
/// deleted: each copy of a run gets its own file, making a run active leaves its parked copy in place,
/// and Trash moves files to a trash folder. Copies of the active run are hidden from the list, and are
/// moved to the trash only once the game has saved newer progress of that run, which proves the swap took.
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
    /// Null when a swap is safe now; otherwise why not. Swapping is only safe on the picker's own menu
    /// with nothing else going on: once Load or Continue is pressed the game holds the run in memory
    /// and writes it back, which would overwrite a swap.
    /// </summary>
    public static string? WhyNotNow(IPickerHost? host)
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
        return host == null ? "The menu is gone." : host.WhyNotHere();
    }

    /// <summary>The "Park this run" button: parks the active run and frees the game's slot.</summary>
    public static string ParkActiveRun(SaveKind kind)
    {
        var (text, run) = ReadParkable(kind);
        SlotStore.Park(kind, text, run);
        SlotStore.ParkBackup(kind, text);
        Remember(kind, run);
        ClearActive(kind);
        ModLog.Info($"Parked {SaveKinds.Name(kind)} run {run.Key} ({run.Players.Count} players)");

        if (CloudStillHasActive(kind))
        {
            ModLog.Warn($"Parked run {run.Key}, but Steam Cloud kept it as the active save");
            return SlotText.ParkedButCloudKept(kind);
        }
        return SlotText.Parked(kind);
    }

    /// <summary>
    /// The active run's text, or a refusal before anything changes: a damaged primary file (the picker
    /// would be showing its older .backup, and clearing the slot would delete the newer progress) or a
    /// save that cannot be read at all.
    /// </summary>
    private static (string Text, RunSummary Run) ReadParkable(SaveKind kind)
    {
        if (SlotStore.ActiveDamaged(kind))
        {
            throw new InvalidOperationException($"The game's save file is damaged, so it was left alone. {SlotText.HowTheGameRepairs(kind)}, then try again.");
        }
        var text = SlotStore.ReadActive(kind)
            ?? throw new InvalidOperationException(kind == SaveKind.Solo
                ? "The game's solo save cannot be read, so it was left alone. The game sets it aside the next time it draws the main menu."
                : "The game's multiplayer save cannot be read, so it was left alone. Press Load once so the game sets it aside.");
        return (text, RunSummary.TryParse(text)!);
    }

    /// <summary>
    /// Looks up the Steam names of a multiplayer run's players while Steam is likely to know them. A
    /// solo run has no one to name.
    /// </summary>
    private static void Remember(SaveKind kind, RunSummary run)
    {
        if (kind == SaveKind.Multiplayer)
        {
            PlayerNames.Remember(run);
        }
    }

    private static bool CloudStillHasActive(SaveKind kind)
    {
        try
        {
            return SlotStore.Cloud is { } cloud && cloud.FileExists(SlotStore.ActivePath(kind));
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not check Steam Cloud after parking: {ex.Message}");
            return false;
        }
    }

    /// <summary>Clears the game's slot of this kind (here and in Steam Cloud) after its run has been parked.</summary>
    private static void ClearActive(SaveKind kind)
    {
        Busy = true;
        try
        {
            using (SlotStore.Manager.BeginSaveBatch())
            {
                SlotStore.DeleteActive(kind);
            }
        }
        finally
        {
            Busy = false;
        }

        if (SlotStore.HasActive(kind))
        {
            throw new InvalidOperationException($"The game's {SaveKinds.Name(kind)} save could not be cleared. The run is also parked.");
        }
    }

    /// <summary>
    /// Makes a parked run the game's save of its kind. Everything that could refuse is checked before
    /// anything changes: you are in the run (multiplayer only), the game can load it with the installed
    /// version and mods, it has not been won or lost here or in Steam Cloud, and the active run can be
    /// parked. Then, in order: the active run is parked (only adds a file), an abandon record is moved
    /// aside, the slot is cleared, and the run is written through the game's store and read back. If a
    /// step after the record moved fails, the record is put back. The parked copy stays where it is.
    /// </summary>
    public static string Activate(Slot slot)
    {
        var kind = slot.Kind;
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

        // Continue never asks who is in a solo save, so neither does this.
        var host = SaveKinds.NeedsYouInIt(kind) ? LocalPlayerId : 0;
        if (SaveKinds.NeedsYouInIt(kind))
        {
            if (host == 0)
            {
                throw new InvalidOperationException("Steam is not ready yet, so the game could not host this run. Nothing was changed.");
            }
            if (!run.HasPlayer(host))
            {
                throw new InvalidOperationException(SlotText.StateNote(SlotState.NotYourRun));
            }
        }
        SaveCheck.Verify(kind, text, host);

        var ending = SlotStore.EndingAnywhere(run.StartTime);
        if (ending == RunEnding.Finished)
        {
            throw new InvalidOperationException(SlotText.StateNote(SlotState.Finished));
        }

        (string Text, RunSummary Run)? active = SlotStore.HasActive(kind) ? ReadParkable(kind) : null;
        if (active is { } same && same.Run.StartTime == run.StartTime && ending == RunEnding.None
            && same.Run.SchemaVersion <= SlotStore.Manager.GetLatestSchemaVersion<SerializableRun>()
            && SlotStore.AgreedActive(kind, same.Run, out _) is { } agreed && agreed.SaveTime >= run.SaveTime)
        {
            return $"This run is already active. {SlotText.ContinuesIt(kind)}";
        }

        // Every check has passed. Additive steps first, then the ones that change the game's files.
        if (active is { } current)
        {
            SlotStore.Park(kind, current.Text, current.Run);
            SlotStore.ParkBackup(kind, current.Text);
            Remember(kind, current.Run);
        }
        var record = ending == RunEnding.Abandoned ? ReviveHistory(kind, run.StartTime) : null;

        var path = SlotStore.ActivePath(kind);
        try
        {
            if (active != null)
            {
                ClearActive(kind);
            }
            SlotStore.Local.CreateDirectory(path[..path.LastIndexOf('/')]);
            using (SlotStore.Manager.BeginSaveBatch())
            {
                SlotStore.Store.WriteFile(path, Encoding.UTF8.GetBytes(text));
            }
            if (!SameCopy(RunSummary.TryParse(SlotStore.ReadActive(kind)), run))
            {
                throw new InvalidOperationException($"The run did not read back from the game's {SaveKinds.Name(kind)} slot. It is still parked.");
            }
        }
        catch
        {
            if (record != null)
            {
                RestoreHistory(kind, run.StartTime, record);
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
                return $"Done, but Steam Cloud could not be checked just now. {SlotText.ContinuesIt(kind)}";
            }
            if (!SameCopy(inCloud, run))
            {
                ModLog.Warn($"Activated run {run.Key} on this PC, but Steam Cloud did not take the write");
                throw new InvalidOperationException("Steam Cloud refused the save (it may be full), so the game would remove this run at the next launch. It is still parked: free some Steam Cloud space or turn off Steam Cloud for the game, then make it active again.");
            }
        }

        ModLog.Info($"Activated {SaveKinds.Name(kind)} run {run.Key}");
        return $"Done. {SlotText.ContinuesIt(kind)}";
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
    private static string? ReviveHistory(SaveKind kind, long startTime)
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

        SlotStore.WriteLocal(SlotStore.FreePath(SlotStore.RevivedHistoryDir(kind), startTime.ToString(System.Globalization.CultureInfo.InvariantCulture), ".run"), record);
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
            RestoreHistory(kind, startTime, record);
            throw new InvalidOperationException("The abandon record could not be removed here and from Steam Cloud, so the game would delete this run again. Nothing was changed.");
        }
        ModLog.Info($"Moved the abandon record of run {startTime} aside so it can continue");
        return record;
    }

    /// <summary>Puts an abandon record back where the game keeps it, after a swap that did not complete.</summary>
    private static void RestoreHistory(SaveKind kind, long startTime, string record)
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
            ModLog.Error($"Could not put the abandon record of run {startTime} back; a copy is in {SlotStore.RevivedHistoryDir(kind)}: {ex}");
        }
    }

    /// <summary>
    /// Called just before the game deletes the active save of this kind for its own reasons (Abandon,
    /// or a run ending). Keeps a parked copy unless the run was won or lost, so an abandon can be
    /// undone from the picker. A damaged primary file is kept too, beside its readable backup.
    /// </summary>
    public static void SnapshotBeforeGameDeletes(SaveKind kind)
    {
        if (Busy || !SlotStore.Manager.IsProfileInitialized)
        {
            return;
        }

        try
        {
            var path = SlotStore.ActivePath(kind);
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

                SlotStore.ParkAnything(kind, text, "damaged");
                if (run != null)
                {
                    Remember(kind, run);
                    ModLog.Info($"Kept a copy of {SaveKinds.Name(kind)} run {run.Key} before the game removed it");
                    return; // a readable save is enough; its .backup is older
                }
                ModLog.Info($"Kept a copy of the damaged {candidate} before the game removed it");
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not keep a copy of the {SaveKinds.Name(kind)} run before it was removed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called just before the game renames a save of this kind it cannot load to *.corrupt (multiplayer:
    /// on Load or Abandon; solo: whenever the main menu draws its buttons). Keeps a parked copy, so the
    /// run is listed (as unreadable or from a newer version) instead of only sitting in the saves folder.
    /// <paramref name="path"/> limits it to one file.
    /// </summary>
    public static void SnapshotBeforeGameSetsAside(SaveKind kind, string? path = null)
    {
        if (Busy || !SlotStore.Manager.IsProfileInitialized)
        {
            return;
        }

        try
        {
            var active = SlotStore.ActivePath(kind);
            var ours = new[] { active, active + ".backup" };
            foreach (var candidate in path == null ? ours : ours.Where(p => SamePath(p, path)))
            {
                // The game shows an error popup but does not redraw the menu, whatever the file held.
                SlotPicker.RequestRefresh(kind);
                var text = SlotStore.Store.FileExists(candidate) ? SlotStore.Store.ReadFile(candidate) : null;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    SlotStore.ParkAnything(kind, text, "set-aside");
                    ModLog.Info($"Kept a copy of {candidate} before the game set it aside as unloadable");
                }
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not keep a copy of the {SaveKinds.Name(kind)} save the game set aside: {ex.Message}");
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
