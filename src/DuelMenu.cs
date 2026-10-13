using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace StsPvp;

/// <summary>
/// "Duel" main menu button and its host/join panel. Both paths reuse the game's own co-op lobby
/// over its ENet (IP) transport, so it works on LAN, Hamachi or ZeroTier.
/// </summary>
public static class DuelMenu
{
    private const int RematchJoinAttempts = 30;
    private const float DescriptionHeight = 120;
    private const string LastIpSetting = "user://stspvp_last_ip.txt";

    // --pvp-host / --pvp-join=<ip> launch arguments (used by simulate.sh): done once per launch.
    private static bool _autoStartDone;

    // Set by Rematch on the duel end screen, used once back on the main menu.
    private static bool _rematchPending;
    private static bool _rematchAsHost;

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

        if (_rematchPending)
        {
            _rematchPending = false;
            _autoStartDone = true;
            if (_rematchAsHost)
            {
                OpenPanel(menu, autoHost: true);
            }
            else
            {
                OpenPanel(menu, autoJoinIp: LoadLastIp(), waitForHost: true);
            }
        }
        else if (!_autoStartDone)
        {
            _autoStartDone = true;
            if (CommandLineHelper.HasArg("pvp-host"))
            {
                OpenPanel(menu, autoHost: true);
            }
            else if (CommandLineHelper.GetValue("pvp-join") is { Length: > 0 } autoJoin)
            {
                OpenPanel(menu, autoJoinIp: autoJoin);
            }
        }
    }

    /// <summary>
    /// The host/join popup, built on the game's own popup (frame, title, Yes/No buttons) with the
    /// IP rows and field added in the game's font.
    /// </summary>
    private static void OpenPanel(NMainMenu menu, bool autoHost = false, string? autoJoinIp = null, bool waitForHost = false)
    {
        var modal = NModalContainer.Instance;
        var popup = NGenericPopup.Create();
        if (modal == null || popup == null)
        {
            return;
        }
        modal.Add(popup);
        var frame = popup.GetNode<NVerticalPopup>("VerticalPopup");
        frame.SetText("Duel", "Host and wait for your friends (up to " + Duel.MaxPlayers + " players, free-for-all), " +
                              "or type the host's IP and join " +
                              "(ZeroTier / Hamachi / LAN, UDP port " + Duel.Port + ").");
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
        box.AddChild(Label("Your IP (hosting? send it to your friends):", 22));
        AddOwnAddresses(box);
        var ip = new NMegaLineEdit { PlaceholderText = "Host IP, e.g. 25.1.2.3", Text = LoadLastIp(), CustomMinimumSize = new Vector2(0, 52) };
        StyleField(ip);
        box.AddChild(ip);
        var status = Label("", 20);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(status);
        frame.AddChild(box);
        LayOut(body, box, host, join, cancel);

        void SetBusy(bool busy)
        {
            host.Visible = !busy;
            join.Visible = !busy;
            ip.Editable = !busy;
        }

        void Close()
        {
            if (GodotObject.IsInstanceValid(popup) && !popup.IsQueuedForDeletion())
            {
                modal.Clear();
            }
        }

        cancel.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            Duel.Reset();
            Close();
        }));
        host.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            if (StartHost(menu, status))
            {
                Close();
            }
        }));
        void Join()
        {
            var address = ip.Text.Trim();
            if (address.Length == 0)
            {
                status.Text = "Type the host's IP first.";
                return;
            }
            SaveLastIp(address);
            SetBusy(true);
            status.Text = "Connecting to " + address + ":" + Duel.Port + " ...";
            TaskHelper.RunSafely(JoinAsync(menu, address, status, popup, Close, SetBusy, waitForHost));
        }
        join.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Join()));
        ip.TextSubmitted += _ => Join();

        if (autoHost)
        {
            if (StartHost(menu, status))
            {
                Close();
            }
        }
        else if (autoJoinIp != null)
        {
            ip.Text = autoJoinIp;
            Join();
        }
        else
        {
            ip.GrabFocus();
        }
    }

    /// <summary>
    /// Places the added rows under the popup's description and moves the buttons below them,
    /// Host / Join / Cancel side by side; the popup's frame grows to fit.
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

    private static bool StartHost(NMainMenu menu, Label status)
    {
        Duel.Begin();
        var net = new NetHostGameService();
        var error = net.StartENetHost(Duel.Port, Duel.MaxPlayers);
        if (error.HasValue)
        {
            Duel.Reset();
            status.Text = "Could not host on UDP port " + Duel.Port + ": " + error.Value.GetReason() +
                          ". Is another game already hosting?";
            return false;
        }
        Log.Info("[pvp] hosting duel on UDP " + Duel.Port);
        var lobby = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        lobby.InitializeMultiplayerAsHost(net, Duel.MaxPlayers);
        menu.SubmenuStack.Push(lobby);
        return true;
    }

    private static async Task JoinAsync(NMainMenu menu, string address, Label status, Control popup, Action close, Action<bool> setBusy, bool waitForHost)
    {
        // A rematch joiner may get there before the host is hosting again: keep trying for a while.
        for (var attempt = 1; ; attempt++)
        {
            Duel.Begin();
            var flow = new JoinFlow();
            try
            {
                // Each joiner needs its own id in the lobby (the host's is 1).
                var netId = (ulong)Random.Shared.NextInt64(1000, long.MaxValue);
                var initializer = new ENetClientConnectionInitializer(netId, address, Duel.Port);
                var result = await flow.Begin(initializer, menu.GetTree());
                if (result.sessionState == RunSessionState.InLobby && result.gameMode == GameMode.Standard && result.joinResponse.HasValue)
                {
                    var lobby = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                    lobby.InitializeMultiplayerAsClient(flow.NetService!, result.joinResponse.Value);
                    menu.SubmenuStack.Push(lobby);
                    close();
                    return;
                }
                flow.NetService?.Disconnect(NetError.InternalError);
                if (!await Retry())
                {
                    Fail("The host is not in a duel lobby (it is " + result.sessionState + ", " + result.gameMode + ").");
                }
                else
                {
                    continue;
                }
            }
            catch (ClientConnectionFailedException e)
            {
                flow.NetService?.Disconnect(e.info.GetReason());
                if (await Retry())
                {
                    continue;
                }
                Fail("Could not connect: " + e.info.GetReason() + ". Check the IP, that the host clicked Host, and the firewall.");
            }
            catch (Exception e)
            {
                Log.Error("[pvp] join failed: " + e);
                Fail("Could not connect: " + e.Message);
                flow.NetService?.Disconnect(NetError.InternalError);
            }
            return;

            async Task<bool> Retry()
            {
                if (!waitForHost || attempt >= RematchJoinAttempts || !GodotObject.IsInstanceValid(popup))
                {
                    return false;
                }
                status.Text = "Waiting for the host to start the rematch...";
                await menu.ToSignal(menu.GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
                return GodotObject.IsInstanceValid(popup) && !popup.IsQueuedForDeletion();
            }
        }

        void Fail(string message)
        {
            Duel.Reset();
            if (GodotObject.IsInstanceValid(status))
            {
                status.Text = message;
                setBusy(false);
            }
        }
    }

    /// <summary>
    /// The duel end screen's Rematch: back to the main menu, where the host hosts again and the
    /// other players rejoin the same IP (waiting for the host if it gets there first).
    /// </summary>
    public static void Rematch()
    {
        var net = RunManager.Instance.NetService;
        _rematchAsHost = net.Type == NetGameType.Host;
        _rematchPending = true;
        Log.Info("[pvp] rematch as " + (_rematchAsHost ? "host" : "client"));
        if (_rematchAsHost)
        {
            net.Disconnect(NetError.QuitGameOver);
        }
        TaskHelper.RunSafely(NGame.Instance!.ReturnToMainMenuAfterRun());
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
