# Save Slots

A Slay the Spire 2 mod that keeps several multiplayer runs instead of one. The repository is named
after Lizard Tail, the relic that brings you back from the dead, since that is what the mod does for an
abandoned run.

The game has a single multiplayer save. If your groups change from night to night, with different
friends and different subsets of them, you either finish a run in one sitting or abandon it to host
another. This mod adds a **Multiplayer runs** panel to the multiplayer menu. Park the active run,
host a new one, and later make the old run active again and press Load to continue it.

## Using it

Open **Multiplayer** from the main menu. The panel on the right lists:

- **Active**: the run the game's **Load** button continues, with who is in it (Steam names and
  characters), the act, floor and ascension, and when it was last saved. **Park this run** moves it
  into the parked list and frees the slot, so **Host** starts a new run.
- **Parked**: every run you have parked. **Make active** swaps it into the game's slot; whatever was
  active is parked first, so a swap never loses a run. **Trash** (press twice) moves a parked run to
  a trash folder.

With a controller, move right from the **Join** tile into the panel, and left to go back.

**Make active** checks the run before changing anything: that you are one of its players, that the
game can load it with your current version and mods, and that it was not won or lost. If any check
fails, nothing changes and the panel says why. The parked copy stays in the folder until the game
has saved newer progress of that run, so a swap that did not take can always be retried.

A parked run can show a note instead of being ready:

| Note | Meaning |
|---|---|
| Abandoned | You abandoned it from the menu. **Make active** picks it up again (see below). |
| This run ended | It was won or lost. The game deletes the save of a finished run, so it cannot be continued. |
| You are not in this run | Only a player in a run's save can host it. |
| Saved by a newer version | The game cannot read it until you update. |
| This file is not a run save this mod can read | A damaged save the mod kept before the game removed or set it aside. It cannot be made active; Trash moves it out of the list. |

### Abandoning by mistake

If you press the game's own **Abandon**, the mod keeps a copy of the run in the parked list first.
That includes a save the game cannot load (from a newer game version, or with content from a mod you
turned off): the game sets those aside as `.corrupt` files, and the mod parks a copy before it does.
Such a copy shows as saved by a newer version, or fails Make active's load check, until you update the
game or turn that mod back on.

For a run the game could load, **Make active** brings it back: the game wrote an "abandoned" record
into your run history, which would make it delete the save again at the next launch, so the mod moves
that record aside (it keeps a copy) and the run continues. Your profile stats already counted the abandon and are not changed
back. A run that was won or lost is never copied.

### Who can play a parked run

The game's own rules apply when you press **Load**:

- You must be one of the run's players, because only someone in the save can host it.
- Only the run's players can join. Nobody new can be added to a run in progress.
- Any two or more of them can continue. The game asks you to confirm when someone is missing, and
  the missing players stay in the run for next time.

## Where things are kept

Parked runs are files under `%APPDATA%\SlayTheSpire2\steam\<your id>\mod_data\SaveSlots\`, in a
folder named like the game's own profile folder: `modded\profile<N>\` while mods are loaded, or
`profile<N>\` when a mod (Spirebird's shared save paths, for example) makes modded play use the normal
profile. The game's log names the exact folder when the multiplayer menu first opens.

- `slots\`: parked runs, one file per copy (`<run start>-<save time>.save`). Nothing here is ever
  overwritten.
- `trash\`: runs you trashed, and older copies of a run the game has since saved past.
- `revived-history\`: abandon records moved aside.

Damaged saves the mod kept are named `damaged-…` or `set-aside-…` instead. One more file sits directly
in `mod_data\SaveSlots\`: `names.json`, the Steam names of the people in your runs, remembered so they
still show when Steam is offline or someone leaves your friends list.

They stay on this PC. Steam Cloud does not sync them, and the game never cleans that folder.

The active run is changed only through the game's own save store, so Steam Cloud stays in step. A
plain file swap would be undone at the next launch, when the game syncs the cloud copy back. After a
swap the mod checks that Steam Cloud took the new run, and that a revived run's abandon record is gone
from the cloud as well as this PC. Being offline is not a problem (Steam queues the change), but if
Steam refuses the write, usually because its cloud space for the game is full, the panel says so: the
next launch would undo the swap. The run stays parked and visible, so it can be made active again
after freeing space or turning off Steam Cloud for the game. Until the cloud has a run, the panel marks
it, and none of its parked copies are hidden or cleared.

Swaps are refused while they would be unsafe: once Load or Host has been pressed, with a popup open,
during a run, or while the game is saving. They are also refused while the game's own save file is
damaged (the panel then shows its backup); pressing Load once lets the game repair it or set it aside.

## Installing

Put `SaveSlots.dll` and `SaveSlots.json` (from a [release](https://github.com/jerryhebert/sts2-lizard-tail/releases),
or built yourself as below) in a `mods/SaveSlots/` folder inside your Slay the Spire 2 install, and
restart the game. The mod declares `affects_gameplay: false`, so your friends do not need
it to join your games.

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) (9.0.318 or a later 9.0 release,
pinned in `global.json`) and a copy of the game, whose assemblies the mod compiles against. The project
finds a Steam install automatically; if yours lives elsewhere, copy
`SaveSlots/Directory.Build.props.example` to `SaveSlots/Directory.Build.props` (it is git-ignored) and
set `Sts2Path`. Building also installs the mod into the game's `mods` folder (skipped with a warning
while the game is running).

```bash
dotnet build SaveSlots/SaveSlots.csproj -c Release
```

```bash
pwsh ./scripts/Run-Checks.ps1
```

Runs the unit tests, the dependency audit and a Release build. Add `-SkipBuild` on a machine without the
game installed; the unit tests need only the SDK.

The pure logic (reading saves, history records, which copies are listed or stale, file names,
wording) is in `SaveSlots/SaveSlotsCode/Core` and is unit-tested without the game.
`SaveSlotsCode/Game` holds the save-store operations, the picker and the Harmony patches.

## License and attribution

[MIT](LICENSE). Slay the Spire 2 is the property of Mega Crit; this project is an unaffiliated fan mod
and contains no game code or assets. The project was scaffolded from
[Alchyr's mod template](https://github.com/Alchyr/ModTemplate-StS2).
