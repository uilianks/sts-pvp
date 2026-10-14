# sts-pvp: Slay the Spire 2 Duel

A Slay the Spire 2 mod that adds a **Duel** button to the main menu: 2 to 4 players, each on their
own PC, fight each other, free-for-all: the last one standing wins. It reuses the game's own co-op: same lobby, same character select,
same turn sync. Connect through Steam (**Quick Match** against a random opponent, or a
**room code** for friends), or by IP over LAN, Hamachi or ZeroTier.

Built for game version **v0.107.1** (Steam, public branch).

## Setup (every player)

1. **Same game version** on every PC: Steam > Slay the Spire 2 > Properties > Betas: "None".
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
5. **Same mods on every PC.** Co-op checks that the games match, so any other mod has to be
   on every side too. If one of you has extra mods (e.g. from the Workshop), turn them off in
   *Mod Configuration* in the main menu.
6. **By IP only: same virtual network**: all in the same Hamachi network, or the same ZeroTier network
   (the ZeroTier admin must tick "Auth" for every PC in ZeroTier Central). Quick Match and room
   codes go through Steam and need none of this.

## Play

1. Connect, one of three ways (all from the **Duel** panel):
   - **Quick Match** (beta test, may not work properly): a random 1v1 opponent. It joins someone already waiting, or opens a
     lobby and waits for the next player who clicks Quick Match (the character select says
     "waiting for an opponent").
   - **Room code** (friends, 2 to 4 players): one clicks **Host**; the character select opens
     with a 5-letter room code on top, already copied to the clipboard. The others type it in
     the field and click **Join**.
   - **By IP** (LAN, Hamachi, ZeroTier): the host clicks **Host by IP**; the panel lists their
     IPs. The others type the host's IP in the field and click **Join**.
2. Finding the IP to share:
   - Hamachi: the IPv4 at the top of the Hamachi window (`25.x.y.z`).
   - ZeroTier: the "Managed IP" of the network (`zerotier-cli listnetworks` or ZeroTier Central).
   - Same LAN: the host's local IP (`192.168.x.y`).
3. Everyone picks a character and readies up; the host starts the run (with 2 or more players).
4. **Draft**: the run opens on the *Duel Draft* event. Click **Draft** and open **10 packs**
   of 5 random cards from your character's pool (Common, Uncommon, Rare), keeping one card
   from each, like a booster draft; then **5 relics**, one
   out of three each time. When everyone is done, click **Proceed** and pick the next room on
   the map: it is the duel.
5. The duel: you on the left, every other player on the right,
   facing you, with their real HP, block and powers.
6. Turns rotate in lobby order: the host plays first, ends turn, then the next player, and so
   on. While it is not your turn you have no cards and wait ("Opponent's Turn"). Block lasts
   through the others' turns and resets at the start of your own. Attacks, debuffs and potions
   aimed at an opponent hit that player for real (their block, Vulnerable, Thorns, etc. all
   apply); area attacks hit every opponent.
7. A player who dies is out: they vanish from the enemy side, their turns are skipped, and
   they watch the rest. When one player is left standing, everyone sees the end screen:
   **Victory!** for the winner, **Defeat** (2 players) or the place (*2nd Place*, *3rd Place*…).
   A player still fighting who quits forfeits: if that leaves one player, they win; with more
   left the duel ends with no result (*Duel Over*). Click **Continue**, then one of the two
   buttons above Main Menu: **Rematch** goes back to the character select; **Same Character**
   keeps your character (picked and readied for you) and goes straight to a new draft once
   everyone is ready. Each player chooses for themselves. The host hosts again and the others
   rejoin the way they came (same IP, or the host's Steam lobby); whoever clicks first waits
   for the host.

Duel runs are not saved and do not count towards stats or unlocks.

### Firewall (by IP only)

Quick Match and room codes use Steam's relay: no port to open. By IP, the host listens on **UDP 33771** (the game's own co-op port).
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

## Try it alone: several games on one PC

    ./simulate.sh             # 2 players
    PLAYERS=4 ./simulate.sh   # 3 or 4 players, windows tiled 2x2

Opens copies of the game side by side, already connected over 127.0.0.1 (the first hosts,
the others join). Pick a character and click ✓ in every window, start from the host, and play
every side of the duel. It runs without Steam, gives each copy a throwaway data folder seeded
from your settings and progress (your saves are not touched), and puts the built mod in `mods/`
only for the session. Logs: `/tmp/sts-pvp-sim/host.log`, `client.log` (then `client2.log`, `client3.log`).

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

- `SteamMatch`: Quick Match and room codes on the game's own Steam co-op transport
  (`NetHostGameService.StartSteamHost`, `SteamClientConnectionInitializer.FromLobby`: a Steam
  lobby plus Steam relay). The game creates friends-only lobbies; a duel lobby is made public
  and tagged with the game and mod builds, the kind (quick, room, rematch) and a key (room code,
  or the host's Steam id for a rematch), and found with `SteamMatchmaking.RequestLobbyList`
  filters. A `SteamHost.SetHostIsClosed` patch keeps it public while open. Two Quick Match
  players searching at once would both host: a waiting host searches again every 3 s and
  moves into an older (lower id) waiting lobby, so exactly one stays host.
- `DuelMenu`: the Duel button (a copy of the Multiplayer button) and the connect panel, built
  on the game's `NGenericPopup` / `NVerticalPopup` (its frame and Yes/No buttons, plus a copy
  for Host). Rematch returns to the main menu, then re-hosts or rejoins the last IP (retrying
  while the host is not up yet). The Rematch button is a copy of the end screen's
  `NReturnToMainMenuButton`, shown with it; the end screen also hides the creatures the duel
  hides (`MoveCreaturesToDifferentLayerAndDisableUi` would show them again).
  Host: `NetHostGameService.StartENetHost(33771, 4)` + the co-op `NCharacterSelectScreen`.
  Join: `JoinFlow` with `ENetClientConnectionInitializer(netId, ip, 33771)`, a random net id per joiner (the host is 1). The game itself only uses
  this IP transport for local debugging (`fastmp`, hardcoded to 127.0.0.1).
- `PvpDraft`: the draft room, a per-player event like the game's own card-picking events.
  `CardSelectCmd.FromSimpleGrid` (10 packs × 1 of 5, the picked card leaves the pool) and `RelicSelectCmd.FromChooseARelicScreen`
  (5 × 1 of 3; packs and offers shuffled with the event's per-player RNG) go through the game's
  `PlayerChoiceSynchronizer`, so every game applies the same picks. It shows the Battleworn
  Dummy portrait (`EventModel.InitialPortraitPath` patch), since the mod ships no art.
- `PvpDuel` / `PvpOpponent`: the duel encounter, one proxy monster per player, drawn with the
  player's character visuals (`creature_visuals/<character>`, as the game's own test monsters do).
- Patches (`Patches.cs`):
  - `RunManager.CreateRoom`: in duel mode the run's first room is the draft, the next one the duel.
  - `CreatureCmd.Damage` / `PowerCmd.Apply`: a proxy target is replaced by the player it stands
    for (and dropped when that is the attacker), before damage modifiers run. Co-op is
    lockstep, so every game resolves the same hit the same way.
  - `CombatManager.StartTurn`: turn rotation. Each player-side turn is given to the next living
    player in lobby order through the game's own "players taking the turn" list, the one
    extra-turn effects use; the others are marked done and draw nothing.
  - `CreatureCmd.Kill`: a dead player's proxy is killed too; one player left standing decides
    the duel (not when the run is abandoned) and every game shows the game over screen,
    `NGameOverScreen.InitializeBannerAndQuote` sets the Victory/Defeat/place banner. A player
    still fighting who leaves (`RunLobby.RemotePlayerDisconnected`, or the client's connection
    to the host) forfeits.
  - `NCreatureStateDisplay.SetCreature`: the proxy shows its player's HP, block and powers.
  - `RunManager.SetUpNewMultiplayer`: duel runs are not saved.
  - `LocManager.SetLanguageInternal`: the mod's strings (no .pck) are merged into the tables.
- Scene-tree hooks (not `_Ready` patches): on your screen your own proxy and the opponents'
  co-op bodies are hidden and their proxies are mirrored to face you; the turn banner says
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
- in testing mode (`simulate.sh`, 2 players over IP): the connect panel layout, draft, duel,
  damage numbers and hit effects shown at the opponent's proxy, both end screen rematch
  buttons, and a Same Character rematch (characters picked and readied, straight to a new draft).

Not verified: Quick Match, room codes and the Steam rematch (they need two Steam accounts on two
PCs: one account cannot join its own lobby, so `simulate.sh` cannot cover them); 3 and 4 player duels; an actual duel between two PCs (host/join, the proxy display, damage
redirection, the end screen), Windows, and play across Hamachi/ZeroTier.
