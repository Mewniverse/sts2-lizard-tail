using System.Globalization;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>A parked run: one file in the mod's slot folder.</summary>
public sealed record Slot(string FileName, string Path, RunSummary? Summary, SlotState State);

/// <summary>
/// Where things live, and reading and writing them through the game's own save stores.
///
/// The active multiplayer save is changed only through the game's store, which is cloud-aware: a
/// plain file swap would be undone at the next launch, when the game syncs the cloud copy back.
/// Parked runs live in mod_data, which the game never syncs or cleans, through the local store.
/// Parked files are only ever added or moved to the trash folder, never overwritten or deleted.
/// </summary>
public static class SlotStore
{
    private const string RootName = "mod_data/LizardTail";

    /// <summary>Where the mod kept everything while it was called Save Slots (before v0.2.0).</summary>
    private const string OldRootName = "mod_data/SaveSlots";

    private static bool _rootChecked;

    private static readonly AccessTools.FieldRef<SaveManager, ISaveStore> StoreField =
        AccessTools.FieldRefAccess<SaveManager, ISaveStore>("_saveStore");

    public static SaveManager Manager => SaveManager.Instance;

    /// <summary>The game's store: local plus Steam Cloud when cloud saves are on.</summary>
    public static ISaveStore Store => StoreField(Manager);

    /// <summary>The local half of the store, for files the cloud should never see.</summary>
    public static ISaveStore Local => (Store as CloudSaveStore)?.LocalStore ?? Store;

    /// <summary>Steam Cloud, when the player syncs saves with it; null otherwise.</summary>
    public static ICloudSaveStore? Cloud =>
        Store is CloudSaveStore cloud && cloud.HasUserEnabledCloudSync() ? cloud.CloudStore : null;

    public static int ProfileId => Manager.CurrentProfileId;

    /// <summary>Recomputed every time: it changes with the profile and with modded/unmodded paths.</summary>
    public static string ActivePath => RunSaveManager.GetRunSavePath(ProfileId, RunSaveManager.multiplayerRunSaveFileName);

    /// <summary>The mod's folder in mod_data, moved over from its old name the first time it is needed.</summary>
    private static string Root
    {
        get
        {
            if (!_rootChecked)
            {
                _rootChecked = true;
                MoveOldRoot();
            }
            return RootName;
        }
    }

    /// <summary>
    /// The mod was called Save Slots before v0.2.0 and kept its files in mod_data/SaveSlots. Renames that
    /// folder once, so parked runs, trash and remembered names carry over. Left alone if the new folder
    /// already exists, so nothing is ever merged over.
    /// </summary>
    private static void MoveOldRoot()
    {
        try
        {
            if (!Local.DirectoryExists(OldRootName) || Local.DirectoryExists(RootName))
            {
                return;
            }
            var error = DirAccess.RenameAbsolute(Local.GetFullPath(OldRootName), Local.GetFullPath(RootName));
            if (error == Error.Ok)
            {
                ModLog.Info($"Moved {OldRootName} to {RootName} (the mod's old name)");
            }
            else
            {
                ModLog.Warn($"Could not move {OldRootName} to {RootName} ({error}); runs parked under the old name stay there");
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not move {OldRootName} to {RootName}: {ex.Message}");
        }
    }

    /// <summary>
    /// This profile's folder of the mod: the game's own profile folder name, under mod_data. That is
    /// modded/profileN while the game keeps separate modded saves, or profileN while it shares the
    /// normal ones (see <see cref="SharedSaves"/>).
    /// </summary>
    public static string ProfileFolder => $"{Root}/{UserDataPathProvider.GetProfileDir(ProfileId)}";

    public static string SlotDir => $"{ProfileFolder}/slots";

    /// <summary>
    /// The slot folder of the other kind of profile (modded or normal). Runs parked there are listed too,
    /// so switching between shared and separate saves never hides a parked run.
    /// </summary>
    private static string OtherSlotDir
    {
        get
        {
            var modded = UserDataPathProvider.GetAccountDir().Length > 0;
            return $"{Root}/{UserDataPathProvider.GetProfileDir(ProfileId, !modded)}/slots";
        }
    }

    public static string TrashDir => $"{ProfileFolder}/trash";

    /// <summary>Abandon records moved aside when an abandoned run is picked up again.</summary>
    public static string RevivedHistoryDir => $"{ProfileFolder}/revived-history";

    public static string NamesPath => $"{Root}/names.json";

    public static string HistoryPath(long startTime) =>
        System.IO.Path.Combine(RunHistorySaveManager.GetHistoryPath(ProfileId), $"{startTime.ToString(CultureInfo.InvariantCulture)}.run");

    /// <summary>The active run's text: the primary file, or its .backup as the game itself falls back to.</summary>
    public static string? ReadActive()
    {
        var path = ActivePath;
        foreach (var candidate in new[] { path, path + ".backup" })
        {
            if (Store.FileExists(candidate) && Store.ReadFile(candidate) is { } text && RunSummary.TryParse(text) != null)
            {
                return text;
            }
        }
        return null;
    }

    /// <summary>
    /// True when the game's primary save file exists but cannot be read, so <see cref="ReadActive"/> is
    /// showing the older .backup. Clearing the slot then would delete progress the mod never copied.
    /// </summary>
    public static bool ActiveDamaged()
    {
        var path = ActivePath;
        return Store.FileExists(path) && RunSummary.TryParse(Store.ReadFile(path)) == null;
    }

    /// <summary>
    /// The active run as Steam Cloud holds it; null when cloud sync is off or the cloud has none. Steam
    /// reads can throw at any time (the game wraps every one), so callers must catch.
    /// </summary>
    public static RunSummary? ReadCloudActive()
    {
        var path = ActivePath;
        return Cloud is { } cloud && cloud.FileExists(path) ? RunSummary.TryParse(cloud.ReadFile(path)) : null;
    }

    /// <summary>
    /// The active run as far as this PC and Steam Cloud agree (see <see cref="SlotRules.Agreed"/>).
    /// Only the primary file is synced with the cloud: a run read from a lone .backup survives the next
    /// launch whatever the cloud holds, so it counts as agreed. When the cloud cannot be read the answer
    /// is null, which hides and clears nothing, and <paramref name="cloudUnknown"/> says why.
    /// </summary>
    public static RunSummary? AgreedActive(RunSummary? local, out bool cloudUnknown)
    {
        cloudUnknown = false;
        if (local == null || Cloud == null || !Store.FileExists(ActivePath))
        {
            return local;
        }
        try
        {
            return SlotRules.Agreed(local, ReadCloudActive(), cloudSync: true);
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not read the active run from Steam Cloud: {ex.Message}");
            cloudUnknown = true;
            return null;
        }
    }

    /// <summary>
    /// Parks the game's .backup when it holds a different save of a run than the primary. The game
    /// loads the .backup when it cannot load the primary (one from a newer version, say), so clearing the
    /// slot must not delete it uncopied.
    /// </summary>
    public static void ParkBackup(string primaryText)
    {
        var path = ActivePath + ".backup";
        var text = Store.FileExists(path) ? Store.ReadFile(path) : null;
        if (text != null && text != primaryText && RunSummary.TryParse(text) is { } run)
        {
            Park(text, run);
        }
    }

    /// <summary>How the run ended according to the history record on this PC.</summary>
    public static RunEnding EndingOf(long startTime)
    {
        var path = HistoryPath(startTime);
        return Local.FileExists(path) ? HistoryRecord.EndingOf(Local.ReadFile(path) ?? "") : RunEnding.None;
    }

    /// <summary>
    /// How the run ended on this PC or in Steam Cloud. A record that is only in the cloud (the run was
    /// abandoned on another PC) comes down at the next launch and ends the run there too. Throws a
    /// readable refusal when the cloud cannot be read, since an unseen record could end the run.
    /// </summary>
    public static RunEnding EndingAnywhere(long startTime)
    {
        var ending = EndingOf(startTime);
        var path = HistoryPath(startTime);
        if (Cloud is { } cloud)
        {
            try
            {
                if (cloud.FileExists(path))
                {
                    ending = HistoryRecord.Worst(ending, HistoryRecord.EndingOf(cloud.ReadFile(path) ?? ""));
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Could not read run {startTime}'s history from Steam Cloud: {ex.Message}");
                throw new InvalidOperationException("Steam Cloud could not be read just now, so this run's history could not be checked. Nothing was changed; try again in a moment.");
            }
        }
        return ending;
    }

    /// <summary>
    /// Every parked file, readable or not, in no particular order: this profile's slot folder, then the
    /// other kind of profile's (see <see cref="OtherSlotDir"/>).
    /// </summary>
    public static IReadOnlyList<Slot> AllSlots(ulong localPlayerId)
    {
        var latest = Manager.GetLatestSchemaVersion<SerializableRun>();
        var endings = new Dictionary<long, RunEnding>();
        var slots = new List<Slot>();
        foreach (var dir in new[] { SlotDir, OtherSlotDir }.Distinct())
        {
            if (Local.DirectoryExists(dir))
            {
                ReadSlots(dir, localPlayerId, latest, endings, slots);
            }
        }
        return slots;
    }

    private static void ReadSlots(string dir, ulong localPlayerId, int latest, Dictionary<long, RunEnding> endings, List<Slot> slots)
    {
        foreach (var file in Local.GetFilesInDirectory(dir))
        {
            if (!file.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
            {
                continue; // .backup and .tmp siblings the store writes
            }

            var path = $"{dir}/{file}";
            try
            {
                var summary = RunSummary.TryParse(Local.ReadFile(path));
                var ending = RunEnding.None;
                if (summary != null && !endings.TryGetValue(summary.StartTime, out ending))
                {
                    ending = endings[summary.StartTime] = EndingOf(summary.StartTime);
                }
                slots.Add(new Slot(file, path, summary, SlotRules.StateOf(summary, localPlayerId, ending, latest)));
            }
            catch (Exception ex)
            {
                // One bad file must not hide the others; it is listed as unreadable so it can be trashed.
                ModLog.Warn($"Could not read parked run {file}: {ex.Message}");
                slots.Add(new Slot(file, path, null, SlotState.Unreadable));
            }
        }
    }

    /// <summary>The parked runs to show: the newest copy of each, newest first, leaving out the active run.</summary>
    public static IReadOnlyList<Slot> Listed(IReadOnlyList<Slot> all, RunSummary? active) =>
        SlotRules.Listed(all, s => s.Summary, active, s => SlotRules.CanActivate(s.State))
            .OrderByDescending(s => s.Summary?.SaveTime ?? 0)
            .ToList();

    /// <summary>
    /// Parks a copy of a run under a name no other copy uses, and checks it reads back. An identical
    /// copy already parked is reused instead of written twice.
    /// </summary>
    public static string Park(string text, RunSummary run)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var path = $"{SlotDir}/{SlotRules.FileName(run, attempt)}";
            if (Local.FileExists(path))
            {
                if (Local.ReadFile(path) == text)
                {
                    return path;
                }
                continue; // the same times but different content: keep both
            }

            WriteLocal(path, text);
            var check = RunSummary.TryParse(Local.ReadFile(path));
            if (check == null || check.StartTime != run.StartTime || check.SaveTime != run.SaveTime)
            {
                throw new InvalidOperationException("The parked copy did not read back correctly, so nothing else was changed.");
            }
            return path;
        }
        throw new InvalidOperationException("Too many parked copies of this run share one save time.");
    }

    /// <summary>
    /// Parks a save the mod cannot read as a run (damaged, or about to be set aside by the game), so it
    /// is kept and listed as unreadable rather than lost. Readable runs go through <see cref="Park"/>.
    /// </summary>
    public static void ParkAnything(string text, string why)
    {
        if (RunSummary.TryParse(text) is { } run)
        {
            Park(text, run);
            return;
        }
        WriteLocal(FreePath(SlotDir, why, ".save"), text);
    }

    public static void WriteLocal(string path, string text)
    {
        Local.CreateDirectory(path[..path.LastIndexOf('/')]);
        Local.WriteFile(path, Encoding.UTF8.GetBytes(text));
    }

    /// <summary>A path in <paramref name="dir"/> that nothing uses yet: name-stamp.ext, numbered if need be.</summary>
    public static string FreePath(string dir, string name, string extension)
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        for (var n = 0; ; n++)
        {
            var path = $"{dir}/{name}-{stamp}{(n > 0 ? $"-{n}" : "")}{extension}";
            if (!Local.FileExists(path))
            {
                return path;
            }
        }
    }

    /// <summary>Moves one parked file, and its .backup, into the trash folder. Nothing is deleted.</summary>
    public static void MoveToTrash(Slot slot)
    {
        Local.CreateDirectory(TrashDir);
        var target = FreePath(TrashDir, System.IO.Path.GetFileNameWithoutExtension(slot.FileName), ".save");
        Local.RenameFile(slot.Path, target);
        if (Local.FileExists(slot.Path + ".backup"))
        {
            Local.RenameFile(slot.Path + ".backup", target + ".backup");
        }
    }
}
