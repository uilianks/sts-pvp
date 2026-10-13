using MegaCrit.Sts2.Core.Entities.Creatures;
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

    /// <summary>The duel room was entered (only the first room of the run becomes the duel).</summary>
    public static bool Started { get; set; }

    /// <summary>A player died and the end screen was shown.</summary>
    public static bool Decided { get; set; }

    /// <summary>Outcome for this game, valid once <see cref="Decided"/>.</summary>
    public static bool LocalWon { get; set; }

    public static void Begin()
    {
        Active = true;
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
        Started = false;
        Decided = false;
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
