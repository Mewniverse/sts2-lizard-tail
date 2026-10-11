using LizardTail.LizardTailCode.Core;
using Xunit;

namespace LizardTail.Tests;

public class RunSummaryTests
{
    // Modeled on a current_run_mp.save (schema 20), trimmed to the fields the picker reads.
    private const string ThreePlayerSave = """
        {
          "acts": [{"id":"ACT.UNDERDOCKS"},{"id":"ACT.HIVE"},{"id":"ACT.GLORY"}],
          "ascension": 10,
          "current_act_index": 1,
          "game_mode": "standard",
          "map_point_history": [[{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{}],[{},{},{}]],
          "players": [
            {"character_id":"CHARACTER.IRONCLAD","current_hp":17,"max_hp":80,"net_id":76561190000000001},
            {"character_id":"CHARACTER.SILENT","current_hp":40,"max_hp":70,"net_id":76561190000000002},
            {"character_id":"CHARACTER.NECROBINDER","current_hp":55,"max_hp":66,"net_id":76561190000000003}
          ],
          "run_time": 1701,
          "save_time": 1700001701,
          "schema_version": 20,
          "start_time": 1700000000
        }
        """;

    [Fact]
    public void ReadsTheFieldsThePickerShows()
    {
        var run = RunSummary.TryParse(ThreePlayerSave)!;

        Assert.Equal(1700000000, run.StartTime);
        Assert.Equal(1700001701, run.SaveTime);
        Assert.Equal(10, run.Ascension);
        Assert.Equal(1, run.CurrentActIndex);
        Assert.Equal("ACT.HIVE", run.CurrentActId);
        Assert.Equal(20, run.Floor); // 17 rooms in act 1 + 3 in act 2
        Assert.Equal(3, run.Players.Count);
        Assert.Equal(20, run.SchemaVersion);
        Assert.False(run.IsDaily);
        Assert.True(run.HasPlayer(76561190000000002));
    }

    [Fact]
    public void SteamIdsAboveLongMaxStillParse()
    {
        // Steam id64s fit in a long today, but net_id is unsigned in the game.
        var run = RunSummary.TryParse("""{"start_time":1,"players":[{"net_id":18446744073709551615,"character_id":"CHARACTER.DEFECT"}]}""")!;

        Assert.Equal(ulong.MaxValue, run.Players[0].NetId);
    }

    [Fact]
    public void ADailyRunIsMarked()
    {
        var run = RunSummary.TryParse("""{"start_time":1,"dailyTime":1791400000}""")!;

        Assert.True(run.IsDaily);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"players":[]}""")]
    public void AnythingThatIsNotARunSaveIsNull(string? json)
    {
        Assert.Null(RunSummary.TryParse(json));
    }

    [Fact]
    public void AnAbandonRecordCanBePickedUpAgain()
    {
        Assert.Equal(RunEnding.Abandoned, HistoryRecord.EndingOf("""{"was_abandoned":true,"win":false}"""));
    }

    [Theory]
    [InlineData("""{"was_abandoned":false,"win":false}""")]
    [InlineData("""{"was_abandoned":false,"win":true}""")]
    [InlineData("""{"was_abandoned":true,"win":true}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("")]
    [InlineData("  ")]
    public void WinsLossesAndUnreadableRecordsAreFinished(string json)
    {
        Assert.Equal(RunEnding.Finished, HistoryRecord.EndingOf(json));
    }

    [Fact]
    public void NoRecordMeansTheRunHasNotEnded()
    {
        Assert.Equal(RunEnding.None, HistoryRecord.EndingOf(null));
    }

    [Theory]
    [InlineData(RunEnding.None, RunEnding.Abandoned, RunEnding.Abandoned)]
    [InlineData(RunEnding.Abandoned, RunEnding.Finished, RunEnding.Finished)]
    [InlineData(RunEnding.Finished, RunEnding.None, RunEnding.Finished)]
    [InlineData(RunEnding.None, RunEnding.None, RunEnding.None)]
    public void ARecordHereAndInTheCloudCountsAsTheMoreFinal(RunEnding here, RunEnding cloud, RunEnding expected)
    {
        Assert.Equal(expected, HistoryRecord.Worst(here, cloud));
    }
}
