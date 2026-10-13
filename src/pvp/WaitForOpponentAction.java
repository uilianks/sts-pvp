package pvp;

import com.megacrit.cardcrawl.actions.AbstractGameAction;
import com.megacrit.cardcrawl.characters.AbstractPlayer;
import com.megacrit.cardcrawl.dungeons.AbstractDungeon;
import com.megacrit.cardcrawl.vfx.ThoughtBubble;

/** Holds the action queue until the opponent reaches the same barrier (or is gone). */
public class WaitForOpponentAction extends AbstractGameAction {
    private final int sync;
    private boolean announced;

    WaitForOpponentAction(int sync) {
        this.sync = sync;
        this.actionType = ActionType.WAIT;
    }

    @Override
    public void update() {
        Pvp.poll();
        if (Pvp.barrierReached(sync) || Pvp.opponentDefeated()) {
            isDone = true;
            return;
        }
        if (!announced) {
            announced = true;
            AbstractPlayer p = AbstractDungeon.player;
            AbstractDungeon.effectList.add(new ThoughtBubble(p.dialogX, p.dialogY, 3.0F, "Waiting for opponent...", true));
        }
    }
}
