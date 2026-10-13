# sts-pvp: Slay the Spire 2 Duel

A Slay the Spire 2 mod that adds a **Duel** button to the main menu: two players, each on their
own PC, fight each other. It reuses the game's own co-op: same lobby, same character select,
same turn sync, connected by IP, so it works over LAN, Hamachi or ZeroTier.

Built for game version **v0.107.1** (Steam, public branch).

## Setup (both players)

1. **Same game version** on both PCs: Steam > Slay the Spire 2 > Properties > Betas: "None".
2. **Get the mod**, either way:
   - **Steam Workshop** (easiest): open the
     [PvP Duel Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3819085998)
     and click **Subscribe**; Steam installs and updates it. The item is public. Then skip to step 4.
   - **Manual**: download `StsPvp.zip` from the latest
     [release](https://github.com/uilianks/sts-pvp/releases/latest) and unzip it into the
     `mods` folder (step 3). You can also build it (see below).

   Use one or the other: if the mod is both subscribed and in `mods`, the game disables the
   Workshop copy.
3. **Manual install only: put it in the game's `mods` folder.** Steam > right click Slay the Spire 2 > Manage >
   Browse local files. Create a folder named `mods` there if it does not exist, and copy the
   `StsPvp` folder into it:

       Slay the Spire 2/
         SlayTheSpire2(.exe)
         mods/
           StsPvp/
             StsPvp.dll
             StsPvp.json

   On Linux, `./install.sh` does this.
4. **Start the game from Steam.** The first time, the game asks whether to load mods: accept.
   The main menu then shows **Duel** under Multiplayer.
5. **Same mods on both PCs.** Co-op checks that both games match, so any other mod has to be
   on both sides too. If one of you has extra mods (e.g. from the Workshop), turn them off in
   *Mod Configuration* in the main menu.
6. **Same virtual network**: both in the same Hamachi network, or the same ZeroTier network
   (the ZeroTier admin must tick "Auth" for both PCs in ZeroTier Central).

## Play

1. Host: **Duel**. The panel lists your IPs (ZeroTier, Hamachi, LAN): click **Copy** next to
   the one your friend can reach and send it to them. Then **Host**: the game's co-op
   character select opens and waits.
2. Other player: **Duel**, type the host's IP, **Join**.
   - Hamachi: the IPv4 at the top of the Hamachi window (`25.x.y.z`).
   - ZeroTier: the "Managed IP" of the network (`zerotier-cli listnetworks` or ZeroTier Central).
   - Same LAN: the host's local IP (`192.168.x.y`).
3. Both pick a character and ready up; the host starts the run.
4. **Draft**: the run opens on the *Duel Draft* event. Click **Draft**, pick **10 cards** from
   your character's pool (Common, Uncommon, Rare) to add to your deck, then **5 relics**, one
   out of three each time. When both are done, click **Proceed** and pick the next room on
   the map: it is the duel.
5. The duel: you on the left, the other player on the right,
   facing you, with their real HP, block and powers.
6. Turns alternate: the host plays first, ends turn, then the other player plays, and so on.
   While it is not your turn you have no cards and wait ("Opponent's Turn"). Block lasts
   through the opponent's turn and resets at the start of your own. Attacks, debuffs, area
   attacks and potions aimed at the opponent hit the other player for real (their block,
   Vulnerable, Thorns, etc. all apply).
7. When a player dies, both see the end screen: **Victory!** or **Defeat**. If the opponent
   quits mid-duel, you win. Main Menu to play again.

Duel runs are not saved and do not count towards stats or unlocks.

### Firewall

The host listens on **UDP 33771** (the game's own co-op port).
- Windows: when Windows asks whether Slay the Spire 2 may use the network, allow it for
  private and public networks (Hamachi/ZeroTier adapters are often "public").
- Linux: `sudo ufw allow 33771/udp` or `sudo firewall-cmd --add-port=33771/udp`.

### Troubleshooting

- *Could not connect*: from the joining PC, `ping <host IP>` must work; the host must have
  clicked Host first; check the firewall above.
- *Could not host*: another copy of the game is already hosting on this PC.
- *Not in a duel lobby*: the host opened normal Multiplayer instead of Duel.
- The Duel button is missing: the mod is not loaded. Check the folder layout above and that
  mods were accepted (Mod Configuration in the main menu lists it as enabled).
- Game log: Linux `~/.local/share/SlayTheSpire2/logs/`, Windows
  `%APPDATA%\SlayTheSpire2\logs\`; the mod's lines start with `[pvp]`.

## Try it alone: two games on one PC

    ./simulate.sh

Opens two copies of the game side by side, already connected over 127.0.0.1 (the first hosts,
the second joins). Pick a character and click ✓ in both windows, start from the host, and play
both sides of the duel. It runs without Steam, gives each copy a throwaway data folder seeded
from your settings and progress (your saves are not touched), and puts the built mod in `mods/`
only for the session. Logs: `/tmp/sts-pvp-sim/host.log` and `client.log`.

The same auto start works on any install: launch the game with `--pvp-host` or
`--pvp-join=<ip>` to open the Duel panel and host or join right away.

## Build

Needs the .NET 9 SDK and the game installed.

    ./build.sh      # dist/StsPvp/, then checks every Harmony patch against the installed game
    ./install.sh    # copies dist/StsPvp into <game>/mods/
    ./uninstall.sh

`STS2_DIR=/path/to/Slay the Spire 2` overrides the game folder.

Workshop update: set `changeNote` in `workshop/workshop.json`, then `./workshop.sh` (builds, copies
the mod into `workshop/content/` and runs Mega Crit's
[uploader](https://github.com/megacrit/sts2-mod-uploader); Steam must be running). The item id
is kept in `workshop/mod_id.txt`, so it updates the same Workshop item. On Windows, build with
`dotnet build StsPvp.csproj -c Release -o build -p:GameDir="C:\...\Slay the Spire 2"`.

## How it works

StS2 loads mods natively: `ModManager` reads `<game>/mods/**/<id>.json`, loads `<id>.dll` and
calls the class marked `[ModInitializer]`; Harmony ships with the game.

- `DuelMenu`: the Duel button (a copy of the Multiplayer button) and the host/join panel.
  Host: `NetHostGameService.StartENetHost(33771, 2)` + the co-op `NCharacterSelectScreen`.
  Join: `JoinFlow` with `ENetClientConnectionInitializer(ip, 33771)`. The game itself only uses
  this IP transport for local debugging (`fastmp`, hardcoded to 127.0.0.1).
- `PvpDraft`: the draft room, a per-player event like the game's own card-picking events.
  `CardSelectCmd.FromSimpleGrid` (10 cards) and `RelicSelectCmd.FromChooseARelicScreen`
  (5 × 1 of 3, offers shuffled with the event's per-player RNG) go through the game's
  `PlayerChoiceSynchronizer`, so both games apply the same picks. It shows the Battleworn
  Dummy portrait (`EventModel.InitialPortraitPath` patch), since the mod ships no art.
- `PvpDuel` / `PvpOpponent`: the duel encounter, one proxy monster per player, drawn with the
  player's character visuals (`creature_visuals/<character>`, as the game's own test monsters do).
- Patches (`Patches.cs`):
  - `RunManager.CreateRoom`: in duel mode the run's first room is the draft, the next one the duel.
  - `CreatureCmd.Damage` / `PowerCmd.Apply`: a proxy target is replaced by the player it stands
    for (and dropped when that is the attacker), before damage modifiers run. Co-op is
    lockstep, so both games resolve the same hit the same way.
  - `CombatManager.StartTurn`: alternating turns. Each player-side turn is given to one player
    (host on odd rounds) through the game's own "players taking the turn" list, the one
    extra-turn effects use; the other player is marked done and draws nothing.
  - `CreatureCmd.Kill`: one player dead decides the duel (not when the run is abandoned); a
    disconnect of the opponent mid-duel is a forfeit win; both games show the game over screen,
    `NGameOverScreen.InitializeBannerAndQuote` sets the Victory/Defeat banner.
  - `NCreatureStateDisplay.SetCreature`: the proxy shows its player's HP, block and powers.
  - `RunManager.SetUpNewMultiplayer`: duel runs are not saved.
  - `LocManager.SetLanguageInternal`: the mod's strings (no .pck) are merged into the tables.
- Scene-tree hooks (not `_Ready` patches): on your screen your own proxy and the opponent's
  co-op body are hidden and their proxy is mirrored to face you; the turn banner says
  "Your Turn" / "Opponent's Turn" instead of the extra-turn title.

## Findings

- REA 6.4.0 (`~/Projetos/rea`): `inspect-artifact` classifies `sts2.dll` as a PE and points to
  managed inspection; `inspect-managed-artifact` identifies modern .NET (net9.0) IL-only with
  9,409 types / 49,738 methods (`analysis/`). `inspect-managed-members` could not admit the
  metadata of this assembly ("managed member inspection is unavailable"), so the code was read
  by decompiling with ILSpy (`ilspycmd`), not committed.
- On Linux every Harmony patch fails with `mm-exhelper.so: undefined symbol: _Unwind_RaiseException`
  unless `libgcc_s.so.1` is loaded with `RTLD_GLOBAL` first (BaseLib does the same); the mod does
  this at startup. Godot callbacks (`_Ready`) are not patched at all.

## Verified / not verified

Verified on Linux, game v0.107.1:
- builds; `test/` applies every patch to the real `sts2.dll` (run by `build.sh`);
- in game: the mod loads, all patches apply, the **Duel** button shows in the main menu.

Not verified: an actual duel between two PCs (host/join, the proxy display, damage
redirection, the end screen), Windows, and play across Hamachi/ZeroTier.
