using System.Text.Json;

namespace LizardTail.LizardTailCode.Core;

/// <summary>One player in a saved run.</summary>
public readonly record struct RunPlayer(ulong NetId, string CharacterId, int Hp, int MaxHp);

/// <summary>
/// What the picker shows about a saved multiplayer run, read straight from the save's JSON. Read
/// with JsonDocument rather than the game's serializer: it works on any schema version and has no
/// side effects (the game's own loader renames a file it cannot validate to *.corrupt).
/// </summary>
public sealed record RunSummary(
    long StartTime,
    long SaveTime,
    int RunTimeSeconds,
    int Ascension,
    string GameMode,
    bool IsDaily,
    int CurrentActIndex,
    IReadOnlyList<string> ActIds,
    int Floor,
    IReadOnlyList<RunPlayer> Players,
    int SchemaVersion)
{
    /// <summary>The run's identity: also the name of its history file once it ends.</summary>
    public string Key => StartTime.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string? CurrentActId => CurrentActIndex >= 0 && CurrentActIndex < ActIds.Count ? ActIds[CurrentActIndex] : null;

    public bool HasPlayer(ulong netId) => Players.Any(p => p.NetId == netId);

    /// <summary>Null when the text is not a run save the picker can describe.</summary>
    public static RunSummary? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Long(root, "start_time") is not { } start)
            {
                return null;
            }

            var acts = new List<string>();
            if (root.TryGetProperty("acts", out var actArray) && actArray.ValueKind == JsonValueKind.Array)
            {
                acts.AddRange(actArray.EnumerateArray().Select(a => Str(a, "id") ?? "").Where(id => id.Length > 0));
            }

            // map_point_history is one list of visited rooms per act; the floor reached is their total
            // (the game's own SerializableRun.FloorReached).
            var floor = 0;
            if (root.TryGetProperty("map_point_history", out var history) && history.ValueKind == JsonValueKind.Array)
            {
                floor = history.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Array).Sum(a => a.GetArrayLength());
            }

            var players = new List<RunPlayer>();
            if (root.TryGetProperty("players", out var playerArray) && playerArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in playerArray.EnumerateArray())
                {
                    if (ULong(p, "net_id") is { } id)
                    {
                        players.Add(new RunPlayer(id, Str(p, "character_id") ?? "", (int)(Long(p, "current_hp") ?? 0), (int)(Long(p, "max_hp") ?? 0)));
                    }
                }
            }

            return new RunSummary(
                start,
                Long(root, "save_time") ?? 0,
                (int)(Long(root, "run_time") ?? 0),
                (int)(Long(root, "ascension") ?? 0),
                Str(root, "game_mode") ?? "standard",
                root.TryGetProperty("dailyTime", out var daily) && daily.ValueKind != JsonValueKind.Null,
                (int)(Long(root, "current_act_index") ?? 0),
                acts,
                floor,
                players,
                (int)(Long(root, "schema_version") ?? 0));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static ulong? ULong(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out var n) ? n : null;
}

/// <summary>How a run's history record says it ended.</summary>
public enum RunEnding
{
    /// <summary>No history record: the run has not ended.</summary>
    None,
    /// <summary>Abandoned from the menu. The run can be picked up again by moving the record aside.</summary>
    Abandoned,
    /// <summary>Won or lost. The game deletes any save of a finished run at startup.</summary>
    Finished,
}

public static class HistoryRecord
{
    /// <summary>
    /// Reads a history record (history/&lt;start_time&gt;.run): abandoned only when it says so and was not
    /// a win. Null means there is no record. Any record that exists, even an empty one, ends the run,
    /// because the game deletes a save whose record file is there at all.
    /// </summary>
    public static RunEnding EndingOf(string? historyJson)
    {
        if (historyJson == null)
        {
            return RunEnding.None;
        }
        if (string.IsNullOrWhiteSpace(historyJson))
        {
            return RunEnding.Finished;
        }

        try
        {
            using var doc = JsonDocument.Parse(historyJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RunEnding.Finished; // any history file at all makes the game treat the run as ended
            }
            var abandoned = root.TryGetProperty("was_abandoned", out var a) && a.ValueKind == JsonValueKind.True;
            var won = root.TryGetProperty("win", out var w) && w.ValueKind == JsonValueKind.True;
            return abandoned && !won ? RunEnding.Abandoned : RunEnding.Finished;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // An unreadable record still makes the game treat the run as ended.
            return RunEnding.Finished;
        }
    }

    /// <summary>The more final of two endings, for a run with a record both here and in Steam Cloud.</summary>
    public static RunEnding Worst(RunEnding a, RunEnding b) => (RunEnding)Math.Max((int)a, (int)b);
}
