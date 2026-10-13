# sts-pvp

Slay the Spire PvP: two players, each on their own PC, fight each other over a virtual LAN
(Hamachi or ZeroTier) or a real one. Works with **Slay the Spire 1** (the Steam game
"Slay the Spire", not Slay the Spire 2). No ModTheSpire needed, and the game install is not
changed: the mod only runs when you start the game through `run.bat` / `run.sh`.

## Setup

Both players need:

- Slay the Spire on Steam, updated, on the normal branch (Steam > Slay the Spire > Properties >
  Betas: "None"). Both games must be the same version.
- The same `sts-pvp.jar`. The jar is not in this repo (it contains a patched copy of one game
  class), so either get it from the other player or build it yourself (see below).
- The same virtual network: both join the same Hamachi network or the same ZeroTier network
  (on ZeroTier, the network admin must tick "Auth" for both PCs in ZeroTier Central).

### Windows

1. Get `sts-pvp.jar` and `run.bat` (from the other player, or build them, see below).
2. Open the game folder: Steam > right click Slay the Spire > Manage > Browse local files.
   It contains `desktop-1.0.jar` and a `jre` folder.
3. Copy `sts-pvp.jar` and `run.bat` into that folder.
4. Open a terminal there: click the folder's address bar, type `cmd`, press Enter.
5. Start the game in that terminal:
   - the host: `run.bat host`
   - the other player: `run.bat join <host IP>`

   The host IP is the host's address on the virtual network: in Hamachi, the IPv4 shown at the
   top of the window (`25.x.y.z`); in ZeroTier, the "Managed IP" of the network
   (`zerotier-cli listnetworks`, or ZeroTier Central).
6. The first time the host runs it, Windows asks whether Java may use the network: allow it
   (tick private networks; Hamachi/ZeroTier networks may count as public, tick that too).

To play the normal game again, start it from Steam as usual.

### Linux

    ./run.sh host              # the host
    ./run.sh join <host IP>    # the other player

`run.sh` builds the jar first if `dist/sts-pvp.jar` is missing. If the game is not in the
default Steam folder, set it: `STS_DIR=/path/to/SlayTheSpire ./run.sh host`. If the host has a
firewall, open TCP port 24680 (`sudo ufw allow 24680/tcp` or
`sudo firewall-cmd --add-port=24680/tcp`).

### Play the duel

1. Both start the game with the commands above (host first is easiest, the other player
   keeps retrying until the host is up).
2. Both start a new run, any character, any Neow bonus.
3. Both enter the first fight on the map. That fight is the duel: the enemy is the other
   player. If one arrives first, they see "Waiting for opponent..." until the other gets there.

### Troubleshooting

- Stuck on "Waiting for opponent...": check that `ping <host IP>` works from the other PC,
  that both used the same port, and that the host's firewall allows Java / port 24680.
- Port 24680 is taken or blocked: pick another one, the same on both sides:
  `run.bat host 30000` and `run.bat join <host IP> 30000`.
- `run.bat` says "usage": it was double clicked; run it from a terminal with `host` or `join`.
- The terminal window shows lines starting with `[pvp]`: they say whether it is hosting,
  joining or connected.

### Building the jar

Needs a JDK 11 or newer (`javac`, `jar`) and the game installed.

- Linux: `./build.sh` → `dist/sts-pvp.jar` and `dist/run.bat`.
- Windows: install a JDK (e.g. Temurin 17) and Git for Windows, then in Git Bash, in this repo:

      STS_DIR="/c/Program Files (x86)/Steam/steamapps/common/SlayTheSpire" ./build.sh

  (adjust the path if the Steam library is elsewhere). The result is in `dist/`.

## Rules

- The enemy is the other player, drawn as their character, with their real HP and block.
- Turns are simultaneous: play your cards, end turn; the next turn starts once both ended
  ("Waiting for opponent..."). The fight also waits at the start until both are in it.
- Your damage is resolved by your game (strength, Vulnerable on the opponent, relics like
  The Boot) and applied by theirs to their real player (their block, Buffer, Intangible,
  Thorns hits you back, etc.). Poison and HP loss go through as HP loss.
- The duel ends when a player dies; the winner gets normal combat rewards. If the opponent
  disconnects, you win. One duel per launch; after it the game plays normally.
- The run is flagged as modded, so no achievements, leaderboards or metrics.

## How it works

`tools/PatchGetEncounter.java` prepends to `MonsterHelper.getEncounter(String)`, which every
combat room uses to build its monsters: `MonsterGroup duel = pvp.Pvp.encounter(); if (duel != null) return duel;`.
The patched class sits in `sts-pvp.jar` ahead of `desktop-1.0.jar` on the classpath and
shadows the original.

- `Net`: one TCP line protocol (`HELLO class`, `STATE hp maxHp block`, `HIT amount type`, `SYNC n`).
- `Pvp`: session, message handling on the game thread, a background thread streaming the
  local player's HP/block (so a death is reported even when combat stops updating).
- `OpponentMonster`: mirrors the remote player; `damage()` runs the game's own damage
  resolution against a probe HP with no block and forwards the HP that would have been lost;
  its turn is only the barrier.
- `WaitForOpponentAction`: holds the action queue until the other game reaches the same barrier.

## Findings

REA 6.4.0 (`~/Projetos/rea`) cannot analyse JVM bytecode: on a `.class` it reads the
`CAFEBABE` magic as a Mach-O FAT header and rejects it (`analysis/rea.inspect-MonsterHelper.json`),
and it refuses `desktop-1.0.jar` itself (see `sts-instawin/analysis`). The game bytecode was
read with `javap` instead:

- `MonsterRoom.onPlayerEntry` → `AbstractDungeon.getMonsterForRoomCreation` → `MonsterHelper.getEncounter`
  (also used for elites and bosses).
- `InstantKillAction` sets `currentHealth = 0` then calls `damage(HP_LOSS 0)`; `OpponentMonster`
  detects such direct HP changes and forwards them as HP loss.
- Characters' idle skeletons: `images/characters/{ironclad,theSilent,defect,watcher}/idle/skeleton.{atlas,json}`,
  animation `Idle`.

## Verified / not verified

Verified: the build; the patched `getEncounter` loads from the mod jar; every mod class and the
patched class pass `-Xverify:all` on the game's bundled Java 1.8.0_131; host/client exchange over
loopback (`test/VerifyAndNetTest.java`).

Not verified: an actual duel in game (needs two players), how the mirrored opponent looks
(position, facing), and play across Hamachi.
