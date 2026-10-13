using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsPvp;

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
                "Before the duel, build your deck: pick 10 cards to add to it, then 5 relics (one out of three each time).",
            ["PVP_DRAFT.pages.INITIAL.options.DRAFT.title"] = "Draft",
            ["PVP_DRAFT.pages.INITIAL.options.DRAFT.description"] = "Pick 10 cards, then 5 relics.",
            ["PVP_DRAFT.cardsPrompt"] = "Choose {Amount} cards to add to your deck",
            ["PVP_DRAFT.pages.DONE.description"] =
                "Ready. When both players are done, proceed and pick the next room on the map: it is the duel.",
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
            __result = new EventRoom(ModelDb.Event<PvpDraft>());
            Log.Info("[pvp] draft room created");
            return false;
        }
        Duel.Started = true;
        Duel.WatchForForfeit(__instance.NetService);
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
/// Alternating turns: each player-side turn is given to one player only (host on odd rounds,
/// the other player on even rounds), through the game's own "players taking the turn" list,
/// the same one extra-turn effects use. The other player is marked done automatically, keeps
/// their block and draws nothing. Both games derive it from the round number, so they agree.
/// </summary>
[HarmonyPatch(typeof(CombatManager), "StartTurn")]
internal static class AlternatingTurnsPatch
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
        var active = players[(____state.RoundNumber - 1) % players.Count];
        if (active.Creature.IsDead)
        {
            return;
        }
        ____playersTakingExtraTurn.Add(active);
    }
}

/// <summary>When a player dies the duel is decided: both games show the end screen.</summary>
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
        // Everyone dead is a normal co-op loss; the game already shows its game over screen.
        if (players.All(p => p.Creature.IsDead))
        {
            Duel.Decided = true;
            return;
        }
        Duel.Finish(won: !me.Creature.IsDead, "a player died");
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

[HarmonyPatch(typeof(NGameOverScreen), "InitializeBannerAndQuote")]
internal static class DuelBannerPatch
{
    private static void Postfix(NCommonBanner ____banner)
    {
        if (Duel.Decided)
        {
            ____banner.label.SetTextAutoSize(Duel.LocalWon ? "Victory!" : "Defeat");
        }
    }
}
