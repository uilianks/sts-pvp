using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace StsPvp;

/// <summary>
/// The room before the duel: each player picks <see cref="CardCount"/> cards from their
/// character's pool and then <see cref="RelicCount"/> relics, one out of
/// <see cref="RelicChoices"/> at a time. A per-player (non-shared) event, like the game's own
/// card-picking events: the picks go through the game's choice sync, so both games end up with
/// the same decks and relics.
/// </summary>
public sealed class PvpDraft : EventModel
{
    public const int CardCount = 10;
    public const int RelicCount = 5;
    public const int RelicChoices = 3;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return new[] { new EventOption(this, Draft, "PVP_DRAFT.pages.INITIAL.options.DRAFT") };
    }

    private async Task Draft()
    {
        var owner = Owner!;
        var run = owner.RunState;

        var pool = owner.Character.CardPool
            .GetUnlockedCards(run.UnlockState, CardMultiplayerConstraint.MultiplayerOnly)
            .Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
            .OrderBy(c => c.Rarity)
            .ThenBy(c => c.Id.Entry, StringComparer.Ordinal)
            .Select(c => run.CreateCard(c, owner))
            .ToList();
        var cards = await CardSelectCmd.FromSimpleGrid(new BlockingPlayerChoiceContext(), pool, owner,
            new CardSelectorPrefs(L10NLookup("PVP_DRAFT.cardsPrompt"), CardCount));
        foreach (var card in cards)
        {
            CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(card, PileType.Deck));
        }

        var owned = owner.Relics.Select(r => r.Id).ToHashSet();
        var relics = ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(run.UnlockState)
            .Concat(owner.Character.RelicPool.GetUnlockedRelics(run.UnlockState))
            .Where(r => r.Rarity is RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop)
            .Where(r => !owned.Contains(r.Id))
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Id.Entry, StringComparer.Ordinal)
            .ToList();
        Rng.Shuffle(relics);
        for (var round = 0; round < RelicCount; round++)
        {
            var offer = relics.Skip(round * RelicChoices).Take(RelicChoices).Select(r => r.ToMutable()).ToList();
            if (offer.Count == 0)
            {
                break;
            }
            var relic = await RelicSelectCmd.FromChooseARelicScreen(owner, offer);
            Log.Info($"[pvp] draft: player {owner.NetId} relic {round + 1}/{RelicCount}: {relic?.Id.Entry ?? "skipped"}");
            if (relic != null)
            {
                await RelicCmd.Obtain(relic, owner);
            }
        }

        SetEventFinished(L10NLookup("PVP_DRAFT.pages.DONE.description"));
    }
}
