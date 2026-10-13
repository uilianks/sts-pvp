using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace StsPvp;

/// <summary>
/// Stand-in for a player on the enemy side: drawn as that player's character, never acts.
/// It is only a target; see <see cref="Duel.Redirect"/> and the display patch, which shows the
/// real player's HP, block and powers on it.
/// </summary>
public sealed class PvpOpponent : MonsterModel
{
    /// <summary>Index in the run's player list, set by <see cref="PvpDuel"/>.</summary>
    public int PlayerIndex;

    /// <summary>Character model entry (e.g. IRONCLAD) of that player, for the visuals.</summary>
    public string CharacterEntry = "IRONCLAD";

    public override LocString Title => MonsterModel.L10NMonsterLookup("PVP_OPPONENT.name");

    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/" + CharacterEntry.ToLowerInvariant());

    // Never takes damage itself (everything is redirected); the HP only has to keep it alive.
    public override int MinInitialHp => 9999;

    public override int MaxInitialHp => 9999;

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var nothing = new MoveState("NOTHING", _ => Task.CompletedTask, new HiddenIntent());
        nothing.FollowUpState = nothing;
        return new MonsterMoveStateMachine(new List<MonsterState> { nothing }, nothing);
    }
}
