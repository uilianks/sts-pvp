#!/usr/bin/env bash
# Builds dist/sts-pvp.jar from the installed game jar. Does not modify the game.
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="${STS_DIR:-$HOME/.local/share/Steam/steamapps/common/SlayTheSpire}"
GAME_JAR="$GAME_DIR/desktop-1.0.jar"
TARGET=com/megacrit/cardcrawl/helpers/MonsterHelper.class
ASM_EXPORT=java.base/jdk.internal.org.objectweb.asm=ALL-UNNAMED

rm -rf build dist
mkdir -p build/orig build/patch build/tools dist

unzip -q -o "$GAME_JAR" "$TARGET" -d build/orig

# Mod classes, Java 8 bytecode to match the game's bundled JRE.
javac -nowarn -Xlint:-options --release 8 -cp "$GAME_JAR" -d build/patch src/pvp/*.java

javac -nowarn --add-exports "$ASM_EXPORT" -d build/tools tools/PatchGetEncounter.java
java --add-exports "$ASM_EXPORT" -cp build/tools PatchGetEncounter "build/orig/$TARGET" "build/patch/$TARGET"

jar --create --file dist/sts-pvp.jar -C build/patch .
cp run.bat dist/
echo "built dist/sts-pvp.jar"
