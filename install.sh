#!/usr/bin/env bash
# Copies the mod into the game's mods folder (<game>/mods/StsPvp). uninstall.sh removes it.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="${STS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}"
[ -f "$HERE/dist/StsPvp/StsPvp.dll" ] || "$HERE/build.sh"
mkdir -p "$GAME_DIR/mods/StsPvp"
cp "$HERE/dist/StsPvp/"* "$GAME_DIR/mods/StsPvp/"
echo "installed into $GAME_DIR/mods/StsPvp"
