#!/usr/bin/env bash
# Removes the mod from the game's mods folder.
set -euo pipefail
GAME_DIR="${STS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}"
rm -rf "$GAME_DIR/mods/StsPvp"
echo "removed $GAME_DIR/mods/StsPvp"
