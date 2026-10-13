package pvp;

import com.megacrit.cardcrawl.actions.AbstractGameAction;
import com.megacrit.cardcrawl.cards.DamageInfo;
import com.megacrit.cardcrawl.characters.AbstractPlayer;
import com.megacrit.cardcrawl.core.Settings;
import com.megacrit.cardcrawl.dungeons.AbstractDungeon;
import com.megacrit.cardcrawl.monsters.MonsterGroup;

/**
 * Duel session. Each game is authoritative for its own player: hits on the opponent monster
 * are sent as HIT and resolved by the other game against its real player (block, powers,
 * relics), and each side streams its player's STATE so the other mirrors it on the monster.
 *
 * Messages (one line each): HELLO class | STATE hp maxHp block | HIT amount type | SYNC n
 *
 * Enabled by -Dpvp.host=true or -Dpvp.join=address (optional -Dpvp.port). One duel per launch:
 * every combat is replaced by the duel until it is decided, then the game is back to normal.
 */
public final class Pvp {
    static final int DEFAULT_PORT = 24680;

    private static Net net;
    private static OpponentMonster opponent;
    private static boolean duelOver;

    // Opponent's player as last reported; remoteHp < 0 until the first STATE.
    static volatile String remoteClass;
    static int remoteHp = -1;
    static int remoteMaxHp;
    static int remoteBlock;

    // Turn barrier: both games count barriers the same way (battle start, then each end of turn).
    private static int localSync;
    private static int remoteSync;

    private Pvp() {}

    /** Prepended to MonsterHelper.getEncounter: non-null replaces the encounter with the duel. */
    public static MonsterGroup encounter() {
        if (duelOver || !configured()) {
            return null;
        }
        if (net == null) {
            String join = System.getProperty("pvp.join");
            net = new Net(join == null || join.isEmpty() ? null : join, Integer.getInteger("pvp.port", DEFAULT_PORT));
            startStateStream();
        }
        // Like the instawin patch: the game then skips achievements, leaderboards and metrics.
        Settings.isModded = true;
        opponent = new OpponentMonster();
        return new MonsterGroup(opponent);
    }

    static boolean configured() {
        return Boolean.getBoolean("pvp.host") || System.getProperty("pvp.join") != null;
    }

    /** Applies everything received so far. Game thread only. */
    static void poll() {
        if (net == null) {
            return;
        }
        for (String line; (line = net.inbox.poll()) != null; ) {
            handle(line.split(" "));
        }
        if (opponent != null) {
            opponent.mirror();
        }
    }

    private static void handle(String[] m) {
        try {
            switch (m[0]) {
                case "HELLO":
                    remoteClass = m[1];
                    break;
                case "STATE":
                    remoteHp = Integer.parseInt(m[1]);
                    remoteMaxHp = Integer.parseInt(m[2]);
                    remoteBlock = Integer.parseInt(m[3]);
                    break;
                case "HIT":
                    AbstractPlayer p = AbstractDungeon.player;
                    if (opponent != null && p != null && !p.isDead) {
                        p.damage(new DamageInfo(opponent, Integer.parseInt(m[1]), DamageInfo.DamageType.valueOf(m[2])));
                    }
                    break;
                case "SYNC":
                    remoteSync = Integer.parseInt(m[1]);
                    break;
                default:
                    log("unknown message " + String.join(" ", m));
            }
        } catch (RuntimeException e) {
            log("bad message " + String.join(" ", m) + ": " + e);
        }
    }

    /** The opponent's player is dead, or left: either way this side has won. */
    static boolean opponentDefeated() {
        return remoteHp == 0 || (net != null && net.lost());
    }

    static void duelDecided() {
        duelOver = true;
        opponent = null;
    }

    static void hit(int amount, DamageInfo.DamageType type) {
        net.send("HIT " + amount + " " + type.name());
    }

    /** Next turn barrier: announces it and returns the action that waits for the opponent. */
    static AbstractGameAction barrier() {
        localSync++;
        net.send("SYNC " + localSync);
        return new WaitForOpponentAction(localSync);
    }

    static boolean barrierReached(int sync) {
        return remoteSync >= sync;
    }

    /**
     * Streams the local player's class, HP and block from a background thread, so a death is
     * reported even when the game stops updating the combat room.
     */
    private static void startStateStream() {
        Thread t = new Thread(() -> {
            String lastHello = null;
            String lastState = null;
            while (true) {
                try {
                    Thread.sleep(100);
                } catch (InterruptedException e) {
                    return;
                }
                AbstractPlayer p = AbstractDungeon.player;
                if (p == null || !net.connected()) {
                    continue;
                }
                String hello = "HELLO " + p.chosenClass.name();
                String state = "STATE " + p.currentHealth + " " + p.maxHealth + " " + p.currentBlock;
                if (!hello.equals(lastHello)) {
                    net.send(hello);
                    lastHello = hello;
                }
                if (!state.equals(lastState)) {
                    net.send(state);
                    lastState = state;
                }
            }
        }, "pvp-state");
        t.setDaemon(true);
        t.start();
    }

    static void log(String s) {
        System.out.println("[pvp] " + s);
    }
}
