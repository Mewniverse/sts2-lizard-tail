using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// Keeps the game on your normal profile while every loaded mod leaves gameplay alone.
///
/// The game switches to a separate modded profile as soon as any mod loads. Its one-time copy of your
/// normal saves into that profile is then undone by the Steam Cloud sync at the same startup, which
/// deletes local files the cloud has never seen, so a first modded launch lands on a blank profile at
/// ascension 0. Mods that cannot change gameplay have no reason to be kept apart from your progress.
///
/// Spirebird's "unify save paths" option patches the same getter to always answer false. Both patches
/// only ever turn the answer to false, never to true, so they work together in either order: with
/// both, Spirebird's wins whenever it is on; without it, this rule decides.
/// </summary>
public static class SharedSaves
{
    private static bool? _applies;

    /// <summary>
    /// True once mods have finished loading if none of the loaded ones affects gameplay. Decided once:
    /// the game loads mods only at startup.
    /// </summary>
    public static bool Applies
    {
        get
        {
            if (_applies is { } known)
            {
                return known;
            }
            if (ModManager.State != ModManagerState.Initialized)
            {
                return false; // mods are still loading; leave the game's answer alone until they are all in
            }

            var loaded = ModManager.GetLoadedMods().ToList();
            _applies = SaveSharing.CanShare(loaded.Select(m => m.manifest?.affectsGameplay));
            ModLog.Info(_applies.Value
                ? $"None of the {loaded.Count} loaded mods affects gameplay, so the game keeps your normal saves"
                : "A loaded mod affects gameplay, so the game keeps its separate modded saves");
            return _applies.Value;
        }
    }

    /// <summary>True when the game is using the normal profile now, whichever mod arranged it.</summary>
    public static bool UsingNormalSaves => UserDataPathProvider.GetAccountDir().Length == 0;
}

/// <summary>The one place the game decides between the normal and the modded profile folders.</summary>
[HarmonyPatch(typeof(UserDataPathProvider), nameof(UserDataPathProvider.IsRunningModded), MethodType.Getter)]
public static class SharedSavesPatch
{
    private static void Postfix(ref bool __result)
    {
        if (__result && SharedSaves.Applies)
        {
            __result = false;
        }
    }
}

/// <summary>
/// The game tells you on a first modded launch that your saves were copied to a separate modded
/// profile. Skipped while that profile is not in use, since the message would be wrong.
/// </summary>
[HarmonyPatch(typeof(NGame), "CheckShowModdedSaveFilePopup")]
public static class SharedSavesPopupPatch
{
    private static bool Prefix() => !SharedSaves.UsingNormalSaves;
}
