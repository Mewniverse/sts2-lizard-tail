using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using LizardTail.LizardTailCode.Core;

namespace LizardTail.LizardTailCode.Game;

/// <summary>
/// The panel on the multiplayer menu listing the active multiplayer run and every parked one.
/// The game's Load button always continues the active run; this panel decides which run that is.
/// It sits at the right edge, and the game's tiles move left (and shrink on narrow screens) to make room.
/// </summary>
public partial class SlotPicker : PanelContainer
{
    public const string NodeName = "LizardTailPicker";

    private const float Width = 520;
    private const float Margin = 40;

    private static readonly Color Gold = new("e0c070");
    private static readonly Color Ink = new("e8e2d0");
    private static readonly Color Muted = new("9a9a9a");
    private static readonly Color Warn = new("e0a070");
    private static readonly Color Good = new("8fd18f");

    private NMultiplayerSubmenu _submenu = null!;
    private VBoxContainer _list = null!;
    private Label _status = null!;
    private double _sinceCheck;
    private string? _blocked = "";
    private readonly List<SlotButton> _actions = new();
    private readonly List<List<SlotButton>> _rows = new();
    private NodePath? _joinRightAsScene;
    private Vector2? _tilesHomeOffset;
    private static bool _loggedFolder;
    private bool _acting;
    private static bool _refreshRequested;

    public static SlotPicker? Of(NMultiplayerSubmenu submenu) => submenu.GetNodeOrNull<SlotPicker>(NodeName);

    public static void AttachTo(NMultiplayerSubmenu submenu)
    {
        if (Of(submenu) != null)
        {
            return;
        }

        var picker = new SlotPicker { Name = NodeName };
        picker.Build(submenu);
        submenu.AddChild(picker);
        // Under the game's loading overlay, so it is covered while a lobby starts.
        if (MultiplayerMenu.Overlay(submenu) is { } overlay && overlay.GetParent() == submenu)
        {
            submenu.MoveChild(picker, overlay.GetIndex());
        }
        picker.Refresh();

        if (!_loggedFolder && SlotStore.Manager.IsProfileInitialized)
        {
            _loggedFolder = true;
            ModLog.Info($"Parked runs for this profile are in {SlotStore.Local.GetFullPath(SlotStore.SlotDir)} ({(SharedSaves.UsingNormalSaves ? "normal saves" : "separate modded saves")})");
        }
    }

    private void Build(NMultiplayerSubmenu submenu)
    {
        _submenu = submenu;
        MouseFilter = MouseFilterEnum.Stop;

        AnchorLeft = 1;
        AnchorRight = 1;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -Width - Margin;
        OffsetRight = -Margin;
        OffsetTop = -330;
        OffsetBottom = 330;

        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.07f, 0.9f),
            BorderColor = new Color(0.85f, 0.75f, 0.45f, 0.9f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        });

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);

        column.AddChild(MakeLabel("Multiplayer runs", 26, Gold));
        column.AddChild(MakeLabel("Load continues the active run. Park it to host a new one; make any parked run active to continue it.", 15, Muted));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true, // a controller moving down the list scrolls it
        };
        column.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(_list);

        _status = MakeLabel("", 15, Muted);
        column.AddChild(_status);
        column.AddChild(MakeLabel("Parked runs are kept on this PC only.", 13, Muted));
    }

    /// <summary>
    /// Rebuilds the list from the files on disk, keeping a controller's focus on the same button. The
    /// game's own hooks call this; during a picker action they are ignored and the action refreshes once.
    /// </summary>
    public void Refresh()
    {
        if (_acting)
        {
            return;
        }
        var owner = IsInsideTree() ? GetViewport()?.GuiGetFocusOwner() : null;
        Rebuild(KeyOf(owner) ?? KeyOf(MultiplayerMenu.LastFocused(_submenu)), owner is SlotButton);
    }

    private void Rebuild(string? focusKey, bool hadFocus)
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child); // out of the tree now, so nothing can focus a button about to be freed
            child.QueueFree();
        }
        _actions.Clear();
        _rows.Clear();

        try
        {
            var me = SlotActions.LocalPlayerId;
            var ready = SlotStore.Manager.IsProfileInitialized;
            var active = ready ? RunSummary.TryParse(SlotStore.ReadActive()) : null;
            // Copies are hidden and cleared only against a save Steam Cloud also holds: one the cloud
            // lacks is undone at the next launch, and its parked copy must stay reachable.
            var cloudUnknown = false;
            var agreed = ready ? SlotStore.AgreedActive(active, out cloudUnknown) : null;
            var all = ready ? SlotStore.AllSlots(me) : Array.Empty<Slot>();
            // A save from a newer game version proves nothing about what this one can load, so older
            // copies of that run stay listed (one can be made active to go back) and are not cleared.
            var latest = ready ? SlotStore.Manager.GetLatestSchemaVersion<MegaCrit.Sts2.Core.Saves.SerializableRun>() : 0;
            var proof = agreed != null && (latest <= 0 || agreed.SchemaVersion <= latest) ? agreed : null;
            if (SlotRules.Superseded(all, s => s.Summary, proof).Count > 0)
            {
                SlotActions.TrashSuperseded(all, proof);
                all = SlotStore.AllSlots(me);
            }
            var note = active == null ? null
                : SlotStore.ActiveDamaged() ? SlotText.Damaged
                : cloudUnknown ? SlotText.CloudUnknown
                : agreed == null ? SlotText.NotInCloud
                : null;
            BuildActive(active, me, note);
            BuildParked(all, SlotStore.Listed(all, proof), me);
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not list multiplayer runs: {ex}");
            _list.AddChild(MakeLabel($"Could not list runs: {ex.Message}", 16, Warn));
        }

        WireFocus();
        _blocked = "";
        UpdateBlocked();

        // Point the game's memory of focus, and focus itself, at the rebuilt button. A tile that Park or
        // Make active just hid (Load, Host) is forgotten too, so the game falls back to a visible one.
        var replacement = _actions.FirstOrDefault(b => b.Key == focusKey) ?? _actions.FirstOrDefault(b => !b.Disabled);
        ref var last = ref MultiplayerMenu.LastFocused(_submenu);
        if (last is SlotButton || (last != null && !IsInstanceValid(last)))
        {
            last = replacement;
        }
        else if (last != null && !last.Visible)
        {
            last = null;
        }
        if (hadFocus && NControllerManager.Instance?.IsUsingDirectionalNavigation == true)
        {
            var target = (Control?)replacement ?? _submenu.DefaultFocusedControl;
            Callable.From(() =>
            {
                if (target != null && IsInstanceValid(target) && target.IsVisibleInTree())
                {
                    target.GrabFocus();
                }
            }).CallDeferred();
        }
    }

    private static string? KeyOf(Control? control) =>
        control is SlotButton button && IsInstanceValid(button) ? button.Key : null;

    private void BuildActive(RunSummary? active, ulong me, string? note)
    {
        _list.AddChild(MakeLabel("ACTIVE", 14, Gold, spacing: 2));
        if (active == null)
        {
            var unreadable = SlotStore.Manager.IsProfileInitialized && SlotStore.Manager.HasMultiplayerRunSave;
            _list.AddChild(unreadable
                ? MakeLabel(SlotText.Unreadable, 17, Warn)
                : MakeLabel("No active run. Host starts a new one.", 17, Muted));
            return;
        }

        var row = RunRow(active, me, note, noteColour: Warn);
        AddButtons(row, MakeButton("Park this run", "park", () => Act(SlotActions.ParkActiveRun)));
        _list.AddChild(row);
    }

    private void BuildParked(IReadOnlyList<Slot> all, IReadOnlyList<Slot> listed, ulong me)
    {
        _list.AddChild(MakeLabel(listed.Count == 0 ? "PARKED" : $"PARKED ({listed.Count})", 14, Gold, spacing: 2));
        if (listed.Count == 0)
        {
            _list.AddChild(MakeLabel("Nothing parked yet.", 17, Muted));
            return;
        }

        foreach (var slot in listed)
        {
            try
            {
                var note = SlotText.StateNote(slot.State);
                var noteColour = slot.State == SlotState.Abandoned ? Gold : Warn;
                var row = slot.Summary == null
                    ? FileRow(slot.FileName, note)
                    : RunRow(slot.Summary, me, note, noteColour);

                var key = slot.Summary?.Key ?? slot.FileName;
                var activate = MakeButton("Make active", $"activate:{key}", () => Act(() => SlotActions.Activate(slot)));
                activate.Disabled = !SlotRules.CanActivate(slot.State);
                AddButtons(row, activate, MakeTrashButton(slot, all, $"trash:{key}"));
                _list.AddChild(row);
            }
            catch (Exception ex)
            {
                // One odd file must not hide the rest of the list.
                ModLog.Warn($"Could not show parked run {slot.FileName}: {ex}");
                _list.AddChild(MakeLabel($"{slot.FileName}: could not be shown ({ex.Message})", 15, Warn));
            }
        }
    }

    private VBoxContainer RunRow(RunSummary run, ulong me, string? note, Color noteColour)
    {
        var row = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 2);
        row.AddChild(MakeLabel(SlotText.Players(run, me, PlayerNames.NameOf), 18, Ink));
        row.AddChild(MakeLabel($"{SlotText.Progress(run)}  ·  {SlotText.Saved(run.SaveTime)}", 15, Muted));
        if (note != null)
        {
            row.AddChild(MakeLabel(note, 15, noteColour));
        }
        return row;
    }

    private VBoxContainer FileRow(string fileName, string? note)
    {
        var row = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(MakeLabel(fileName, 18, Ink));
        if (note != null)
        {
            row.AddChild(MakeLabel(note, 15, Warn));
        }
        return row;
    }

    private void AddButtons(VBoxContainer row, params SlotButton[] buttons)
    {
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        foreach (var button in buttons)
        {
            line.AddChild(button);
        }
        row.AddChild(line);
        _rows.Add(buttons.ToList());
    }

    /// <summary>
    /// Trash needs a second press within a few seconds, so a stray click cannot remove a run. The timer
    /// owns the armed state, so the label and what the next press does always agree.
    /// </summary>
    private SlotButton MakeTrashButton(Slot slot, IReadOnlyList<Slot> all, string key)
    {
        SlotButton? button = null;
        var armed = false;
        var arming = 0;
        button = MakeButton("Trash", key, () =>
        {
            if (armed)
            {
                Act(() => SlotActions.Trash(slot, all));
                return;
            }
            armed = true;
            var id = ++arming;
            button!.Text = "Press again to trash";
            GetTree().CreateTimer(4).Timeout += () =>
            {
                if (id == arming && IsInstanceValid(button))
                {
                    armed = false;
                    button.Text = "Trash";
                }
            };
        });
        return button;
    }

    /// <summary>
    /// Explicit controller paths: right from the Join tile into the list, left back out, and up and
    /// down between rows. Rebuilt with the list, since the buttons are new each time.
    /// </summary>
    private void WireFocus()
    {
        var join = MultiplayerMenu.JoinButton(_submenu);
        var hasJoin = join != null && IsInstanceValid(join);
        var first = _rows.FirstOrDefault()?.FirstOrDefault();
        if (hasJoin)
        {
            _joinRightAsScene ??= join!.FocusNeighborRight;
            join!.FocusNeighborRight = first != null ? first.GetPath() : _joinRightAsScene;
        }

        for (var r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var button = row[c];
                var self = button.GetPath();
                button.FocusNeighborLeft = c > 0 ? row[c - 1].GetPath() : hasJoin ? join!.GetPath() : self;
                button.FocusNeighborRight = c < row.Count - 1 ? row[c + 1].GetPath() : self;
                button.FocusNeighborTop = r > 0 ? NearestIn(_rows[r - 1], c).GetPath() : self;
                button.FocusNeighborBottom = r < _rows.Count - 1 ? NearestIn(_rows[r + 1], c).GetPath() : self;
            }
        }
    }

    private static SlotButton NearestIn(List<SlotButton> row, int column) => row[Math.Min(column, row.Count - 1)];

    /// <summary>Runs one change to the saves, then makes the game's buttons and this list match.</summary>
    private void Act(Func<string> action)
    {
        var why = SlotActions.WhyNotNow(_submenu);
        if (why != null)
        {
            SetStatus(why, Warn);
            return;
        }

        var owner = GetViewport()?.GuiGetFocusOwner();
        var focusKey = KeyOf(owner);
        _acting = true; // the game's UpdateButtons hook would rebuild the list mid-action and drop focus
        try
        {
            SetStatus(action(), Good);
        }
        catch (Exception ex)
        {
            ModLog.Error($"Save slot change failed: {ex}");
            SetStatus(ex.Message, Warn);
        }
        finally
        {
            try
            {
                MultiplayerMenu.RefreshButtons(_submenu);
            }
            finally
            {
                _acting = false;
            }
        }
        Rebuild(focusKey, owner is SlotButton);
    }

    /// <summary>Makes the game's tiles and this list match the saves on disk, rebuilding the list once.</summary>
    private void RedrawAll()
    {
        var owner = GetViewport()?.GuiGetFocusOwner();
        var focusKey = KeyOf(owner) ?? KeyOf(MultiplayerMenu.LastFocused(_submenu));
        _acting = true;
        try
        {
            MultiplayerMenu.RefreshButtons(_submenu);
        }
        finally
        {
            _acting = false;
        }
        Rebuild(focusKey, owner is SlotButton);
    }

    /// <summary>Asks every open picker to redraw on its next frame, after the game changed the saves itself.</summary>
    public static void RequestRefresh() => _refreshRequested = true;

    public override void _Process(double delta)
    {
        if (_refreshRequested && IsVisibleInTree())
        {
            _refreshRequested = false;
            RedrawAll();
        }
        FitTiles(); // every frame, so tiles never sit under the picker after Host and Load swap places
        _sinceCheck += delta;
        if (_sinceCheck < 0.4)
        {
            return;
        }
        _sinceCheck = 0;
        UpdateBlocked();
    }

    /// <summary>Greys the buttons out while a swap would be unsafe (a lobby starting, a popup open).</summary>
    private void UpdateBlocked()
    {
        var why = SlotActions.WhyNotNow(_submenu);
        if (why == _blocked)
        {
            return;
        }
        _blocked = why;
        foreach (var button in _actions.Where(IsInstanceValid))
        {
            button.MouseFilter = why == null ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            button.Modulate = why == null ? Colors.White : new Color(1, 1, 1, 0.5f);
        }
    }

    /// <summary>
    /// Moves the game's tiles left so none sits under the picker, centred in the space that is left,
    /// and shrinks them if they do not fit there. Measured from where the scene put them, so it can be
    /// recomputed whenever the window size changes.
    /// </summary>
    private void FitTiles()
    {
        if (!IsVisibleInTree() || MultiplayerMenu.ButtonContainer(_submenu) is not { } container)
        {
            return;
        }
        var tiles = container.GetChildren().OfType<Control>().Where(t => t.Visible).ToList();
        if (tiles.Count == 0)
        {
            return;
        }

        var offset = new Vector2(container.OffsetLeft, container.OffsetTop);
        _tilesHomeOffset ??= offset;
        var home = container.Position - (offset - _tilesHomeOffset.Value);

        var left = tiles.Min(t => t.Position.X);
        var right = tiles.Max(t => t.Position.X + t.Size.X);
        var top = tiles.Min(t => t.Position.Y);
        var bottom = tiles.Max(t => t.Position.Y + t.Size.Y);

        var spaceRight = Position.X - Margin; // the picker's left edge, in the menu's coordinates
        var spaceLeft = BackButtonRight() + Margin;
        Vector2 position;
        var scale = 1f;
        if (home.X + right <= spaceRight)
        {
            position = home;
        }
        else
        {
            scale = Math.Clamp((spaceRight - spaceLeft) / Math.Max(1, right - left), 0.4f, 1f);
            container.PivotOffset = new Vector2((left + right) / 2, (top + bottom) / 2);
            position = new Vector2((spaceLeft + spaceRight) / 2 - container.PivotOffset.X, home.Y);
        }

        if (container.Position.DistanceTo(position) > 0.5f)
        {
            container.Position = position;
        }
        if (Math.Abs(container.Scale.X - scale) > 0.001f)
        {
            container.Scale = new Vector2(scale, scale);
        }
    }

    /// <summary>
    /// The right edge of the menu's Back button where it rests when shown (it slides in and out, so its
    /// current position is not used), in the menu's coordinates. Zero when there is none.
    /// </summary>
    private float BackButtonRight()
    {
        if (_submenu.GetNodeOrNull<NBackButton>("BackButton") is not { } back)
        {
            return 0;
        }
        var shown = BackShownPosition(back);
        return shown.X - _submenu.GlobalPosition.X + back.Size.X; // not its hover-animated scale
    }

    private static readonly HarmonyLib.AccessTools.FieldRef<NBackButton, Vector2> BackShownPosition =
        HarmonyLib.AccessTools.FieldRefAccess<NBackButton, Vector2>("_showPos");

    private void SetStatus(string text, Color colour)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", colour);
    }

    private SlotButton MakeButton(string text, string key, Action onPress)
    {
        var button = new SlotButton { Text = text, Key = key, FocusMode = FocusModeEnum.All };
        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += onPress;
        _actions.Add(button);
        return button;
    }

    private static Label MakeLabel(string text, int size, Color colour, int spacing = 0)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(Width - 80, 0),
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        if (spacing > 0)
        {
            label.AddThemeConstantOverride("line_spacing", spacing);
        }
        return label;
    }
}

/// <summary>
/// A plain button that also answers the controller's select button. The game maps confirm to its own
/// ui_select action and removed ui_accept, which is the only one Godot buttons answer by default.
/// </summary>
public partial class SlotButton : Button
{
    /// <summary>What the button does to which run, so focus can find its replacement after a rebuild.</summary>
    public string Key { get; set; } = "";

    public override void _GuiInput(InputEvent @event)
    {
        if (!Disabled && @event.IsActionPressed("ui_select"))
        {
            EmitSignal(BaseButton.SignalName.Pressed);
            AcceptEvent();
            return;
        }
        base._GuiInput(@event);
    }
}
