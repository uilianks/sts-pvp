# sts-pvp

Slay the Spire PvP over LAN / Hamachi: two players, each on their own PC, fight each other.
The game installed here is **Slay the Spire 1** (`desktop-1.0.jar`); this mod targets it.
No ModTheSpire needed, the game install is not modified.

## Play

Both players need the same game version and the same `sts-pvp.jar`.

Linux (this machine):

    ./build.sh                 # builds dist/sts-pvp.jar from the installed game
    ./run.sh host              # host: waits on TCP port 24680
    ./run.sh join 25.x.y.z     # other player: the host's Hamachi IPv4

Windows: copy `dist/sts-pvp.jar` and `dist/run.bat` into the game folder
(Steam > Slay the Spire > Manage > Browse local files), then run `run.bat host` or
`run.bat join 25.x.y.z` from a terminal in that folder. On first host, allow Java through the
Windows firewall (Hamachi network). An optional last argument changes the port on both sides.

Then both start a run (any character, any Neow bonus) and enter the first fight on the map:
that combat is the duel. Game log lines starting with `[pvp]` show the connection.

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
