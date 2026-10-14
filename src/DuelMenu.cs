using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace StsPvp;

/// <summary>
/// "Duel" main menu button and its connect panel. Every path reuses the game's own co-op lobby:
/// Quick Match and room codes over its Steam transport (see <see cref="SteamMatch"/>), and the
/// IP path over its ENet transport (LAN, Hamachi or ZeroTier).
/// </summary>
public static class DuelMenu
{
    private const int RematchJoinAttempts = 30;
    private const float DescriptionHeight = 120;
    private const string LastIpSetting = "user://stspvp_last_ip.txt";

    private const string QuickMatchBeta = "(beta test: may not work properly)";

    /// <summary>Quick Match pairs two players.</summary>
    private const int QuickMatchPlayers = 2;

    /// <summary>Seconds between a waiting Quick Match host's searches for an older waiting lobby.</summary>
    private const double QuickMatchRecheckSeconds = 3;

    // --pvp-host / --pvp-join=<ip> launch arguments (used by simulate.sh): done once per launch.
    private static bool _autoStartDone;

    /// <summary>Set by Rematch on the duel end screen, used once back on the main menu.</summary>
    private sealed record RematchPlan(bool AsHost, ulong SteamHostId);

    private static RematchPlan? _rematch;

    /// <summary>Character the local player keeps in a same-character rematch, picked on the next character select.</summary>
    private static CharacterModel? _keepCharacter;

    /// <summary>The open connect panel: its status line, and whether it is still open.</summary>
    private sealed class Panel(Control popup, Label status, Action close, Action<bool> setBusy)
    {
        public bool IsOpen => GodotObject.IsInstanceValid(popup) && !popup.IsQueuedForDeletion();

        public void Status(string text)
        {
            if (GodotObject.IsInstanceValid(status))
            {
                status.Text = text;
            }
        }

        public void Busy(bool busy) => setBusy(busy);

        public void Close() => close();

        public void Fail(string message)
        {
            Duel.Reset();
            if (GodotObject.IsInstanceValid(status))
            {
                status.Text = message;
                setBusy(false);
            }
        }
    }

    /// <summary>Adds the Duel button right under Multiplayer. Called once the main menu is ready.</summary>
    public static void AddButton(NMainMenu menu, NMainMenuTextButton multiplayerButton)
    {
        // No DuplicateFlags.Signals: the copy must not open the multiplayer submenu.
        var duel = (NMainMenuTextButton)multiplayerButton.Duplicate((int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation));
        duel.Name = "DuelButton";
        multiplayerButton.GetParent().AddChild(duel);
        multiplayerButton.GetParent().MoveChild(duel, multiplayerButton.GetIndex() + 1);
        duel.SetLocalization("PVP_DUEL");
        duel.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OpenPanel(menu)));

        if (_rematch is { } plan)
        {
            _rematch = null;
            _autoStartDone = true;
            var key = plan.SteamHostId.ToString();
            if (plan.SteamHostId == 0)
            {
                OpenPanel(menu, plan.AsHost ? p => HostByIp(menu, p) : p => JoinByIp(menu, p, LoadLastIp(), waitForHost: true));
            }
            else if (plan.AsHost)
            {
                OpenPanel(menu, p => TaskHelper.RunSafely(HostOnSteam(menu, p, SteamMatch.Kind.Rematch, key, Duel.MaxPlayers,
                    "Rematch: waiting for the other players...")));
            }
            else
            {
                OpenPanel(menu, p => TaskHelper.RunSafely(JoinOnSteam(menu, p, SteamMatch.Kind.Rematch, key, waitForHost: true)));
            }
        }
        else if (!_autoStartDone)
        {
            _autoStartDone = true;
            if (CommandLineHelper.HasArg("pvp-host"))
            {
                OpenPanel(menu, p => HostByIp(menu, p));
            }
            else if (CommandLineHelper.GetValue("pvp-join") is { Length: > 0 } autoJoin)
            {
                OpenPanel(menu, p => JoinByIp(menu, p, autoJoin, waitForHost: false));
            }
        }
    }

    /// <summary>
    /// The connect popup, built on the game's own popup (frame, title, Yes/No buttons) with the
    /// Quick Match button, the room code / IP field and this PC's addresses added in the game's font.
    /// <paramref name="autoStart"/> runs once the popup is up (rematch, launch arguments).
    /// </summary>
    private static void OpenPanel(NMainMenu menu, Action<Panel>? autoStart = null)
    {
        var modal = NModalContainer.Instance;
        var popup = NGenericPopup.Create();
        if (modal == null || popup == null)
        {
            return;
        }
        modal.Add(popup);
        var frame = popup.GetNode<NVerticalPopup>("VerticalPopup");
        frame.SetText("Duel", "Quick Match finds a random opponent. Host gives you a room code: " +
                              "friends type it and Join (up to " + Duel.MaxPlayers + " players, free-for-all). " +
                              "Both go through Steam, no IP needed.");
        var body = frame.GetNode<Control>("Description");
        _font = body.GetThemeFont(ThemeConstants.RichTextLabel.NormalFont);

        var join = frame.YesButton;
        join.IsYes = true;
        join.SetText("Join");
        var cancel = frame.NoButton;
        cancel.Visible = true;
        cancel.IsYes = false;
        cancel.SetText("Cancel");
        // A third button, a copy of Join without its hotkey (Enter stays on Join).
        var host = (NPopupYesNoButton)join.Duplicate((int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation));
        host.Name = "HostButton";
        frame.AddChild(host);
        host.DisconnectHotkeys();
        host.SetText("Host");

        var box = new VBoxContainer { Name = "DuelContent" };
        box.AddThemeConstantOverride("separation", 10);
        var quickRow = new HBoxContainer();
        quickRow.AddThemeConstantOverride("separation", 16);
        var quick = Button("Quick Match");
        quick.CustomMinimumSize = new Vector2(220, 48);
        quickRow.AddChild(quick);
        quickRow.AddChild(Label("Random 1v1 opponent\n" + QuickMatchBeta, 18));
        box.AddChild(quickRow);
        var field = new NMegaLineEdit { PlaceholderText = "Room code, or the host's IP", Text = LoadLastIp(), CustomMinimumSize = new Vector2(0, 52) };
        StyleField(field);
        box.AddChild(field);
        var status = Label("", 20);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(status);
        var ipRow = new HBoxContainer();
        ipRow.AddThemeConstantOverride("separation", 12);
        var ipTitle = Label("LAN / Hamachi / ZeroTier instead:", 18);
        ipTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ipRow.AddChild(ipTitle);
        var hostByIp = Button("Host by IP");
        hostByIp.CustomMinimumSize = new Vector2(160, 40);
        ipRow.AddChild(hostByIp);
        box.AddChild(ipRow);
        AddOwnAddresses(box);
        frame.AddChild(box);
        LayOut(body, box, host, join, cancel);

        void SetBusy(bool busy)
        {
            host.Visible = !busy;
            join.Visible = !busy;
            quick.Disabled = busy || !SteamMatch.Available;
            hostByIp.Disabled = busy;
            field.Editable = !busy;
        }

        void Close()
        {
            if (GodotObject.IsInstanceValid(popup) && !popup.IsQueuedForDeletion())
            {
                modal.Clear();
            }
        }

        var panel = new Panel(popup, status, Close, SetBusy);
        cancel.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            Duel.Reset();
            Close();
        }));
        quick.Pressed += () => TaskHelper.RunSafely(QuickMatch(menu, panel));
        if (!SteamMatch.Available)
        {
            // Without Steam only the IP path works: Host hosts by IP.
            quick.Disabled = true;
            quickRow.GetChild<Label>(1).Text = "Needs Steam running\n" + QuickMatchBeta;
        }
        host.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            if (!SteamMatch.Available)
            {
                HostByIp(menu, panel);
                return;
            }
            var code = SteamMatch.NewRoomCode();
            TaskHelper.RunSafely(HostOnSteam(menu, panel, SteamMatch.Kind.Room, code, Duel.MaxPlayers,
                "Room code: " + code + "  (copied: send it to your friends)", copy: code));
        }));
        hostByIp.Pressed += () => HostByIp(menu, panel);
        void Join()
        {
            var text = field.Text.Trim();
            if (text.Length == 0)
            {
                panel.Status("Type a room code or the host's IP first.");
            }
            else if (SteamMatch.ParseRoomCode(text) is { } code)
            {
                TaskHelper.RunSafely(JoinOnSteam(menu, panel, SteamMatch.Kind.Room, code, waitForHost: false));
            }
            else
            {
                JoinByIp(menu, panel, text, waitForHost: false);
            }
        }
        join.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Join()));
        field.TextSubmitted += _ => Join();

        if (autoStart != null)
        {
            autoStart(panel);
        }
        else
        {
            field.GrabFocus();
        }
    }

    // ---- Hosting ---------------------------------------------------------------------------

    /// <summary>Opens the character select as host of <paramref name="net"/>.</summary>
    private static NCharacterSelectScreen PushHostLobby(NMainMenu menu, NetHostGameService net, int maxPlayers)
    {
        var lobby = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        lobby.InitializeMultiplayerAsHost(net, maxPlayers);
        menu.SubmenuStack.Push(lobby);
        return lobby;
    }

    private static void HostByIp(NMainMenu menu, Panel panel)
    {
        Duel.Begin();
        var net = new NetHostGameService();
        var error = net.StartENetHost(Duel.Port, Duel.MaxPlayers);
        if (error.HasValue)
        {
            panel.Fail("Could not host on UDP port " + Duel.Port + ": " + error.Value.GetReason() +
                       ". Is another game already hosting?");
            return;
        }
        Log.Info("[pvp] hosting duel on UDP " + Duel.Port);
        PushHostLobby(menu, net, Duel.MaxPlayers);
        panel.Close();
    }

    /// <summary>
    /// Hosts a tagged Steam lobby and opens the character select, with <paramref name="note"/> shown
    /// on top of it. Null if the lobby could not be opened (the panel says why).
    /// </summary>
    private static async Task<(NCharacterSelectScreen screen, NetHostGameService net)?> HostOnSteam(
        NMainMenu menu, Panel panel, SteamMatch.Kind kind, string key, int maxPlayers, string note, string? copy = null)
    {
        Duel.Begin();
        panel.Busy(true);
        panel.Status("Opening a Steam lobby...");
        var net = new NetHostGameService();
        var error = await SteamMatch.Host(net, kind, key, maxPlayers);
        if (error != null)
        {
            panel.Fail("Could not open a Steam lobby: " + error);
            return null;
        }
        if (!panel.IsOpen)
        {
            net.Disconnect(NetError.Quit);
            Duel.Reset();
            return null;
        }
        if (copy != null)
        {
            DisplayServer.ClipboardSet(copy);
        }
        var screen = PushHostLobby(menu, net, maxPlayers);
        ShowLobbyNote(screen, note);
        panel.Close();
        return (screen, net);
    }

    /// <summary>A line of text on top of the character select (room code, quick match status).</summary>
    private static void ShowLobbyNote(NCharacterSelectScreen screen, string text)
    {
        var note = Label(text, 28);
        note.Name = "DuelNote";
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        note.OffsetTop = 24;
        note.MouseFilter = Control.MouseFilterEnum.Ignore;
        screen.AddChild(note);
    }

    // ---- Joining ---------------------------------------------------------------------------

    /// <summary>
    /// One attempt through the game's join flow; on success the character select is open as
    /// client. Null when joined, else why not.
    /// </summary>
    private static async Task<string?> TryJoin(NMainMenu menu, Panel panel, IClientConnectionInitializer initializer)
    {
        Duel.Begin();
        var flow = new JoinFlow();
        try
        {
            var result = await flow.Begin(initializer, menu.GetTree());
            if (result.sessionState == RunSessionState.InLobby && result.gameMode == GameMode.Standard && result.joinResponse.HasValue)
            {
                if (!panel.IsOpen)
                {
                    // Cancelled while connecting.
                    flow.NetService?.Disconnect(NetError.Quit);
                    Duel.Reset();
                    return "Cancelled.";
                }
                var lobby = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                lobby.InitializeMultiplayerAsClient(flow.NetService!, result.joinResponse.Value);
                menu.SubmenuStack.Push(lobby);
                panel.Close();
                return null;
            }
            flow.NetService?.Disconnect(NetError.InternalError);
            Duel.Reset();
            return "The host is not in a duel lobby (it is " + result.sessionState + ", " + result.gameMode + ").";
        }
        catch (ClientConnectionFailedException e)
        {
            flow.NetService?.Disconnect(e.info.GetReason());
            Duel.Reset();
            return "Could not connect: " + e.info.GetReason() + ".";
        }
        catch (Exception e)
        {
            Log.Error("[pvp] join failed: " + e);
            flow.NetService?.Disconnect(NetError.InternalError);
            Duel.Reset();
            return "Could not connect: " + e.Message;
        }
    }

    /// <summary>
    /// Joins whatever <paramref name="find"/> points at (null: nothing to join yet). A rematch
    /// joiner may get there before the host is hosting again: with <paramref name="waitForHost"/>
    /// it keeps trying for a while.
    /// </summary>
    private static async Task Join(NMainMenu menu, Panel panel, Func<Task<IClientConnectionInitializer?>> find,
        string connecting, string notFound, string hint, bool waitForHost)
    {
        panel.Busy(true);
        for (var attempt = 1; ; attempt++)
        {
            panel.Status(connecting);
            string? error;
            try
            {
                var target = await find();
                error = target == null ? notFound : await TryJoin(menu, panel, target);
            }
            catch (Exception e)
            {
                Log.Error("[pvp] lobby search failed: " + e);
                error = "Steam lobby search failed: " + e.Message;
            }
            if (error == null || !panel.IsOpen)
            {
                return;
            }
            if (waitForHost && attempt < RematchJoinAttempts)
            {
                panel.Status("Waiting for the host to start the rematch...");
                await Wait(menu, 2);
                continue;
            }
            panel.Fail(error + " " + hint);
            return;
        }
    }

    private static void JoinByIp(NMainMenu menu, Panel panel, string address, bool waitForHost)
    {
        // A rematch rejoins the last IP joined.
        SaveLastIp(address);
        // Each joiner needs its own id in the lobby (the host's is 1).
        TaskHelper.RunSafely(Join(menu, panel,
            () => Task.FromResult<IClientConnectionInitializer?>(
                new ENetClientConnectionInitializer((ulong)Random.Shared.NextInt64(1000, long.MaxValue), address, Duel.Port)),
            "Connecting to " + address + ":" + Duel.Port + " ...", "",
            "Check the IP, that the host clicked Host by IP, and the firewall.", waitForHost));
    }

    private static Task JoinOnSteam(NMainMenu menu, Panel panel, SteamMatch.Kind kind, string key, bool waitForHost)
    {
        return Join(menu, panel,
            async () => (await SteamMatch.Find(kind, key)) is [var lobby, ..] ? SteamClientConnectionInitializer.FromLobby(lobby) : null,
            kind == SteamMatch.Kind.Room ? "Looking for room " + key + " ..." : "Looking for the host's lobby...",
            kind == SteamMatch.Kind.Room ? "No open duel with room code " + key + "." : "The host's lobby was not found.",
            kind == SteamMatch.Kind.Room ? "Check the code, and that the host is still in the lobby." : "", waitForHost);
    }

    // ---- Quick Match -----------------------------------------------------------------------

    /// <summary>
    /// Joins the oldest open Quick Match lobby; if there is none, opens one and waits there for the
    /// next player. Two players searching at once would both open one: a waiting host keeps
    /// looking, and moves into an older lobby if one shows up, so exactly one of them stays host.
    /// </summary>
    private static async Task QuickMatch(NMainMenu menu, Panel panel)
    {
        panel.Busy(true);
        panel.Status("Searching for an opponent...");
        try
        {
            foreach (var lobby in await SteamMatch.Find(SteamMatch.Kind.Quick, ""))
            {
                if (!panel.IsOpen || await TryJoin(menu, panel, SteamClientConnectionInitializer.FromLobby(lobby)) == null)
                {
                    return;
                }
            }
        }
        catch (Exception e)
        {
            // Hosting below reports Steam being unavailable.
            Log.Error("[pvp] quick match search failed: " + e);
        }
        if (!panel.IsOpen)
        {
            return;
        }
        if (await HostOnSteam(menu, panel, SteamMatch.Kind.Quick, "", QuickMatchPlayers,
                "Quick Match: waiting for an opponent...") is { } hosted)
        {
            await WatchForOlderLobby(menu, hosted.screen, hosted.net);
        }
    }

    private static async Task WatchForOlderLobby(NMainMenu menu, NCharacterSelectScreen screen, NetHostGameService net)
    {
        var mine = SteamMatch.HostedLobby;
        while (true)
        {
            await Wait(menu, QuickMatchRecheckSeconds);
            bool StillWaiting() => GodotObject.IsInstanceValid(screen) && menu.SubmenuStack.Peek() == screen &&
                                   SteamMatch.HostedLobby == mine && net.IsConnected && net.ConnectedPeers.Count == 0;
            if (!StillWaiting())
            {
                return;
            }
            List<ulong> others;
            try
            {
                others = await SteamMatch.Find(SteamMatch.Kind.Quick, "");
            }
            catch (Exception e)
            {
                Log.Warn("[pvp] quick match recheck failed: " + e.Message);
                continue;
            }
            if (others.Count == 0 || others[0] > mine || !StillWaiting())
            {
                continue;
            }
            Log.Info($"[pvp] older quick match lobby {others[0]} found: leaving ours ({mine}) to join it");
            SteamMatch.ForgetHostedLobby();
            // Closing the character select closes our lobby.
            menu.SubmenuStack.Pop();
            OpenPanel(menu, p => TaskHelper.RunSafely(QuickMatch(menu, p)));
            return;
        }
    }

    private static async Task Wait(Node node, double seconds)
    {
        await node.ToSignal(node.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    // ---- Rematch ---------------------------------------------------------------------------

    /// <summary>
    /// The duel end screen's Rematch: back to the main menu, where the host hosts again and the
    /// other players rejoin it the way they came (same IP, or the host's Steam lobby). With
    /// <paramref name="sameCharacter"/> this player keeps their character: it is picked and
    /// readied for them, and the duel goes straight to a new draft.
    /// </summary>
    public static void Rematch(bool sameCharacter)
    {
        var net = RunManager.Instance.NetService;
        var asHost = net.Type == NetGameType.Host;
        var steamHost = net.Platform != PlatformType.Steam ? 0
            : asHost ? SteamMatch.MySteamId
            : ((NetClientGameService)net).HostNetId;
        _rematch = new RematchPlan(asHost, steamHost);
        _keepCharacter = sameCharacter ? Duel.LocalCharacter : null;
        Log.Info($"[pvp] rematch as {(asHost ? "host" : "client")} over {(steamHost == 0 ? "IP" : "Steam")}" +
                 (_keepCharacter != null ? ", keeping " + _keepCharacter.Id : ""));
        if (asHost)
        {
            net.Disconnect(NetError.QuitGameOver);
        }
        TaskHelper.RunSafely(NGame.Instance!.ReturnToMainMenuAfterRun());
    }

    /// <summary>
    /// On the character select (every time it opens): in a same-character rematch, picks the kept character and readies
    /// up (the lobby still waits for the other players, who may pick anew).
    /// </summary>
    public static void OnCharacterSelectOpened(NCharacterSelectScreen screen)
    {
        // The game reuses the screen: a note from an earlier lobby goes (a host adds its own after this).
        if (screen.GetNodeOrNull("DuelNote") is { } note)
        {
            // Removed now, so a new note can take the name.
            screen.RemoveChild(note);
            note.QueueFree();
        }
        if (!Duel.Active || _keepCharacter is not { } keep)
        {
            return;
        }
        _keepCharacter = null;
        var button = screen.FindChildren("*", "", recursive: true, owned: false).OfType<NCharacterSelectButton>()
            .FirstOrDefault(b => b.Character.Id == keep.Id && !b.IsLocked);
        if (button == null)
        {
            Log.Warn("[pvp] kept character " + keep.Id + " not found on the character select");
            return;
        }
        button.Select();
        AccessTools.Method(typeof(NCharacterSelectScreen), "OnEmbarkPressed").Invoke(screen, new object?[] { null });
        Log.Info("[pvp] rematch: picked and readied " + keep.Id);
    }

    /// <summary>
    /// Places the added rows under the popup's description and moves the buttons below them,
    /// Host / Join / Cancel side by side; the popup's frame grows to fit (<see cref="FitFrame"/>).
    /// </summary>
    private static void LayOut(Control body, VBoxContainer box, Control host, Control join, Control cancel)
    {
        // The description fills the popup down to its buttons: keep only its top for the text.
        body.MouseFilter = Control.MouseFilterEnum.Ignore;
        body.AnchorBottom = 0;
        body.OffsetBottom = body.OffsetTop + DescriptionHeight;
        box.Position = new Vector2(body.Position.X, body.Position.Y + DescriptionHeight);
        box.CustomMinimumSize = new Vector2(body.Size.X, 0);
        // Cancel stays left, Join right, Host goes between them.
        host.Position = new Vector2((cancel.Position.X + join.Position.X) / 2, join.Position.Y);
        // Once the rows have their size: grow the frame (about its center) and move the buttons
        // down, so the rows end above the buttons.
        Callable.From(() => FitFrame(box, host, join, cancel)).CallDeferred();
    }

    private const float RowsToButtonsGap = 16;

    private static void FitFrame(VBoxContainer box, params Control[] buttons)
    {
        if (!GodotObject.IsInstanceValid(box) || box.GetParent() is not Control frame)
        {
            return;
        }
        var grow = box.Position.Y + box.GetCombinedMinimumSize().Y + RowsToButtonsGap - buttons.Min(b => b.Position.Y);
        if (grow <= 0)
        {
            return;
        }
        frame.OffsetTop -= grow / 2;
        frame.OffsetBottom += grow / 2;
        foreach (var button in buttons)
        {
            button.Position += new Vector2(0, grow);
        }
    }

    /// <summary>One row per address of this PC (ZeroTier, Hamachi, LAN) with a Copy button.</summary>
    private static void AddOwnAddresses(VBoxContainer box)
    {
        var addresses = LocalAddresses.Find();
        if (addresses.Count == 0)
        {
            box.AddChild(Label("No network address found. Is ZeroTier / Hamachi connected?", 18));
            return;
        }
        foreach (var address in addresses)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var kind = address.Kind switch
            {
                LocalAddresses.Kind.ZeroTier => "ZeroTier",
                LocalAddresses.Kind.Hamachi => "Hamachi",
                _ => "LAN",
            };
            var text = Label(kind + ":  " + address.Ip, 22);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
            var copy = Button("Copy");
            copy.CustomMinimumSize = new Vector2(120, 40);
            copy.Pressed += () =>
            {
                DisplayServer.ClipboardSet(address.Ip);
                copy.Text = "Copied!";
            };
            row.AddChild(copy);
            box.AddChild(row);
        }
    }

    // Text in the game's font (the popup description's), gold-tinted like the game's labels.
    private static Font? _font;

    private static Label Label(string text, int size)
    {
        var label = new Label { Text = text };
        if (_font != null)
        {
            label.AddThemeFontOverride(ThemeConstants.Label.Font, _font);
        }
        label.AddThemeFontSizeOverride(ThemeConstants.Label.FontSize, size);
        label.AddThemeColorOverride("font_color", new Color("fff6e2"));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
        label.AddThemeConstantOverride(ThemeConstants.Label.OutlineSize, 6);
        return label;
    }

    private static Button Button(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(110, 40), FocusMode = Control.FocusModeEnum.None };
        StyleField(button);
        button.AddThemeColorOverride("font_hover_color", new Color("f0b400"));
        return button;
    }

    /// <summary>Dark inset with a thin gold edge, like the game's search and seed fields.</summary>
    private static void StyleField(Control control)
    {
        if (_font != null)
        {
            control.AddThemeFontOverride("font", _font);
        }
        control.AddThemeFontSizeOverride("font_size", 22);
        control.AddThemeColorOverride("font_color", new Color("fff6e2"));
        control.AddThemeColorOverride("font_placeholder_color", new Color(1, 0.96f, 0.89f, 0.4f));
        foreach (var (state, border) in new[] { ("normal", 0.45f), ("focus", 1f), ("hover", 0.8f), ("pressed", 1f), ("read_only", 0.3f) })
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.05f, 0.07f, 0.09f, 0.85f),
                BorderColor = new Color(0.94f, 0.71f, 0f, border),
                ContentMarginLeft = 14,
                ContentMarginRight = 14,
                ContentMarginTop = 6,
                ContentMarginBottom = 6,
            };
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(6);
            control.AddThemeStyleboxOverride(state, style);
        }
    }

    private static string LoadLastIp()
    {
        using var file = Godot.FileAccess.Open(LastIpSetting, Godot.FileAccess.ModeFlags.Read);
        return file?.GetLine().Trim() ?? "";
    }

    private static void SaveLastIp(string ip)
    {
        using var file = Godot.FileAccess.Open(LastIpSetting, Godot.FileAccess.ModeFlags.Write);
        file?.StoreLine(ip);
    }
}
