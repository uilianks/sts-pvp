using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;

namespace StsPvp;

/// <summary>
/// Duel session state. A duel is a normal 2-player co-op run (same lobby, same lockstep sync)
/// whose first room is <see cref="PvpDuel"/>: one <see cref="PvpOpponent"/> proxy per player on
/// the enemy side. Whatever targets a proxy is redirected to the player it stands for, so both
/// games resolve every hit identically. The run ends when a player dies.
/// </summary>
public static class Duel
{
    /// <summary>UDP port of the game's own ENet co-op host.</summary>
    public const ushort Port = 33771;

    /// <summary>Set by the Duel menu on both host and client; cleared back on the main menu.</summary>
    public static bool Active { get; private set; }

    /// <summary>The draft room was created (the run's first room).</summary>
    public static bool Drafted { get; set; }

    /// <summary>The duel room was entered (only the first room of the run becomes the duel).</summary>
    public static bool Started { get; set; }

    /// <summary>A player died and the end screen was shown.</summary>
    public static bool Decided { get; set; }

    /// <summary>Outcome for this game, valid once <see cref="Decided"/>.</summary>
    public static bool LocalWon { get; set; }

    public static void Begin()
    {
        Active = true;
        Drafted = false;
        Started = false;
        Decided = false;
        Log.Info("[pvp] duel mode on");
    }

    public static void Reset()
    {
        if (Active)
        {
            Log.Info("[pvp] duel mode off");
        }
        Active = false;
        Drafted = false;
        Started = false;
        Decided = false;
    }

    /// <summary>
    /// Ends the duel on this game: Victory/Defeat on the game over screen (the banner is set by
    /// the game over patch from <see cref="LocalWon"/>).
    /// </summary>
    public static void Finish(bool won, string why)
    {
        if (Decided)
        {
            return;
        }
        Decided = true;
        LocalWon = won;
        Log.Info($"[pvp] duel decided ({why}), local player {(won ? "won" : "lost")}");
        if (CombatManager.Instance.IsInProgress)
        {
            CombatManager.Instance.LoseCombat();
        }
        NRun.Instance?.RunMusicController.StopMusic();
        // isVictory: true keeps the opponent proxy out of the enemy discovery list.
        var run = RunManager.Instance.OnEnded(isVictory: true);
        NRun.Instance?.ShowGameOverScreen(run);
    }

    /// <summary>The opponent leaving mid-duel is a forfeit: the player who stayed wins.</summary>
    public static void WatchForForfeit(INetGameService net)
    {
        if (net is NetHostGameService host)
        {
            host.ClientDisconnected += (_, _) => Forfeit();
        }
        else
        {
            net.Disconnected += info =>
            {
                // Our own quit is not a forfeit by the host.
                if (!info.SelfInitiated)
                {
                    Forfeit();
                }
            };
        }
    }

    private static void Forfeit()
    {
        if (Active && Started && !Decided && !RunManager.Instance.IsAbandoned)
        {
            Finish(won: true, "opponent left");
        }
    }

    /// <summary>The player a proxy creature stands for, or null if it is not a proxy.</summary>
    public static Player? PlayerFor(Creature? creature)
    {
        if (creature?.Monster is not PvpOpponent proxy)
        {
            return null;
        }
        var players = creature.CombatState?.Players;
        return players != null && proxy.PlayerIndex < players.Count ? players[proxy.PlayerIndex] : null;
    }

    /// <summary>
    /// Replaces proxies with the players they stand for. A proxy standing for <paramref name="actor"/>
    /// itself (area attacks hit both proxies) is dropped: you never hit yourself through it.
    /// </summary>
    public static List<Creature> Redirect(IEnumerable<Creature> targets, Creature? actor)
    {
        var result = new List<Creature>();
        foreach (var target in targets)
        {
            var player = PlayerFor(target);
            if (player == null)
            {
                result.Add(target);
            }
            else if (player.Creature != actor && !result.Contains(player.Creature))
            {
                result.Add(player.Creature);
            }
        }
        return result;
    }
}
