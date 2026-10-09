using SaveSlots.SaveSlotsCode.Core;
using Xunit;

namespace SaveSlots.Tests;

public class SlotRulesTests
{
    private const ulong Me = 76561190000000001;
    private const ulong Friend = 76561190000000002;

    private static RunSummary Run(long start = 100, long saved = 200, int schema = 20, params ulong[] players) =>
        new(start, saved, 600, 10, "standard", false, 0, ["ACT.UNDERDOCKS"], 5,
            (players.Length == 0 ? [Me, Friend] : players).Select(id => new RunPlayer(id, "CHARACTER.IRONCLAD", 50, 80)).ToList(),
            schema);

    [Fact]
    public void AnUnfinishedRunWithYouInItIsReady()
    {
        Assert.Equal(SlotState.Ready, SlotRules.StateOf(Run(), Me, RunEnding.None, 20));
    }

    [Fact]
    public void AnAbandonedRunCanBeActivated()
    {
        var state = SlotRules.StateOf(Run(), Me, RunEnding.Abandoned, 20);

        Assert.Equal(SlotState.Abandoned, state);
        Assert.True(SlotRules.CanActivate(state));
    }

    [Fact]
    public void AFinishedRunCannot()
    {
        var state = SlotRules.StateOf(Run(), Me, RunEnding.Finished, 20);

        Assert.Equal(SlotState.Finished, state);
        Assert.False(SlotRules.CanActivate(state));
    }

    [Fact]
    public void YouCannotHostARunYouAreNotIn()
    {
        Assert.Equal(SlotState.NotYourRun, SlotRules.StateOf(Run(players: [Friend, 42]), Me, RunEnding.None, 20));
    }

    [Fact]
    public void AnUnknownLocalIdDoesNotBlock()
    {
        // Steam not up yet: the game itself refuses later if you are not in the run.
        Assert.Equal(SlotState.Ready, SlotRules.StateOf(Run(players: [Friend]), 0, RunEnding.None, 20));
    }

    [Theory]
    [InlineData(21, 20, SlotState.NewerVersion)]
    [InlineData(12, 20, SlotState.Unreadable)]
    [InlineData(13, 20, SlotState.Ready)]
    [InlineData(21, 0, SlotState.Ready)] // latest schema unknown: do not guess
    public void SchemaVersionsOutsideWhatTheGameReadsAreRefused(int schema, int latest, SlotState expected)
    {
        Assert.Equal(expected, SlotRules.StateOf(Run(schema: schema), Me, RunEnding.None, latest));
    }

    [Fact]
    public void AnUnreadableFileIsUnreadable()
    {
        Assert.Equal(SlotState.Unreadable, SlotRules.StateOf(null, Me, RunEnding.None, 20));
    }

    [Fact]
    public void CopiesOfOneRunCollapseToTheLatestSave()
    {
        var slots = new[] { ("a", Run(start: 1, saved: 10)), ("b", Run(start: 1, saved: 30)), ("c", Run(start: 2, saved: 5)) };

        var kept = SlotRules.Newest(slots, s => s.Item2).Select(s => s.Item1).Order();

        Assert.Equal(["b", "c"], kept);
    }

    [Fact]
    public void UnreadableSlotsAreAllKept()
    {
        var slots = new[] { ("x", (RunSummary?)null), ("y", null) };

        Assert.Equal(2, SlotRules.Newest(slots, s => s.Item2).Count);
    }

    private static readonly (string Name, RunSummary? Run)[] Parked =
    [
        ("a-old", Run(start: 1, saved: 10)),
        ("a-new", Run(start: 1, saved: 30)),
        ("b", Run(start: 2, saved: 5)),
        ("junk", null),
    ];

    private static string[] Names(IEnumerable<(string Name, RunSummary? Run)> slots) => slots.Select(s => s.Name).Order().ToArray();

    [Fact]
    public void WithNoActiveRunTheNewestCopyOfEachRunIsListed()
    {
        Assert.Equal(["a-new", "b", "junk"], Names(SlotRules.Listed(Parked, s => s.Run, null)));
    }

    [Theory]
    [InlineData(30)] // the swap just made it active: the parked copy is the same save
    [InlineData(45)] // played on since
    public void ARunThatIsActiveIsNotListedAgain(long activeSaved)
    {
        var listed = SlotRules.Listed(Parked, s => s.Run, Run(start: 1, saved: activeSaved));

        Assert.Equal(["b", "junk"], Names(listed));
    }

    [Fact]
    public void AParkedCopyNewerThanTheActiveOneIsStillListed()
    {
        // The active copy is older (an older copy was made active by hand): the newer one stays reachable.
        var listed = SlotRules.Listed(Parked, s => s.Run, Run(start: 1, saved: 20));

        Assert.Equal(["a-new", "b", "junk"], Names(listed));
    }

    [Fact]
    public void OnlyCopiesTheGameHasSavedPastAreSuperseded()
    {
        Assert.Equal(["a-new", "a-old"], Names(SlotRules.Superseded(Parked, s => s.Run, Run(start: 1, saved: 31))));
    }

    [Fact]
    public void ACopyFromTheSameMomentAsTheActiveRunIsKept()
    {
        // Right after a swap the parked copy and the active save are the same; it may be the only copy left.
        Assert.Equal(["a-old"], Names(SlotRules.Superseded(Parked, s => s.Run, Run(start: 1, saved: 30))));
    }

    [Fact]
    public void NothingIsSupersededWithoutAnActiveRun()
    {
        Assert.Empty(SlotRules.Superseded(Parked, s => s.Run, null));
    }

    [Fact]
    public void TrashingARunTakesEveryCopyOfIt()
    {
        Assert.Equal(["a-new", "a-old"], Names(SlotRules.CopiesOf(Parked, s => s.Run, Parked[1])));
    }

    [Fact]
    public void TrashingAnUnreadableFileTakesOnlyThatFile()
    {
        Assert.Equal(["junk"], Names(SlotRules.CopiesOf(Parked, s => s.Run, Parked[3])));
    }

    [Fact]
    public void TheNewestCopyThisGameCanContinueIsListedOverANewerOneItCannot()
    {
        // A save from a newer game version sits beside the older one this version loads.
        var slots = new[] { ("newer-version", Run(start: 1, saved: 50, schema: 99)), ("loadable", Run(start: 1, saved: 40)) };

        var listed = SlotRules.Listed(slots, s => s.Item2, null, s => s.Item2!.SchemaVersion <= 20);

        Assert.Equal(["loadable"], listed.Select(s => s.Item1));
    }

    [Fact]
    public void WhenNoCopyIsUsableTheNewestIsStillListed()
    {
        var slots = new[] { ("a", Run(start: 1, saved: 50)), ("b", Run(start: 1, saved: 40)) };

        Assert.Equal(["a"], SlotRules.Newest(slots, s => s.Item2, _ => false).Select(s => s.Item1));
    }

    [Fact]
    public void WithoutCloudSyncThisPcDecides()
    {
        var local = Run(start: 1, saved: 30);

        Assert.Same(local, SlotRules.Agreed(local, cloud: null, cloudSync: false));
    }

    [Fact]
    public void ARunTheCloudDoesNotHoldIsNotSafelyActive()
    {
        // The next launch deletes a local save the cloud lacks, so its parked copies must stay listed.
        Assert.Null(SlotRules.Agreed(Run(start: 1, saved: 30), cloud: null, cloudSync: true));
        Assert.Null(SlotRules.Agreed(Run(start: 1, saved: 30), Run(start: 2, saved: 99), cloudSync: true));
    }

    [Fact]
    public void WhenTheCloudIsBehindItsOlderSaveIsWhatSurvives()
    {
        var cloud = Run(start: 1, saved: 20);

        Assert.Same(cloud, SlotRules.Agreed(Run(start: 1, saved: 30), cloud, cloudSync: true));
    }

    [Fact]
    public void ACopyIsOnlyClearedOnceTheCloudHasSavedPastIt()
    {
        // Played on locally (save 45) but the cloud still holds save 30: the copy at 30 is kept.
        var agreed = SlotRules.Agreed(Run(start: 1, saved: 45), Run(start: 1, saved: 30), cloudSync: true);

        Assert.Equal(["a-old"], Names(SlotRules.Superseded(Parked, s => s.Run, agreed)));
    }

    [Fact]
    public void NoActiveRunAgreesWithNothing()
    {
        Assert.Null(SlotRules.Agreed(null, Run(), cloudSync: true));
    }

    [Fact]
    public void EveryCopyOfARunGetsItsOwnFileName()
    {
        Assert.Equal("1700000000-1700001701.save", SlotRules.FileName(Run(start: 1700000000, saved: 1700001701)));
        Assert.Equal("1-2-1.save", SlotRules.FileName(Run(start: 1, saved: 2), attempt: 1));
        Assert.NotEqual(SlotRules.FileName(Run(start: 1, saved: 2)), SlotRules.FileName(Run(start: 1, saved: 3)));
    }
}

public class SlotTextTests
{
    private const ulong Me = 1;

    [Theory]
    [InlineData("CHARACTER.IRONCLAD", "Ironclad")]
    [InlineData("ACT.UNDERDOCKS", "Underdocks")]
    [InlineData("CHARACTER.THE_REGENT", "The Regent")]
    [InlineData("standard", "Standard")]
    [InlineData("", "?")]
    public void PrettyNamesFromIds(string id, string expected)
    {
        Assert.Equal(expected, SlotText.Pretty(id));
    }

    [Fact]
    public void ProgressReadsActFloorAndAscension()
    {
        var run = new RunSummary(1, 2, 3, 10, "standard", false, 1, ["ACT.UNDERDOCKS", "ACT.HIVE"], 20, [], 20);

        Assert.Equal("Act 2 · Hive · floor 20 · A10", SlotText.Progress(run));
    }

    [Fact]
    public void ProgressNamesADailyOrCustomMode()
    {
        var daily = new RunSummary(1, 2, 3, 0, "standard", true, 0, ["ACT.UNDERDOCKS"], 4, [], 20);
        var custom = new RunSummary(1, 2, 3, 0, "custom", false, 0, ["ACT.UNDERDOCKS"], 4, [], 20);

        Assert.Equal("Act 1 · Underdocks · floor 4 · Daily", SlotText.Progress(daily));
        Assert.EndsWith("· Custom", SlotText.Progress(custom));
    }

    [Fact]
    public void PlayersListYouFirstThenNames()
    {
        var run = new RunSummary(1, 2, 3, 0, "standard", false, 0, [], 0,
            [new RunPlayer(7, "CHARACTER.DEFECT", 1, 1), new RunPlayer(Me, "CHARACTER.IRONCLAD", 1, 1), new RunPlayer(9, "CHARACTER.SILENT", 1, 1)], 20);

        var text = SlotText.Players(run, Me, id => id == 7 ? "Friend" : null);

        Assert.Equal("You (Ironclad), Friend (Defect), Unknown player (Silent)", text);
    }

    [Fact]
    public void SavedTimeIsShownInTheGivenZone()
    {
        Assert.Equal("Saved 14 Nov, 22:13", SlotText.Saved(1700000000, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1700000000000)] // milliseconds, not seconds
    [InlineData(long.MaxValue)]
    public void AnUnknownOrImpossibleSaveTimeSaysSo(long unix)
    {
        Assert.Equal("Saved at an unknown time", SlotText.Saved(unix, TimeZoneInfo.Utc));
    }

    [Fact]
    public void EveryBlockedStateExplainsItself()
    {
        foreach (var state in Enum.GetValues<SlotState>().Where(s => s != SlotState.Ready))
        {
            Assert.False(string.IsNullOrWhiteSpace(SlotText.StateNote(state)));
        }
        Assert.Null(SlotText.StateNote(SlotState.Ready));
    }
}
