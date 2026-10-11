using System.Globalization;
using System.Text.Json;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// Steam names for the people in a run. Saves hold only Steam ids, so names come from Steam when it
/// knows them and are remembered in mod_data, so a friend's name still shows after they leave your
/// friends list or when Steam is offline.
/// </summary>
public static class PlayerNames
{
    /// <summary>What Steam's GetFriendPersonaName returns for an id it has no persona data for.</summary>
    private const string UnknownName = "[unknown]";

    private static Dictionary<string, string>? _known;

    public static string? NameOf(ulong netId)
    {
        var known = Known();
        var key = netId.ToString(CultureInfo.InvariantCulture);

        try
        {
            if (SteamInitializer.Initialized)
            {
                var name = PlatformUtil.GetPlayerNameRaw(PlatformType.Steam, netId);
                // Steam answers with the id itself, or "[unknown]", when it has no name for someone.
                if (!string.IsNullOrWhiteSpace(name) && name != key && name != UnknownName)
                {
                    if (!known.TryGetValue(key, out var old) || old != name)
                    {
                        known[key] = name;
                        Save();
                    }
                    return name;
                }
            }
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not look up a Steam name: {ex.Message}");
        }

        return known.TryGetValue(key, out var cached) ? cached : null;
    }

    /// <summary>Looks up everyone in a run now, while Steam is likely to know them.</summary>
    public static void Remember(RunSummary run)
    {
        foreach (var player in run.Players)
        {
            NameOf(player.NetId);
        }
    }

    private static Dictionary<string, string> Known()
    {
        if (_known != null)
        {
            return _known;
        }

        try
        {
            var text = SlotStore.Local.FileExists(SlotStore.NamesPath) ? SlotStore.Local.ReadFile(SlotStore.NamesPath) : null;
            _known = text == null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(text) ?? new();
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not read remembered player names: {ex.Message}");
            _known = new();
        }
        return _known;
    }

    private static void Save()
    {
        try
        {
            SlotStore.WriteLocal(SlotStore.NamesPath, JsonSerializer.Serialize(_known, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not remember player names: {ex.Message}");
        }
    }
}
