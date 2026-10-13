using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace StsPvp;

/// <summary>The duel fight: one <see cref="PvpOpponent"/> per player, in player order.</summary>
public sealed class PvpDuel : EncounterModel
{
    private string[] _characters = Array.Empty<string>();

    public override RoomType RoomType => RoomType.Monster;

    public override bool ShouldGiveRewards => false;

    public override IEnumerable<MonsterModel> AllPossibleMonsters => new[] { ModelDb.Monster<PvpOpponent>() };

    /// <summary>A mutable duel for these players (character of each, in run order).</summary>
    public static PvpDuel For(IReadOnlyList<Player> players)
    {
        var duel = (PvpDuel)ModelDb.Encounter<PvpDuel>().ToMutable();
        duel._characters = players.Select(p => p.Character.Id.Entry).ToArray();
        return duel;
    }

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        var monsters = new List<(MonsterModel, string?)>();
        for (var i = 0; i < _characters.Length; i++)
        {
            var proxy = (PvpOpponent)ModelDb.Monster<PvpOpponent>().ToMutable();
            proxy.PlayerIndex = i;
            proxy.CharacterEntry = _characters[i];
            monsters.Add((proxy, null));
        }
        return monsters;
    }
}
