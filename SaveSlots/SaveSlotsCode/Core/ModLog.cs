namespace SaveSlots.SaveSlotsCode.Core;

/// <summary>Minimal logging seam so the pure logic does not depend on the game's logger.</summary>
public interface ISaveSlotsLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>Discards everything. The default outside the game, e.g. in unit tests.</summary>
public sealed class NullLog : ISaveSlotsLog
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}

/// <summary>Named ModLog rather than Log to avoid colliding with the game's own logging class.</summary>
public static class ModLog
{
    public static ISaveSlotsLog Current { get; set; } = new NullLog();

    public static void Info(string message) => Current.Info(message);
    public static void Warn(string message) => Current.Warn(message);
    public static void Error(string message) => Current.Error(message);
}
