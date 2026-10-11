namespace LizardTail.LizardTailCode.Core;

/// <summary>Which of the game's two run saves a picker manages.</summary>
public enum SaveKind
{
    /// <summary>current_run_mp.save: the run the multiplayer menu's Load continues.</summary>
    Multiplayer,
    /// <summary>current_run.save: the run the main menu's Continue continues.</summary>
    Solo,
}

/// <summary>What differs between the two saves, apart from the file itself.</summary>
public static class SaveKinds
{
    /// <summary>
    /// The mod's folder for one kind of run, given its folder for the profile. Multiplayer runs sit
    /// directly in it, where they always have; solo runs get a folder of their own beneath, so the two
    /// lists never mix and a solo run is never offered to Load.
    /// </summary>
    public static string Folder(SaveKind kind, string profileFolder) =>
        kind == SaveKind.Solo ? $"{profileFolder}/solo" : profileFolder;

    /// <summary>"solo" or "multiplayer", as the panel and the log name the kind of run.</summary>
    public static string Name(SaveKind kind) => kind == SaveKind.Solo ? "solo" : "multiplayer";

    /// <summary>The game's own button that continues the active run.</summary>
    public static string ContinueButton(SaveKind kind) => kind == SaveKind.Solo ? "Continue" : "Load";

    /// <summary>The game's own button that starts a new run once the slot is free.</summary>
    public static string NewRunButton(SaveKind kind) => kind == SaveKind.Solo ? "Singleplayer" : "Host";

    /// <summary>
    /// Whether a run can only be continued by one of its players. True for multiplayer, where only a
    /// player in the save can host it. A solo save is continued whoever it names: the game's Continue
    /// never looks at the player id.
    /// </summary>
    public static bool NeedsYouInIt(SaveKind kind) => kind == SaveKind.Multiplayer;
}
