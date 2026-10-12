# Lizard Tail

A Slay the Spire 2 mod that keeps several runs instead of one, multiplayer and solo. It is named after
Lizard Tail, the relic that brings you back from the dead, since that is what it does for an abandoned
run.

The game has a single multiplayer save. If your groups change from night to night, with different
friends and different subsets of them, you either finish a run in one sitting or abandon it to host
another. This mod adds a **Multiplayer runs** panel to the multiplayer menu. Park the active run,
host a new one, and later make the old run active again and press Load to continue it.

The game's solo save is single too, so a **Solo runs** panel on the main menu does the same for it:
park the run **Continue** would pick up, start another, and swap back later.

## Using it

Open **Multiplayer** from the main menu. The panel on the right lists:

- **Active**: the run the game's **Load** button continues, with who is in it (Steam names and
  characters), the act, floor and ascension, and when it was last saved. **Park this run** moves it
  into the parked list and frees the slot, so **Host** starts a new run.
- **Parked**: every run you have parked. **Make active** swaps it into the game's slot; whatever was
  active is parked first, so a swap never loses a run. **Trash** (press twice) moves a parked run to
  a trash folder.

With a controller, move right from the **Join** tile into the panel, and left to go back.

### Solo runs

The main menu has a **Solo runs** panel at its right edge. It starts closed, as a title and a **Show**
button that says how many runs are parked, and hides whenever the main menu's own buttons do. Open, it
works like the multiplayer one, for the solo save:

- **Active** is the run the main menu's **Continue** picks up. **Park this run** frees the slot, so
  the menu shows **Singleplayer** again in place of Continue and Abandon Run.
- **Parked** lists solo runs only. The two panels never show each other's runs.

A solo run has no other players, so its row shows the character, and nothing checks whose run it is.
With a controller, move right from the main menu's buttons into the panel, and left to go back.

### Checks before a swap

**Make active** checks the run before changing anything: that you are one of its players (multiplayer
only), that the game can load it with your current version and mods, and that it was not won or lost.
If any check
fails, nothing changes and the panel says why. The parked copy stays in the folder until the game
has saved newer progress of that run, so a swap that did not take can always be retried.

A parked run can show a note instead of being ready:

| Note | Meaning |
|---|---|
| Abandoned | You abandoned it from the menu. **Make active** picks it up again (see below). |
| This run ended | It was won or lost. The game deletes the save of a finished run, so it cannot be continued. |
| You are not in this run | Multiplayer only: only a player in a run's save can host it. |
| Saved by a newer version | The game cannot read it until you update. |
| This file is not a run save this mod can read | A damaged save the mod kept before the game removed or set it aside. It cannot be made active; Trash moves it out of the list. |

### Abandoning by mistake

If you press the game's own **Abandon** (or **Abandon Run**, for a solo run, on the main menu or from
inside the run), the mod keeps a copy of the run in that kind's parked list first.
That includes a save the game cannot load (from a newer game version, or with content from a mod you
turned off): the game sets those aside as `.corrupt` files, and the mod parks a copy before it does.
Such a copy shows as saved by a newer version, or fails Make active's load check, until you update the
game or turn that mod back on.

For a run the game could load, **Make active** brings it back: the game wrote an "abandoned" record
into your run history, which would make it delete the save again at the next launch, so the mod moves
that record aside (it keeps a copy) and the run continues. Your profile stats already counted the abandon and are not changed
back. A run that was won or lost is never copied.

### Who can play a parked run

The game's own rules apply when you press **Load** on a multiplayer run:

- You must be one of the run's players, because only someone in the save can host it.
- Only the run's players can join. Nobody new can be added to a run in progress.
- Any two or more of them can continue. The game asks you to confirm when someone is missing, and
  the missing players stay in the run for next time.

## Your normal saves stay in use

Out of the box, the game switches to a separate, modded profile as soon as any mod loads. Its
one-time copy of your progress into that profile does not survive the Steam Cloud sync at the same
launch, so the first modded launch can land on a blank profile at ascension 0.

Lizard Tail keeps the game on your normal profile, with your progress and your normal run saves,
as long as every loaded mod declares that it does not affect gameplay. Load any mod that does, and the
game keeps its separate modded profile as usual. Spirebird's "unify save paths" option does the same
for every mod; with both installed, whichever applies keeps the normal profile, and they never work
against each other.

## Where things are kept

Parked runs are files under `%APPDATA%\SlayTheSpire2\steam\<your id>\mod_data\LizardTail\`, in a
folder named like the game's own profile folder: `profile<N>\` while the normal profile is in use, or
`modded\profile<N>\` while the game keeps separate modded saves. The panel lists runs parked under
either, so switching between them never hides one. Solo runs are kept apart, in a `solo` folder inside
that profile folder with the same three folders in it. The game's log names the exact folders when the
main menu and the multiplayer menu first open.

- `slots\`: parked runs, one file per copy (`<run start>-<save time>.save`). Nothing here is ever
  overwritten.
- `trash\`: runs you trashed, and older copies of a run the game has since saved past.
- `revived-history\`: abandon records moved aside.

Damaged saves the mod kept are named `damaged-…` or `set-aside-…` instead. One more file sits directly
in `mod_data\LizardTail\`: `names.json`, the Steam names of the people in your runs, remembered so they
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

Swaps are refused while they would be unsafe: once Load, Host or Continue has been pressed, with a popup open,
during a run, or while the game is saving. They are also refused while the game's own save file is
damaged (the panel then shows its backup); pressing Load once lets the game repair it or set it aside.
The main menu reads the solo save whenever it draws its buttons, so a damaged solo save is repaired or
set aside without a press.

## Installing

Put `LizardTail.dll` and `LizardTail.json` (from a [release](https://github.com/jerryhebert/sts2-lizard-tail/releases),
or built yourself as below) in a `mods/LizardTail/` folder inside your Slay the Spire 2 install, and
restart the game. The mod declares `affects_gameplay: false`, so your friends do not need
it to join your games.

Lizard Tail was called Save Slots before v0.2.0. If you had that version, delete `mods/SaveSlots/`
so the two do not both load. Runs you parked carry over by themselves: the first time the new version
needs them, it renames `mod_data\SaveSlots\` to `mod_data\LizardTail\`.

## Publishing to the Steam Workshop

```powershell
pwsh tools\Publish-Workshop.ps1           # build and stage only
pwsh tools\Publish-Workshop.ps1 -Upload   # build, stage and upload
```

The listing lives in `workshop/` (title, description, preview image, visibility). The upload runs Mega
Crit's [mod uploader](https://github.com/megacrit/sts2-mod-uploader) against the Steam client signed in on
that PC. The first upload writes `workshop/mod_id.txt`; commit it so later uploads update the same item.
Subscribers then get each new version through Steam, so remove any hand-installed `mods/LizardTail/`
copy once you are subscribed.

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) (9.0.318 or a later 9.0 release,
pinned in `global.json`) and a copy of the game, whose assemblies the mod compiles against. The project
finds a Steam install automatically; if yours lives elsewhere, copy
`LizardTail/Directory.Build.props.example` to `LizardTail/Directory.Build.props` (it is git-ignored) and
set `Sts2Path`. Building also installs the mod into the game's `mods` folder (skipped with a warning
while the game is running).

```bash
dotnet build LizardTail/LizardTail.csproj -c Release
```

```bash
pwsh ./scripts/Run-Checks.ps1
```

Runs the unit tests, the dependency audit and a Release build. Add `-SkipBuild` on a machine without the
game installed; the unit tests need only the SDK.

The pure logic (reading saves, history records, which copies are listed or stale, file names,
wording) is in `LizardTail/LizardTailCode/Core` and is unit-tested without the game.
`LizardTailCode/Game` holds the save-store operations, the picker and the Harmony patches.

## License and attribution

[MIT](LICENSE). Slay the Spire 2 is the property of Mega Crit; this project is an unaffiliated fan mod
and contains no game code or assets. The project was scaffolded from
[Alchyr's mod template](https://github.com/Alchyr/ModTemplate-StS2).
