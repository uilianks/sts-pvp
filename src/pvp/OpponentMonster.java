package pvp;

import com.megacrit.cardcrawl.cards.DamageInfo;
import com.megacrit.cardcrawl.monsters.AbstractMonster;

/**
 * The other player, as a monster. HP and block mirror the remote player; damage dealt to it is
 * forwarded instead of applied, and its turn is only the barrier with the other game.
 */
public class OpponentMonster extends AbstractMonster {
    public static final String ID = "pvp:Opponent";
    private static final int PROBE_HP = Integer.MAX_VALUE / 2;

    private String shownClass;
    private boolean dying;

    public OpponentMonster() {
        super("Opponent", ID, 80, -8.0F, 10.0F, 220.0F, 300.0F, null, 0.0F, 0.0F);
        type = EnemyType.NORMAL;
        flipHorizontal = true;
        show("IRONCLAD");
    }

    /** Shows the opponent's character, facing the local player. */
    private void show(String playerClass) {
        String dir;
        switch (playerClass) {
            case "THE_SILENT": dir = "theSilent"; break;
            case "DEFECT": dir = "defect"; break;
            case "WATCHER": dir = "watcher"; break;
            default: dir = "ironclad";
        }
        loadAnimation("images/characters/" + dir + "/idle/skeleton.atlas",
                "images/characters/" + dir + "/idle/skeleton.json", 1.0F);
        state.setAnimation(0, "Idle", true);
        name = "Opponent (" + dir + ")";
        shownClass = playerClass;
    }

    /** Copies the remote player's state onto this monster. */
    void mirror() {
        String cls = Pvp.remoteClass;
        if (cls != null && !cls.equals(shownClass)) {
            show(cls);
        }
        if (Pvp.remoteHp >= 0) {
            maxHealth = Math.max(Pvp.remoteMaxHp, 1);
            currentHealth = Math.max(Pvp.remoteHp, 0);
            currentBlock = Pvp.remoteBlock;
            healthBarUpdatedEvent();
        }
        if (Pvp.opponentDefeated() && !dying) {
            dying = true;
            currentHealth = 0;
            currentBlock = 0;
            Pvp.duelDecided();
            super.die();
        }
    }

    @Override
    public void update() {
        Pvp.poll();
        super.update();
    }

    /**
     * Runs the game's own damage resolution (the attacker's relics and powers, this monster's
     * Vulnerable etc.) against a probe HP with no block, and forwards the HP that would have been
     * lost. The opponent's game applies its real block and defences.
     */
    @Override
    public void damage(DamageInfo info) {
        if (dying) {
            return;
        }
        int mirroredHp = Pvp.remoteHp >= 0 ? Pvp.remoteHp : currentHealth;
        // Effects such as InstantKillAction set currentHealth directly before calling damage.
        int setDirectly = Math.max(mirroredHp - currentHealth, 0);
        int block = currentBlock;
        currentBlock = 0;
        currentHealth = PROBE_HP;
        super.damage(info);
        int lost = PROBE_HP - currentHealth;
        currentHealth = mirroredHp;
        currentBlock = block;
        healthBarUpdatedEvent();
        if (setDirectly > 0) {
            Pvp.hit(setDirectly, DamageInfo.DamageType.HP_LOSS);
        }
        if (lost > 0) {
            Pvp.hit(lost, info.type);
        }
    }

    /** Only the opponent's own game decides that its player is dead (see mirror). */
    @Override
    public void die(boolean triggerRelics) {
        if (dying) {
            super.die(triggerRelics);
        }
    }

    @Override
    public void usePreBattleAction() {
        addToBot(Pvp.barrier());
    }

    @Override
    public void takeTurn() {
        addToBot(Pvp.barrier());
    }

    @Override
    protected void getMove(int num) {
        setMove((byte) 0, Intent.UNKNOWN);
    }
}
