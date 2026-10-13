#!/usr/bin/env bash
# Builds the mod and uploads it to the Steam Workshop (item id in workshop/mod_id.txt, settings in
# workshop/workshop.json: set "changeNote" first). Needs Steam running and Mega Crit's uploader:
#   git clone https://github.com/megacrit/sts2-mod-uploader ~/Projetos/sts2-mod-uploader
#   cd ~/Projetos/sts2-mod-uploader && dotnet publish -c Release -r linux-x64 -o out
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
UPLOADER="${STS2_UPLOADER:-$HOME/Projetos/sts2-mod-uploader/out}"
"$HERE/build.sh"
rm -rf "$HERE/workshop/content"
mkdir -p "$HERE/workshop/content"
cp "$HERE/dist/StsPvp/"* "$HERE/workshop/content/"
cd "$UPLOADER"   # the uploader reads steam_appid.txt from its own folder
./ModUploader upload -w "$HERE/workshop"
