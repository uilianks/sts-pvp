using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
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
/// Duel session state. A duel is a normal co-op run of 2 to <see cref="MaxPlayers"/> players (same
/// lobby, same lockstep sync) whose first room is <see cref="PvpDuel"/>: one <see cref="PvpOpponent"/>
/// proxy per player on the enemy side. Whatever targets a proxy is redirected to the player it
/// stands for, so every game resolves every hit identically. Free-for-all: a player who dies
/// watches until the run ends, when only one player is left standing.
/// </summary>
public static class Duel
{
    /// <summary>UDP port of the game's own ENet co-op host.</summary>
    public const ushort Port = 33771;

    /// <summary>Host included; the game's own co-op limit.</summary>
    public const int MaxPlayers = 4;

    /// <summary>Set by the Duel menu on both host and client; cleared back on the main menu.</summary>
    public static bool Active { get; private set; }

    /// <summary>The draft room was created (the run's first room).</summary>
    public static bool Drafted { get; set; }

    /// <summary>The duel room was entered (only the first room of the run becomes the duel).</summary>
    public static bool Started { get; set; }

    /// <summary>A player died and the end screen was shown.</summary>
    public static bool Decided { get; set; }

    /// <summary>
    /// Final place of the local player (1 = winner), set when they die or the duel is decided;
    /// 0 while still fighting, and after a duel that ended without a result.
    /// </summary>
    public static int LocalPlace { get; set; }

    /// <summary>Players in the duel, for the end banner.</summary>
    public static int PlayerCount { get; set; }

    /// <summary>The local player's character this duel, kept for a same-character rematch.</summary>
    public static MegaCrit.Sts2.Core.Models.CharacterModel? LocalCharacter { get; set; }

    /// <summary>Index (in player order) where the search for the next turn's player starts.</summary>
    public static int NextTurn { get; set; }

    public static void Begin()
    {
        Active = true;
        Drafted = false;
        Started = false;
        Decided = false;
        LocalPlace = 0;
        NextTurn = 0;
        Log.Info("[pvp] duel mode on");
    }

    public static void Reset()
    {
        if (Active)
        {
            Log.Info("[pvp] duel mode off");
        }
        Active = false;
        SteamMatch.ForgetHostedLobby();
        Drafted = false;
        Started = false;
        Decided = false;
    }

    /// <summary>
    /// Ends the duel on this game: the game over screen, whose banner the game over patch sets
    /// from <see cref="LocalPlace"/> (0 = no result).
    /// </summary>
    public static void Finish(int place, string why)
    {
        if (Decided)
        {
            return;
        }
        Decided = true;
        LocalPlace = place;
        Log.Info($"[pvp] duel decided ({why}), local place {place} of {PlayerCount}");
        if (CombatManager.Instance.IsInProgress)
        {
            CombatManager.Instance.LoseCombat();
        }
        NRun.Instance?.RunMusicController.StopMusic();
        // isVictory: true keeps the opponent proxy out of the enemy discovery list.
        var run = RunManager.Instance.OnEnded(isVictory: true);
        NRun.Instance?.ShowGameOverScreen(run);
    }

    /// <summary>
    /// A player still fighting who leaves forfeits. If that leaves one player standing, they win;
    /// with more left the duel cannot go on (nobody plays the leaver's turns) and ends with no
    /// result. A player who already lost may leave freely, unless it is the host: the others lose
    /// the connection and the duel ends with no result.
    /// </summary>
    public static void WatchForForfeit(RunManager runManager, IRunState run)
    {
        if (runManager.RunLobby is { } lobby)
        {
            lobby.RemotePlayerDisconnected += netId => PlayerLeft(run, run.Players.FirstOrDefault(p => p.NetId == netId));
        }
        if (runManager.NetService is NetClientGameService client)
        {
            var host = run.Players.FirstOrDefault(p => p.NetId == client.HostNetId);
            client.Disconnected += info =>
            {
                // Our own quit is not a forfeit by the host.
                if (!info.SelfInitiated)
                {
                    PlayerLeft(run, host, connectionLost: true);
                }
            };
        }
    }

    private static void PlayerLeft(IRunState run, Player? leaver, bool connectionLost = false)
    {
        if (!Active || !Started || Decided || RunManager.Instance.IsAbandoned || leaver == null ||
            (leaver.Creature.IsDead && !connectionLost))
        {
            return;
        }
        var me = LocalContext.GetMe(run.Players);
        if (me == null)
        {
            return;
        }
        // Me and the leaver were the last two standing.
        var won = !me.Creature.IsDead && !leaver.Creature.IsDead && run.Players.Count(p => !p.Creature.IsDead) == 2;
        Finish(won ? 1 : me.Creature.IsDead ? LocalPlace : 0, $"player {leaver.NetId} left");
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
    /// Where a creature is drawn on this screen: another player's own body is hidden (see
    /// <c>ArrangeDuelCreature</c>), so it is shown by its proxy on the enemy side.
    /// </summary>
    public static Creature Shown(Creature creature)
    {
        if (!Active || !Started || creature.Player is not { } player || LocalContext.IsMe(player))
        {
            return creature;
        }
        return creature.CombatState?.Enemies.FirstOrDefault(e => PlayerFor(e) == player) ?? creature;
    }

    /// <summary>
    /// Replaces proxies with the players they stand for. A proxy standing for <paramref name="actor"/>
    /// itself (area attacks hit every proxy) is dropped: you never hit yourself through it.
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
