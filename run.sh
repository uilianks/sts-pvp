#!/usr/bin/env bash
# Launches the game with the PvP mod, without modifying the install.
#   ./run.sh host [port]          wait for the opponent
#   ./run.sh join <ip> [port]     connect to the host (its Hamachi IPv4)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="${STS_DIR:-$HOME/.local/share/Steam/steamapps/common/SlayTheSpire}"

case "${1:-}" in
    host) MODE=(-Dpvp.host=true); PORT="${2:-24680}" ;;
    join) [ -n "${2:-}" ] || { echo "usage: $0 join <ip> [port]" >&2; exit 2; }
          MODE=(-Dpvp.join="$2"); PORT="${3:-24680}" ;;
    *) echo "usage: $0 host [port] | join <ip> [port]" >&2; exit 2 ;;
esac

[ -f "$HERE/dist/sts-pvp.jar" ] || "$HERE/build.sh"
cd "$GAME_DIR"
exec ./jre/bin/java -Xmx1G -Dsun.java2d.dpiaware=true "${MODE[@]}" -Dpvp.port="$PORT" \
    -cp "$HERE/dist/sts-pvp.jar:desktop-1.0.jar" \
    com.megacrit.cardcrawl.desktop.DesktopLauncher
