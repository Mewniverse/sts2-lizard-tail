using LizardTail.LizardTailCode.Core;
using Xunit;

namespace LizardTail.Tests;

public class SaveSharingTests
{
    [Fact]
    public void ModsThatLeaveGameplayAloneShareTheNormalProfile()
    {
        Assert.True(SaveSharing.CanShare([false, false]));
    }

    [Fact]
    public void OneGameplayModKeepsTheModdedProfile()
    {
        Assert.False(SaveSharing.CanShare([false, true, false]));
    }

    [Fact]
    public void AModThatDoesNotSayCountsAsAffectingGameplay()
    {
        // The game treats a missing manifest or flag as affecting gameplay; so does this rule.
        Assert.False(SaveSharing.CanShare([false, null]));
    }

    [Fact]
    public void WithNoModsLoadedThereIsNothingToShare()
    {
        Assert.False(SaveSharing.CanShare([]));
    }
}
