using System.Globalization;

namespace LizardTail.LizardTailCode.Core;

/// <summary>Whether a parked run can be made the active one, and if not, why.</summary>
public enum SlotState
{
    /// <summary>Can be made active as it is.</summary>
    Ready,
    /// <summary>Was abandoned; making it active moves the abandon record aside so the run continues.</summary>
    Abandoned,
    /// <summary>The run was won or lost. The game would delete it at startup, so it cannot be made active.</summary>
    Finished,
    /// <summary>You are not one of its players; only a player in the save can host it.</summary>
    NotYourRun,
    /// <summary>Saved by a newer version of the game than this one.</summary>
    NewerVersion,
    /// <summary>The file is not a run save this mod can read.</summary>
    Unreadable,
}

public static class SlotRules
{
    /// <summary>The oldest run-save schema the game still migrates (MigrationManager's minimum).</summary>
    public const int OldestSupportedSchema = 13;

    public static SlotState StateOf(RunSummary? run, ulong localPlayerId, RunEnding ending, int latestSchema)
    {
        if (run == null || run.SchemaVersion < OldestSupportedSchema)
        {
            return SlotState.Unreadable;
        }
        if (latestSchema > 0 && run.SchemaVersion > latestSchema)
        {
            return SlotState.NewerVersion;
        }
        if (localPlayerId != 0 && !run.HasPlayer(localPlayerId))
        {
            return SlotState.NotYourRun;
        }
        return ending switch
        {
            RunEnding.Finished => SlotState.Finished,
            RunEnding.Abandoned => SlotState.Abandoned,
            _ => SlotState.Ready,
        };
    }

    public static bool CanActivate(SlotState state) => state is SlotState.Ready or SlotState.Abandoned;

    /// <summary>
    /// Copies of the same run (same start time) collapse to one, so a run parked and later made active
    /// again never shows twice: the newest copy that <paramref name="usable"/> accepts (one this game
    /// can continue), or the newest copy when none is. Unreadable files are all kept, each on its own.
    /// </summary>
    public static IReadOnlyList<T> Newest<T>(IEnumerable<T> slots, Func<T, RunSummary?> summary, Func<T, bool>? usable = null) =>
        slots
            .GroupBy(s => summary(s)?.StartTime ?? long.MinValue)
            .SelectMany(g => g.Key == long.MinValue
                ? g
                : g.OrderByDescending(s => usable?.Invoke(s) ?? true).ThenByDescending(s => summary(s)!.SaveTime).Take(1))
            .ToList();

    /// <summary>
    /// The parked runs to list: one copy of each run (see <see cref="Newest"/>), leaving out a run whose
    /// active copy is at least as recent (it is the active run, shown once at the top).
    /// </summary>
    public static IReadOnlyList<T> Listed<T>(IEnumerable<T> slots, Func<T, RunSummary?> summary, RunSummary? active, Func<T, bool>? usable = null) =>
        Newest(slots, summary, usable)
            .Where(s => !(active != null && summary(s) is { } run && run.StartTime == active.StartTime && run.SaveTime <= active.SaveTime))
            .ToList();

    /// <summary>
    /// Parked copies that are safe to clear: copies of the active run saved before it. The game has
    /// saved that run since the copy was made, which proves the active one loads; a copy saved at the
    /// same moment is kept, because it may be the only copy left if the active save is lost.
    /// </summary>
    public static IReadOnlyList<T> Superseded<T>(IEnumerable<T> slots, Func<T, RunSummary?> summary, RunSummary? active) =>
        active == null
            ? Array.Empty<T>()
            : slots.Where(s => summary(s) is { } run && run.StartTime == active.StartTime && run.SaveTime < active.SaveTime).ToList();

    /// <summary>
    /// The active run as far as this PC and Steam Cloud agree. With cloud sync on, the next launch makes
    /// this PC match the cloud, so a run the cloud does not hold is not safely active (null), and when the
    /// cloud holds an older save of it, that older save is what survives. Copies are hidden and cleared
    /// against this, never against a local save the cloud has not taken.
    /// </summary>
    public static RunSummary? Agreed(RunSummary? local, RunSummary? cloud, bool cloudSync)
    {
        if (local == null || !cloudSync)
        {
            return local;
        }
        if (cloud == null || cloud.StartTime != local.StartTime)
        {
            return null;
        }
        return cloud.SaveTime < local.SaveTime ? cloud : local;
    }

    /// <summary>
    /// The file name for a parked copy: the run's start and save time, so two copies of a run never share
    /// a name and parking never overwrites anything. <paramref name="attempt"/> separates the rare copies
    /// that share both times but differ in content.
    /// </summary>
    public static string FileName(RunSummary run, int attempt = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"{run.StartTime}-{run.SaveTime}{(attempt > 0 ? $"-{attempt}" : "")}.save");

    /// <summary>Every file holding a copy of the same run as <paramref name="slot"/> (just that file when unreadable).</summary>
    public static IReadOnlyList<T> CopiesOf<T>(IEnumerable<T> slots, Func<T, RunSummary?> summary, T slot) =>
        summary(slot) is { } run
            ? slots.Where(s => summary(s)?.StartTime == run.StartTime).ToList()
            : new List<T> { slot };
}

/// <summary>The words the picker uses for a run.</summary>
public static class SlotText
{
    /// <summary>"CHARACTER.IRONCLAD" → "Ironclad"; "ACT.UNDERDOCKS" → "Underdocks".</summary>
    public static string Pretty(string id)
    {
        var bare = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id;
        if (bare.Length == 0)
        {
            return "?";
        }
        var words = bare.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', words);
    }

    /// <summary>"Act 1 · Underdocks · floor 15 · A10", with "Daily" or a custom mode when it is one.</summary>
    public static string Progress(RunSummary run)
    {
        var parts = new List<string> { $"Act {run.CurrentActIndex + 1}" };
        if (run.CurrentActId is { } act)
        {
            parts.Add(Pretty(act));
        }
        parts.Add($"floor {run.Floor}");
        if (run.Ascension > 0)
        {
            parts.Add($"A{run.Ascension}");
        }
        if (run.IsDaily)
        {
            parts.Add("Daily");
        }
        else if (!string.Equals(run.GameMode, "standard", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(Pretty(run.GameMode));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"You (Ironclad), Sam (Defect)", with your own seat first.</summary>
    public static string Players(RunSummary run, ulong localPlayerId, Func<ulong, string?> nameOf) =>
        string.Join(", ", run.Players
            .OrderBy(p => p.NetId == localPlayerId ? 0 : 1)
            .Select(p => $"{(p.NetId == localPlayerId ? "You" : nameOf(p.NetId) ?? "Unknown player")} ({Pretty(p.CharacterId)})"));

    /// <summary>"Ironclad": a solo run has one player, you, so only the character is worth showing.</summary>
    public static string Characters(RunSummary run) =>
        run.Players.Count == 0 ? "Unknown character" : string.Join(", ", run.Players.Select(p => Pretty(p.CharacterId)));

    /// <summary>The first line of a run's row: who is in a multiplayer run, or the character of a solo one.</summary>
    public static string Who(SaveKind kind, RunSummary run, ulong localPlayerId, Func<ulong, string?> nameOf) =>
        kind == SaveKind.Solo ? Characters(run) : Players(run, localPlayerId, nameOf);

    /// <summary>"Saved 14 Nov, 22:13" in local time, from a Unix time in seconds.</summary>
    public static string Saved(long unixSeconds, TimeZoneInfo? zone = null)
    {
        if (unixSeconds <= 0)
        {
            return "Saved at an unknown time";
        }
        try
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), zone ?? TimeZoneInfo.Local);
            return "Saved " + local.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "Saved at an unknown time"; // a corrupt or millisecond value
        }
    }

    public static string? StateNote(SlotState state) => state switch
    {
        SlotState.Abandoned => "Abandoned. Making it active picks it up again.",
        SlotState.Finished => "This run ended. The game deletes saves of finished runs.",
        SlotState.NotYourRun => "You are not in this run, so you cannot host it.",
        SlotState.NewerVersion => "Saved by a newer version of the game.",
        SlotState.Unreadable => "This file is not a run save this mod can read.",
        _ => null,
    };

    /// <summary>Shown on the active run when Steam Cloud does not hold it, so the next launch would remove it.</summary>
    public const string NotInCloud = "Steam Cloud does not have this run (its space may be full), so the game would remove it at the next launch. Park it to keep a copy, then make it active again once Steam Cloud has room.";

    /// <summary>Shown on the active run when Steam Cloud could not be read; nothing is hidden or cleared meanwhile.</summary>
    public const string CloudUnknown = "Steam Cloud could not be checked for this run just now.";

    /// <summary>
    /// How the game comes to repair a damaged save or set it aside. Multiplayer: when Load is pressed.
    /// Solo: by itself, since the main menu loads the solo save every time it draws its buttons.
    /// </summary>
    public static string HowTheGameRepairs(SaveKind kind) => kind == SaveKind.Solo
        ? "The game repairs it or sets it aside the next time it draws the main menu"
        : "Press Load once so the game repairs it or sets it aside";

    /// <summary>Shown when the game has a save that cannot be read at all.</summary>
    public static string Unreadable(SaveKind kind) => kind == SaveKind.Solo
        ? "The game's solo save cannot be read. The game sets it aside the next time it draws the main menu; the mod parks a copy of whatever it holds."
        : "The game's multiplayer save cannot be read. Press Load once so the game sets it aside; the mod parks a copy of whatever it holds.";

    /// <summary>Shown when the game's save file is damaged and the picker is reading its .backup instead.</summary>
    public static string Damaged(SaveKind kind) =>
        $"The game's save file is damaged; this is its backup. {HowTheGameRepairs(kind)} before parking.";

    public static string Title(SaveKind kind) => kind == SaveKind.Solo ? "Solo runs" : "Multiplayer runs";

    /// <summary>The line under the title saying what the panel is for.</summary>
    public static string Hint(SaveKind kind) => kind == SaveKind.Solo
        ? "Continue picks up the active run. Park it to start a new one; make any parked run active to continue it."
        : "Load continues the active run. Park it to host a new one; make any parked run active to continue it.";

    public static string NoActive(SaveKind kind) => $"No active run. {SaveKinds.NewRunButton(kind)} starts a new one.";

    /// <summary>What Park this run answers.</summary>
    public static string Parked(SaveKind kind) => $"Parked. {SaveKinds.NewRunButton(kind)} now starts a new run.";

    /// <summary>What Park this run answers when Steam Cloud still holds the run it just cleared.</summary>
    public static string ParkedButCloudKept(SaveKind kind) => kind == SaveKind.Solo
        ? "Parked, but Steam Cloud kept the old save, so it comes back at the next launch unless you start a run first."
        : "Parked, but Steam Cloud kept the old save, so it comes back at the next launch unless you host first.";

    /// <summary>Which of the game's buttons continues the run now, said after Done or after a note.</summary>
    public static string ContinuesIt(SaveKind kind) => kind == SaveKind.Solo
        ? "Continue picks this run up."
        : "Load continues this run.";

    /// <summary>
    /// The button that opens and closes a panel that starts closed (the solo one, on the main menu). Closed,
    /// it says how many runs are parked, so there is a reason to open it.
    /// </summary>
    public static string Toggle(bool open, int parked) =>
        open ? "Hide" : parked > 0 ? $"Show ({parked} parked)" : "Show";
}
