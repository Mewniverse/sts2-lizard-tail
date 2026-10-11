using LizardTail.LizardTailCode.Core;
using Xunit;

namespace LizardTail.Tests;

public class SaveKindTests
{
    private const ulong Me = 76561190000000001;
    private const ulong Friend = 76561190000000002;

    private static RunSummary Run(params (ulong Id, string Character)[] players) =>
        new(100, 200, 600, 10, "standard", false, 0, ["ACT.UNDERDOCKS"], 5,
            players.Select(p => new RunPlayer(p.Id, p.Character, 50, 80)).ToList(), 20);

    [Fact]
    public void MultiplayerRunsStayWhereTheyAlwaysWere()
    {
        // Runs parked by earlier versions sit directly in the profile's folder and must still be found.
        Assert.Equal("mod_data/LizardTail/profile1", SaveKinds.Folder(SaveKind.Multiplayer, "mod_data/LizardTail/profile1"));
    }

    [Fact]
    public void SoloRunsGetAFolderOfTheirOwn()
    {
        Assert.Equal("mod_data/LizardTail/profile1/solo", SaveKinds.Folder(SaveKind.Solo, "mod_data/LizardTail/profile1"));
        Assert.Equal("mod_data/LizardTail/modded/profile2/solo", SaveKinds.Folder(SaveKind.Solo, "mod_data/LizardTail/modded/profile2"));
    }

    [Fact]
    public void TheTwoKindsNeverShareAFolder()
    {
        var folders = Enum.GetValues<SaveKind>().Select(k => SaveKinds.Folder(k, "root/profile1")).ToList();

        Assert.Equal(folders.Count, folders.Distinct().Count());
    }

    [Fact]
    public void OnlyAMultiplayerRunNeedsYouInIt()
    {
        Assert.True(SaveKinds.NeedsYouInIt(SaveKind.Multiplayer));
        Assert.False(SaveKinds.NeedsYouInIt(SaveKind.Solo));
    }

    [Fact]
    public void ASoloRunSavedUnderAnotherIdIsStillReady()
    {
        // The solo list leaves the local id out (0), as SlotStore does for a kind that does not need you in it.
        var mustBeIn = SaveKinds.NeedsYouInIt(SaveKind.Solo) ? Me : 0;

        Assert.Equal(SlotState.Ready, SlotRules.StateOf(Run((Friend, "CHARACTER.SILENT")), mustBeIn, RunEnding.None, 20));
    }

    [Fact]
    public void TheGamesOwnButtonsAreNamedPerKind()
    {
        Assert.Equal("Load", SaveKinds.ContinueButton(SaveKind.Multiplayer));
        Assert.Equal("Host", SaveKinds.NewRunButton(SaveKind.Multiplayer));
        Assert.Equal("Continue", SaveKinds.ContinueButton(SaveKind.Solo));
        Assert.Equal("Singleplayer", SaveKinds.NewRunButton(SaveKind.Solo));
    }

    [Fact]
    public void ASoloRunShowsItsCharacterNotAPlayerList()
    {
        var run = Run((Me, "CHARACTER.THE_REGENT"));

        Assert.Equal("The Regent", SlotText.Who(SaveKind.Solo, run, Me, _ => "Moon"));
        Assert.Equal("You (The Regent)", SlotText.Who(SaveKind.Multiplayer, run, Me, _ => "Moon"));
    }

    [Fact]
    public void ARunWithNoPlayersStillHasARow()
    {
        Assert.Equal("Unknown character", SlotText.Characters(Run()));
    }

    [Fact]
    public void TheMultiplayerWordingIsUnchanged()
    {
        Assert.Equal("Multiplayer runs", SlotText.Title(SaveKind.Multiplayer));
        Assert.Equal("Load continues the active run. Park it to host a new one; make any parked run active to continue it.", SlotText.Hint(SaveKind.Multiplayer));
        Assert.Equal("No active run. Host starts a new one.", SlotText.NoActive(SaveKind.Multiplayer));
        Assert.Equal("Parked. Host now starts a new run.", SlotText.Parked(SaveKind.Multiplayer));
        Assert.Equal("Parked, but Steam Cloud kept the old save, so it comes back at the next launch unless you host first.", SlotText.ParkedButCloudKept(SaveKind.Multiplayer));
        Assert.Equal("Load continues this run.", SlotText.ContinuesIt(SaveKind.Multiplayer));
        Assert.Equal("The game's multiplayer save cannot be read. Press Load once so the game sets it aside; the mod parks a copy of whatever it holds.", SlotText.Unreadable(SaveKind.Multiplayer));
        Assert.Equal("The game's save file is damaged; this is its backup. Press Load once so the game repairs it or sets it aside before parking.", SlotText.Damaged(SaveKind.Multiplayer));
    }

    [Fact]
    public void TheSoloWordingNamesTheMainMenusButtons()
    {
        Assert.Equal("Solo runs", SlotText.Title(SaveKind.Solo));
        Assert.StartsWith("Continue ", SlotText.Hint(SaveKind.Solo));
        Assert.Equal("No active run. Singleplayer starts a new one.", SlotText.NoActive(SaveKind.Solo));
        Assert.Equal("Parked. Singleplayer now starts a new run.", SlotText.Parked(SaveKind.Solo));
        Assert.StartsWith("Continue ", SlotText.ContinuesIt(SaveKind.Solo));
    }

    [Theory]
    [InlineData(SaveKind.Solo)]
    [InlineData(SaveKind.Multiplayer)]
    public void NoSoloTextMentionsMultiplayerAndTheReverse(SaveKind kind)
    {
        var other = kind == SaveKind.Solo ? new[] { "multiplayer", "Load", "Host", "host" } : ["solo", "Continue ", "Singleplayer", "main menu"];
        var texts = new[]
        {
            SlotText.Title(kind), SlotText.Hint(kind), SlotText.NoActive(kind), SlotText.Parked(kind), SlotText.ParkedButCloudKept(kind),
            SlotText.ContinuesIt(kind), SlotText.Unreadable(kind), SlotText.Damaged(kind), SlotText.HowTheGameRepairs(kind),
        };

        foreach (var text in texts)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain(other, word => text.Contains(word, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(true, 0, "Hide")]
    [InlineData(true, 3, "Hide")]
    [InlineData(false, 0, "Show")]
    [InlineData(false, 2, "Show (2 parked)")]
    public void AClosedPanelSaysHowManyRunsAreParked(bool open, int parked, string expected)
    {
        Assert.Equal(expected, SlotText.Toggle(open, parked));
    }
}
