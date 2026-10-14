using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsPvp;

// ---- Startup ---------------------------------------------------------------------------------

/// <summary>
/// Modded, the game hands native Sentry a reject-all sampler, then closes the native SDK in
/// <c>AfterGameInit</c>. When the sampler lands after the extension initialised (Steam's Workshop
/// scan makes mod loading slow enough), the closed SDK later calls through a dangling pointer and
/// the game segfaults at the main menu. The game's own note says the native SDK must stay open
/// until exit: keep the managed shutdown, skip the native one; every event is still rejected.
/// </summary>
[HarmonyPatch(typeof(SentryService), nameof(SentryService.Shutdown))]
internal static class SentryShutdownPatch
{
    private static readonly FieldInfo SentryInit = AccessTools.Field(typeof(SentryService), "_sentryInit");

    private static void Prefix()
    {
        if (ModManager.IsRunningModded())
        {
            SentryInit.SetValue(null, null);
        }
    }
}

// ---- Text ------------------------------------------------------------------------------------

/// <summary>The mod ships no .pck, so its strings are merged into the game's tables on every language load.</summary>
[HarmonyPatch(typeof(LocManager), "SetLanguageInternal")]
internal static class LocPatch
{
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["main_menu_ui"] = new() { ["PVP_DUEL"] = "Duel" },
        ["monsters"] = new()
        {
            ["PVP_OPPONENT.name"] = "Opponent",
            ["PVP_OPPONENT.moves.NOTHING.title"] = "Waiting",
        },
        ["events"] = new()
        {
            ["PVP_DRAFT.title"] = "Duel Draft",
            ["PVP_DRAFT.pages.INITIAL.description"] =
                "Before the duel, build your deck: open 10 packs of 5 cards and keep one card from each, then pick 5 relics (one out of three each time).",
            ["PVP_DRAFT.pages.INITIAL.options.DRAFT.title"] = "Draft",
            ["PVP_DRAFT.pages.INITIAL.options.DRAFT.description"] = "Draft 10 cards, one per pack, then 5 relics.",
            ["PVP_DRAFT.cardsPrompt"] = "Pack {Pack}/{Packs}: choose a card to add to your deck",
            ["PVP_DRAFT.pages.DONE.description"] =
                "Ready. When everyone is done, proceed and pick the next room on the map: it is the duel, every player against all the others.",
        },
        ["encounters"] = new()
        {
            ["PVP_DUEL.title"] = "Duel",
            ["PVP_DUEL.loss"] = "Lost the duel.",
        },
    };

    private static void Postfix(LocManager __instance)
    {
        foreach (var (table, entries) in Strings)
        {
            try
            {
                __instance.GetTable(table).MergeWith(entries);
            }
            catch (Exception e)
            {
                Log.Error($"[pvp] could not add strings to table {table}: {e}");
            }
        }
    }
}

// ---- Run setup -----------------------------------------------------------------------------

/// <summary>Duel runs are not saved: no run save, no stats, no unlocks.</summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class NoSavePatch
{
    private static void Prefix(ref bool shouldSave)
    {
        if (Duel.Active)
        {
            shouldSave = false;
        }
    }
}

/// <summary>The run's first room is the draft, the next one the duel. Both games run this identically (lockstep).</summary>
[HarmonyPatch(typeof(RunManager), "CreateRoom")]
internal static class DuelRoomPatch
{
    private static bool Prefix(RunManager __instance, RoomType roomType, ref AbstractRoom __result)
    {
        if (!Duel.Active || Duel.Started || roomType == RoomType.Map)
        {
            return true;
        }
        var state = (RunState)AccessTools.Property(typeof(RunManager), "State").GetValue(__instance)!;
        if (!Duel.Drafted)
        {
            Duel.Drafted = true;
            Duel.LocalCharacter = LocalContext.GetMe(state.Players)?.Character;
            __result = new EventRoom(ModelDb.Event<PvpDraft>());
            Log.Info("[pvp] draft room created");
            return false;
        }
        Duel.Started = true;
        Duel.PlayerCount = state.Players.Count;
        Duel.WatchForForfeit(__instance, state);
        __result = new CombatRoom(PvpDuel.For(state.Players), state);
        Log.Info($"[pvp] duel room created for {state.Players.Count} players");
        return false;
    }
}

// ---- Combat: proxies redirect to the players they stand for ---------------------------------

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
    typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel))]
internal static class DamageRedirectPatch
{
    private static void Prefix(ref IEnumerable<Creature> targets, Creature? dealer)
    {
        if (Duel.Active)
        {
            targets = Duel.Redirect(targets, dealer);
        }
    }
}

[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply),
    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool))]
internal static class PowerRedirectPatch
{
    private static bool Prefix(ref Creature target, Creature? applier, ref Task __result)
    {
        if (!Duel.Active)
        {
            return true;
        }
        var player = Duel.PlayerFor(target);
        if (player == null)
        {
            return true;
        }
        if (player.Creature == applier)
        {
            __result = Task.CompletedTask;
            return false;
        }
        target = player.Creature;
        return true;
    }
}

/// <summary>
/// Turns in rotation: each player-side turn is given to one player only (in player order, host
/// first, skipping players who died), through the game's own "players taking the turn" list, the
/// same one extra-turn effects use. The others are marked done automatically, keep their block and
/// draw nothing. Every game runs this at the same point of the lockstep, so they all agree.
/// </summary>
[HarmonyPatch(typeof(CombatManager), "StartTurn")]
internal static class TurnRotationPatch
{
    private static void Prefix(CombatState? ____state, List<MegaCrit.Sts2.Core.Entities.Players.Player> ____playersTakingExtraTurn)
    {
        if (!Duel.Active || !Duel.Started || ____state == null || ____state.CurrentSide != CombatSide.Player)
        {
            return;
        }
        // A real extra turn (relic, card) already picked who plays.
        if (____playersTakingExtraTurn.Count > 0)
        {
            return;
        }
        var players = ____state.Players;
        if (players.Count < 2)
        {
            return;
        }
        for (var i = 0; i < players.Count; i++)
        {
            var index = (Duel.NextTurn + i) % players.Count;
            if (!players[index].Creature.IsDead)
            {
                Duel.NextTurn = index + 1;
                ____playersTakingExtraTurn.Add(players[index]);
                return;
            }
        }
    }
}

/// <summary>
/// When a player dies they are out: their stand-in leaves the enemy side and they watch the rest.
/// When one player is left standing the duel is decided and every game shows the end screen.
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(IReadOnlyCollection<Creature>), typeof(bool))]
internal static class DuelEndPatch
{
    private static void Postfix(IReadOnlyCollection<Creature> creatures, ref Task __result)
    {
        if (Duel.Active)
        {
            __result = AfterKill(__result, creatures.FirstOrDefault(c => c.IsPlayer)?.Player?.RunState);
        }
    }

    private static async Task AfterKill(Task kill, IRunState? runState)
    {
        await kill;
        if (runState == null || Duel.Decided || !Duel.Started)
        {
            return;
        }
        var players = runState.Players;
        var me = LocalContext.GetMe(players);
        if (me == null || !players.Any(p => p.Creature.IsDead))
        {
            return;
        }
        // Abandoning kills every player first; that is not a duel result.
        if (RunManager.Instance.IsAbandoned)
        {
            Duel.Decided = true;
            return;
        }
        var alive = players.Count(p => !p.Creature.IsDead);
        // Everyone dead is a normal co-op loss; the game already shows its game over screen.
        if (alive == 0)
        {
            Duel.Decided = true;
            return;
        }
        // Players killed by the same hit share the place.
        if (me.Creature.IsDead && Duel.LocalPlace == 0)
        {
            Duel.LocalPlace = alive + 1;
        }
        if (alive == 1)
        {
            Duel.Finish(me.Creature.IsDead ? Duel.LocalPlace : 1, "one player left standing");
            return;
        }
        var outProxies = me.Creature.CombatState?.Enemies
            .Where(e => !e.IsDead && Duel.PlayerFor(e)?.Creature.IsDead == true)
            .ToList();
        if (outProxies is { Count: > 0 })
        {
            await CreatureCmd.Kill(outProxies);
        }
    }
}

// ---- Display ---------------------------------------------------------------------------------

/// <summary>A proxy shows its player's real HP, block and powers (display only, no game state).</summary>
[HarmonyPatch(typeof(NCreatureStateDisplay), nameof(NCreatureStateDisplay.SetCreature))]
internal static class ProxyDisplayPatch
{
    private static void Prefix(ref Creature creature)
    {
        var player = Duel.PlayerFor(creature);
        if (player != null)
        {
            creature = player.Creature;
        }
    }
}

/// <summary>
/// Combat effects (damage numbers, hit sparks, slashes, beams...) are placed at the node of the
/// creature they are given. Damage is redirected to the opponent's own body, which is hidden on
/// this screen next to your character: an effect on it (or coming from it) is shown at the
/// opponent's proxy instead.
/// </summary>
[HarmonyPatch]
internal static class EffectsAtProxyPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(NDamageNumVfx).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("MegaCrit.Sts2.Core.Nodes.Vfx") == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "Create" && m.GetParameters().Any(p => p.ParameterType == typeof(Creature)));
    }

    private static void Prefix(object?[] __args)
    {
        if (!Duel.Active || !Duel.Started)
        {
            return;
        }
        for (var i = 0; i < __args.Length; i++)
        {
            if (__args[i] is Creature creature)
            {
                __args[i] = Duel.Shown(creature);
            }
        }
    }
}

/// <summary>
/// Several monsters of one type get a random size and tint, and their visuals' scale is reset,
/// which undoes the mirroring of the proxies (all one type). Proxies look like their players.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), "RandomizeEnemyScalesAndHues")]
internal static class ProxyScalePatch
{
    private static bool Prefix() => !Duel.Active;
}

/// <summary>The draft event has no art of its own: it shows the Battleworn Dummy portrait.</summary>
[HarmonyPatch(typeof(EventModel), "InitialPortraitPath", MethodType.Getter)]
internal static class DraftPortraitPatch
{
    private static void Postfix(EventModel __instance, ref string __result)
    {
        if (__instance is PvpDraft)
        {
            __result = MegaCrit.Sts2.Core.Helpers.ImageHelper.GetImagePath("events/battleworn_dummy.png");
        }
    }
}

/// <summary>
/// The game over screen lifts every combat creature's visuals above its backdrop, out of their
/// creature node: the creatures the duel hides (see <c>ArrangeDuelCreature</c>) stay hidden.
/// </summary>
[HarmonyPatch(typeof(NGameOverScreen), "MoveCreaturesToDifferentLayerAndDisableUi")]
internal static class DuelGameOverCreaturesPatch
{
    private static void Postfix()
    {
        if (!Duel.Active || NCombatRoom.Instance == null)
        {
            return;
        }
        foreach (var creature in NCombatRoom.Instance.CreatureNodes.Where(c => !c.Visible && c.Visuals != null))
        {
            creature.Visuals.Visible = false;
        }
    }
}

/// <summary>
/// Duel end screen: two Rematch buttons (copies of the game's Main Menu button) stacked above
/// Main Menu, shown together with it. "Rematch" goes back to the character select; "Same
/// Character" keeps this player's character and goes straight to a new draft.
/// </summary>
[HarmonyPatch(typeof(NGameOverScreen), nameof(NGameOverScreen._Ready))]
internal static class RematchButtonPatch
{
    public static readonly string[] Names = { "RematchButton", "RematchSameButton" };

    private static void Postfix(NReturnToMainMenuButton ____mainMenuButton)
    {
        if (!Duel.Active)
        {
            return;
        }
        var mainMenu = ____mainMenuButton;
        var buttons = new List<NReturnToMainMenuButton>();
        foreach (var (name, text, same, row) in new[] { (Names[0], "Rematch", false, 2), (Names[1], "Same Character", true, 1) })
        {
            // No DuplicateFlags.Signals: the copy must not return to the main menu by itself.
            var button = (NReturnToMainMenuButton)mainMenu.Duplicate((int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation));
            button.Name = name;
            mainMenu.AddSibling(button);
            // Above Main Menu: the spot it slides into when shown (its _Ready computed one from the copy).
            var spot = mainMenu.Position + new Vector2(0, -row * (mainMenu.Size.Y + 16));
            Traverse.Create(button).Field("_showPosition").SetValue(spot);
            button.Position = spot;
            button.GetNode<MegaLabel>("Label").SetTextAutoSize(text);
            button.Disable();
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            {
                buttons.ForEach(b => b.Disable());
                mainMenu.Disable();
                DuelMenu.Rematch(same);
            }));
            buttons.Add(button);
        }
        Log.Info($"[pvp] rematch buttons added under {mainMenu.GetParent().GetType().Name} (main menu at {mainMenu.Position}, size {mainMenu.Size})");
    }
}

/// <summary>The Rematch buttons show up whenever the game shows its Main Menu button.</summary>
[HarmonyPatch(typeof(NReturnToMainMenuButton), "OnEnable")]
internal static class RematchButtonShowPatch
{
    private static void Postfix(NReturnToMainMenuButton __instance)
    {
        if (!Duel.Active || RematchButtonPatch.Names.Contains(__instance.Name.ToString()) || __instance.GetParent() is not { } parent)
        {
            return;
        }
        foreach (var name in RematchButtonPatch.Names)
        {
            parent.GetNodeOrNull<NReturnToMainMenuButton>(name)?.Enable();
        }
    }
}

/// <summary>Same-character rematch: the kept character is picked and readied on the character select.</summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class KeepCharacterPatch
{
    private static void Postfix(NCharacterSelectScreen __instance) => DuelMenu.OnCharacterSelectOpened(__instance);
}

[HarmonyPatch(typeof(NGameOverScreen), "InitializeBannerAndQuote")]
internal static class DuelBannerPatch
{
    private static void Postfix(NCommonBanner ____banner)
    {
        if (Duel.Decided)
        {
            ____banner.label.SetTextAutoSize(Duel.LocalPlace switch
            {
                1 => "Victory!",
                0 => "Duel Over",
                _ when Duel.PlayerCount <= 2 => "Defeat",
                2 => "2nd Place",
                3 => "3rd Place",
                var place => place + "th Place",
            });
        }
    }
}
