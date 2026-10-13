using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
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
    private const ulong ClientNetId = 1000;
    private const string LastIpSetting = "user://stspvp_last_ip.txt";

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
    }

    private static void OpenPanel(NMainMenu menu)
    {
        var overlay = new ColorRect { Color = new Color(0, 0, 0, 0.6f), Name = "DuelPanel" };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(560, 0);
        overlay.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, 24);
        }
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        margin.AddChild(box);

        box.AddChild(Label("Duel", 34));
        box.AddChild(Label("Host waits for the other player. To join, type the host's IP " +
                           "(Hamachi / ZeroTier / LAN). UDP port " + Duel.Port + ".", 18));

        box.AddChild(Label("Your IP (hosting? send it to your friend):", 20));
        AddOwnAddresses(box);

        var ip = new LineEdit { PlaceholderText = "Host IP, e.g. 25.1.2.3", Text = LoadLastIp() };
        ip.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(ip);

        var status = Label("", 18);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(status);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        box.AddChild(buttons);
        var host = Button("Host");
        var join = Button("Join");
        var cancel = Button("Cancel");
        buttons.AddChild(host);
        buttons.AddChild(join);
        buttons.AddChild(cancel);

        void SetBusy(bool busy)
        {
            host.Disabled = busy;
            join.Disabled = busy;
            ip.Editable = !busy;
        }

        cancel.Pressed += () =>
        {
            Duel.Reset();
            overlay.QueueFree();
        };
        host.Pressed += () =>
        {
            if (StartHost(menu, status))
            {
                overlay.QueueFree();
            }
        };
        join.Pressed += () =>
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
            TaskHelper.RunSafely(JoinAsync(menu, address, status, overlay, SetBusy));
        };

        menu.AddChild(overlay);
        ip.GrabFocus();
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
        var error = net.StartENetHost(Duel.Port, 2);
        if (error.HasValue)
        {
            Duel.Reset();
            status.Text = "Could not host on UDP port " + Duel.Port + ": " + error.Value.GetReason() +
                          ". Is another game already hosting?";
            return false;
        }
        Log.Info("[pvp] hosting duel on UDP " + Duel.Port);
        var lobby = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        lobby.InitializeMultiplayerAsHost(net, 2);
        menu.SubmenuStack.Push(lobby);
        return true;
    }

    private static async Task JoinAsync(NMainMenu menu, string address, Label status, Control overlay, Action<bool> setBusy)
    {
        Duel.Begin();
        var flow = new JoinFlow();
        try
        {
            var initializer = new ENetClientConnectionInitializer(ClientNetId, address, Duel.Port);
            var result = await flow.Begin(initializer, menu.GetTree());
            if (result.sessionState == RunSessionState.InLobby && result.gameMode == GameMode.Standard && result.joinResponse.HasValue)
            {
                var lobby = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                lobby.InitializeMultiplayerAsClient(flow.NetService!, result.joinResponse.Value);
                menu.SubmenuStack.Push(lobby);
                overlay.QueueFree();
                return;
            }
            Fail("The host is not in a duel lobby (it is " + result.sessionState + ", " + result.gameMode + ").");
            flow.NetService?.Disconnect(NetError.InternalError);
        }
        catch (ClientConnectionFailedException e)
        {
            Fail("Could not connect: " + e.info.GetReason() + ". Check the IP, that the host clicked Host, and the firewall.");
            flow.NetService?.Disconnect(e.info.GetReason());
        }
        catch (Exception e)
        {
            Log.Error("[pvp] join failed: " + e);
            Fail("Could not connect: " + e.Message);
            flow.NetService?.Disconnect(NetError.InternalError);
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

    private static Label Label(string text, int size)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static Button Button(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(150, 48) };
        button.AddThemeFontSizeOverride("font_size", 22);
        return button;
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
