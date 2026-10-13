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
     and click **Subscribe**; Steam installs and updates it. The item is *friends only*: you
     must be Steam friends with the author to see it. Then skip to step 4.
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
4. The run opens straight into the duel. The enemy is the other player, drawn as their
   character, with their real HP, block and powers on it.
5. Turns are simultaneous like in co-op: play your cards, end turn; the next turn starts when
   both ended. Attacks, debuffs, area attacks and potions aimed at the opponent hit the other
   player for real (their block, Vulnerable, Thorns, etc. all apply).
6. When a player dies, both see the end screen: **Victory!** or **Defeat**. Main Menu to play again.

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
- `PvpDuel` / `PvpOpponent`: the duel encounter, one proxy monster per player, drawn with the
  player's character visuals (`creature_visuals/<character>`, as the game's own test monsters do).
- Patches (`Patches.cs`):
  - `RunManager.CreateRoom`: in duel mode the first room of the run is the duel.
  - `CreatureCmd.Damage` / `PowerCmd.Apply`: a proxy target is replaced by the player it stands
    for (and dropped when that is the attacker), before damage modifiers run. Co-op is
    lockstep, so both games resolve the same hit the same way.
  - `CreatureCmd.Kill`: one player dead decides the duel; both games show the game over screen,
    `NGameOverScreen.InitializeBannerAndQuote` sets the Victory/Defeat banner.
  - `NCreatureStateDisplay.SetCreature`: the proxy shows its player's HP, block and powers.
  - `RunManager.SetUpNewMultiplayer`: duel runs are not saved.
  - `LocManager.SetLanguageInternal`: the mod's strings (no .pck) are merged into the tables.
- Your own proxy is hidden on your screen (scene-tree hook, not a `_Ready` patch).

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
