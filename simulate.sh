#!/usr/bin/env bash
# Opens two copies of the game on this PC (PLAYERS=3 or 4 for more), already connected for a
# duel over 127.0.0.1: the top-left window hosts, the others join. Pick a character in each
# window, ready up, start from the host, and play every side.
# MENU=1 opens every window at the main menu instead, to connect by hand from the Duel panel.
#
# - Runs without Steam (--force-steam=off): no Workshop mods, no Steam conflict between the copies.
# - Each copy gets its own throwaway data folder (XDG_DATA_HOME) seeded with a copy of your
#   settings and progress, so your real saves are never touched.
# - The built mod is put in <game>/mods/StsPvp for the session and removed on exit (Ctrl+C or
#   closing both windows). If you had a local copy there, it is restored.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="${STS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}"
REAL_DATA="${XDG_DATA_HOME:-$HOME/.local/share}/SlayTheSpire2"
SIM="${TMPDIR:-/tmp}/sts-pvp-sim"
PLAYERS="${PLAYERS:-2}"
MENU="${MENU:-0}"
case "$PLAYERS" in 2|3|4) ;; *) echo "PLAYERS must be 2, 3 or 4" >&2; exit 1 ;; esac
MOD_DIR="$GAME_DIR/mods/StsPvp"

[ -f "$HERE/dist/StsPvp/StsPvp.dll" ] || "$HERE/build.sh"
if pgrep -x SlayTheSpire2 >/dev/null; then
    echo "Close Slay the Spire 2 first." >&2
    exit 1
fi

# Temporary mod install (restore whatever was there before).
BACKUP=""
if [ -d "$MOD_DIR" ]; then
    BACKUP="$SIM/mod-backup"
    rm -rf "$BACKUP"; mkdir -p "$SIM"; cp -r "$MOD_DIR" "$BACKUP"
fi
PIDS=()
cleanup() {
    for pid in "${PIDS[@]}"; do kill "$pid" 2>/dev/null || true; done
    rm -rf "$MOD_DIR"
    [ -n "$BACKUP" ] && cp -r "$BACKUP" "$MOD_DIR"
    echo "simulation ended, mods folder restored"
}
trap cleanup EXIT INT TERM
mkdir -p "$MOD_DIR"
cp "$HERE/dist/StsPvp/"* "$MOD_DIR/"

# Sandbox user data: without Steam the game uses user://default/1/.
seed() { # seed <name> <x> <y>
    local data="$SIM/$1/SlayTheSpire2"
    rm -rf "$SIM/$1"
    mkdir -p "$data/default/1"
    local profile
    profile="$(ls -d "$REAL_DATA"/steam/*/ 2>/dev/null | head -1 || true)"
    if [ -n "$profile" ]; then
        cp -r "$profile"/. "$data/default/1/"
        find "$data/default/1" -name 'current_run*' -delete
    fi
    python3 - "$data/default/1/settings.save" "$2" "$3" <<'PY'
import json, os, sys
path, x, y = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
s = json.load(open(path)) if os.path.exists(path) else {}
s.update(fullscreen=False, window_position={"X": x, "Y": y}, window_size={"X": 1260, "Y": 709},
         seen_ea_disclaimer=True, skip_intro_logo=True)
mods = s.setdefault("mod_settings", {})
mods["mods_enabled"] = True  # consent for this sandbox only
json.dump(s, open(path, "w"), indent=2)
PY
    echo "$SIM/$1"
}

run() { # run <name> <x> <y> <args...>
    local name=$1 x=$2 y=$3; shift 3
    local home
    home="$(seed "$name" "$x" "$y")"
    (cd "$GAME_DIR" && XDG_DATA_HOME="$home" ./SlayTheSpire2 --force-steam=off "$@" \
        > "$SIM/$name.log" 2>&1) &
    PIDS+=($!)
}

if [ "$MENU" = 1 ]; then
    HOST_ARGS=(); JOIN_ARGS=()
else
    HOST_ARGS=(--pvp-host); JOIN_ARGS=(--pvp-join=127.0.0.1)
fi

run host 0 60 "${HOST_ARGS[@]}"
echo "host starting (log: $SIM/host.log)"
for _ in $(seq 1 90); do
    [ "$MENU" = 1 ] && break
    grep -q "\[pvp\] hosting duel" "$SIM/host.log" 2>/dev/null && break
    if grep -q "\[pvp\] patch .* failed\|Could not host" "$SIM/host.log" 2>/dev/null; then
        echo "host failed, see $SIM/host.log" >&2; exit 1
    fi
    sleep 1
done
[ "$MENU" = 1 ] || grep -q "\[pvp\] hosting duel" "$SIM/host.log" || { echo "host did not start, see $SIM/host.log" >&2; exit 1; }

# The first joiner keeps the name "client" (and client.log); more are client2, client3.
SPOTS=("1290 60" "0 760" "1290 760")
for i in $(seq 1 $((PLAYERS - 1))); do
    name=client; [ "$i" -gt 1 ] && name="client$i"
    # shellcheck disable=SC2086
    run "$name" ${SPOTS[$((i - 1))]} "${JOIN_ARGS[@]}"
    echo "$name joining (log: $SIM/$name.log)"
    sleep 3
done
echo "All $PLAYERS windows are up. Close them (or Ctrl+C here) to end the simulation."
wait
