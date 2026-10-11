using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using LizardTail.LizardTailCode.Core;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;

namespace LizardTail.LizardTailCode;

/// <summary>Routes the mod's logging seam to the game's logger.</summary>
internal sealed class GameLog : ILizardTailLog
{
    private readonly Logger _logger = new(LizardTailMod.ModId, LogType.Generic);

    public void Info(string message) => _logger.Info(message);
    public void Warn(string message) => _logger.Warn(message);
    public void Error(string message) => _logger.Error(message);
}

/// <summary>Mod entry point. The game calls <see cref="Initialize"/> once after loading the assembly.</summary>
[ModInitializer(nameof(Initialize))]
public static class LizardTailMod
{
    public const string ModId = "LizardTail";

    public static void Initialize()
    {
        ModLog.Current = new GameLog();
        ModLog.Info("Initializing Lizard Tail");

        // Registers this assembly's Node subclasses (the picker, its buttons) with Godot so their
        // overrides are called.
        try
        {
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly());
        }
        catch (Exception ex)
        {
            ModLog.Warn($"Could not register mod scripts with Godot: {ex.Message}");
        }

        var harmony = new Harmony(ModId);
        // One class at a time: a patch that fails to apply disables only its own feature.
        foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
        {
            if (!type.HasHarmonyAttribute())
            {
                continue;
            }
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                ModLog.Error($"Patch class {type.Name} failed to apply, so its feature is off: {ex.Message}");
            }
        }

        var patched = harmony.GetPatchedMethods().Select(m => $"{m.DeclaringType?.Name}.{m.Name}").ToHashSet();
        var expected = new[]
        {
            "NMultiplayerSubmenu._Ready", "NMultiplayerSubmenu.OnSubmenuShown",
            "NMultiplayerSubmenu.UpdateButtons", "SaveManager.DeleteCurrentMultiplayerRun",
            "RunSaveManager.RenameBrokenMultiplayerRunSave", "MigrationManager.PreserveCorruptFile",
            "NMainMenu._Ready", "NMainMenu.RefreshButtons", "SaveManager.DeleteCurrentRun",
            "UserDataPathProvider.get_IsRunningModded", "NGame.CheckShowModdedSaveFilePopup",
        };
        var missing = expected.Where(e => !patched.Contains(e)).ToList();
        if (missing.Count > 0)
        {
            ModLog.Error($"These patches did not apply, so the features behind them are off: {string.Join(", ", missing)}");
        }
        ModLog.Info($"Lizard Tail ready ({patched.Count} patches)");
    }
}
