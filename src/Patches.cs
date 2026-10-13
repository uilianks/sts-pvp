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

/// <summary>The run's first room is the duel. Both games run this identically (lockstep).</summary>
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
        Duel.Started = true;
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
        // Everyone dead is a normal co-op loss; the game already shows its game over screen.
        if (players.All(p => p.Creature.IsDead))
        {
            Duel.Decided = true;
            return;
        }
        Duel.Decided = true;
        Duel.LocalWon = !me.Creature.IsDead;
        Log.Info($"[pvp] duel decided, local player {(Duel.LocalWon ? "won" : "lost")}");
        CombatManager.Instance.LoseCombat();
        NRun.Instance?.RunMusicController.StopMusic();
        // isVictory: true keeps the opponent proxy out of the enemy discovery list.
        var run = RunManager.Instance.OnEnded(isVictory: true);
        NRun.Instance?.ShowGameOverScreen(run);
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
