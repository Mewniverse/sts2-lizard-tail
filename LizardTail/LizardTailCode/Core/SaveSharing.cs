namespace LizardTail.LizardTailCode.Core;

/// <summary>When the game can keep using the normal profile although mods are loaded.</summary>
public static class SaveSharing
{
    /// <summary>
    /// True when at least one mod is loaded and none of them affects gameplay. A mod whose manifest does
    /// not say counts as affecting gameplay, as the game itself counts it.
    /// </summary>
    public static bool CanShare(IEnumerable<bool?> affectsGameplay)
    {
        var any = false;
        foreach (var affects in affectsGameplay)
        {
            if (affects != false)
            {
                return false;
            }
            any = true;
        }
        return any;
    }
}
