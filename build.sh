#!/usr/bin/env bash
# Builds dist/StsPvp/ (StsPvp.dll + StsPvp.json) against the installed game, then checks every
# Harmony patch applies to this game version. Needs a .NET 9 SDK (dotnet).
set -euo pipefail
cd "$(dirname "$0")"
GAME_DIR="${STS2_DIR:-$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2}"
DATA_DIR="$(ls -d "$GAME_DIR"/data_sts2_* | head -1)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

rm -rf build dist
dotnet build StsPvp.csproj -c Release -o build -p:GameDir="$GAME_DIR" -nologo -v q
dotnet run --project test -c Release -p:GameDir="$GAME_DIR" -p:GameData="$DATA_DIR" -- "$DATA_DIR" build/StsPvp.dll

mkdir -p dist/StsPvp
cp build/StsPvp.dll StsPvp.json dist/StsPvp/
echo "built dist/StsPvp"
