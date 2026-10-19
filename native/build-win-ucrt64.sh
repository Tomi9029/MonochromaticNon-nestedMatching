#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <nauty-dir>"
  echo "Example: $0 /c/Users/Tamas/Downloads/nauty2_9_3"
  exit 2
fi

# IMPORTANT:
# The user's Windows username may contain non-ASCII characters (e.g. Tamás).
# GCC/ld/assembler can choke on such absolute output paths under some MSYS2 setups.
# We therefore pass ONLY relative paths to GCC. The shell itself may still cd into
# the Unicode path; GCC arguments and output filenames remain ASCII/relative.

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NAUTY_DIR="$(cd "$1" && pwd)"
OUT_DIR="$SCRIPT_DIR/win-x64"
OBJ_DIR="$OUT_DIR/obj"
OUT_DLL="$OUT_DIR/orderednauty.dll"
BRIDGE="$SCRIPT_DIR/ordered_nauty_bridge.c"

SOURCES=(nauty.c nautil.c nausparse.c naugraph.c schreier.c naurng.c)

mkdir -p "$OBJ_DIR"
rm -f "$OUT_DLL" "$OUT_DIR"/*.log "$OBJ_DIR"/*.o

command -v gcc >/dev/null 2>&1 || { echo "gcc not found. Run this from MSYS2 UCRT64."; exit 3; }
TARGET="$(gcc -dumpmachine)"
if [[ "$TARGET" != x86_64-w64-mingw32* ]]; then
  echo "Expected x86_64-w64-mingw32 GCC, got: $TARGET"
  exit 4
fi

for f in nauty.h "${SOURCES[@]}"; do
  [[ -f "$NAUTY_DIR/$f" ]] || { echo "Missing nauty file: $NAUTY_DIR/$f"; exit 5; }
done
[[ -f "$BRIDGE" ]] || { echo "Missing bridge: $BRIDGE"; exit 6; }

# All GCC/LD paths below are relative to their current working directory.
OBJ_FROM_NAUTY="$(realpath --relative-to="$NAUTY_DIR" "$OBJ_DIR")"
NAUTY_FROM_PROJECT="$(realpath --relative-to="$PROJECT_ROOT" "$NAUTY_DIR")"

# Refuse a cross-drive setup because there is no portable relative path in that case.
if [[ "$OBJ_FROM_NAUTY" = /* || "$NAUTY_FROM_PROJECT" = /* ]]; then
  echo "Nauty and project must be on the same drive for this Unicode-safe build."
  echo "Project: $PROJECT_ROOT"
  echo "Nauty  : $NAUTY_DIR"
  exit 7
fi

echo "=== OrderedMatching / nauty Windows UCRT64 build v13 ==="
echo "Project : $PROJECT_ROOT"
echo "Nauty   : $NAUTY_DIR"
echo "GCC     : $(command -v gcc)"
echo "Version : $(gcc --version | head -n 1)"
echo "Target  : $TARGET"
echo "Unicode-safe GCC paths: relative"
echo

{
  echo "GCC: $(command -v gcc)"
  echo "Target: $TARGET"
  echo "NautyDir: $NAUTY_DIR"
  echo "Project: $PROJECT_ROOT"
  echo "OBJ_FROM_NAUTY: $OBJ_FROM_NAUTY"
  echo "NAUTY_FROM_PROJECT: $NAUTY_FROM_PROJECT"
} > "$OUT_DIR/build.log"

compile_nauty() {
  local src="$1"
  local base="${src%.c}"
  local log="$OUT_DIR/compile-$base.log"
  echo "--- compile-$base ---"
  echo "gcc -O2 -fno-strict-aliasing -I. -c $src -o $OBJ_FROM_NAUTY/$base.o" | tee "$log"
  (
    cd "$NAUTY_DIR"
    gcc -O2 -fno-strict-aliasing -I. -c "$src" -o "$OBJ_FROM_NAUTY/$base.o"
  ) 2>&1 | tee -a "$log"
}

for src in "${SOURCES[@]}"; do
  compile_nauty "$src"
done

echo "--- compile-bridge ---"
{
  echo "gcc -O2 -fno-strict-aliasing -I$NAUTY_FROM_PROJECT -c native/ordered_nauty_bridge.c -o native/win-x64/obj/ordered_nauty_bridge.o"
} | tee "$OUT_DIR/compile-bridge.log"
(
  cd "$PROJECT_ROOT"
  gcc -O2 -fno-strict-aliasing -I"$NAUTY_FROM_PROJECT" \
      -c native/ordered_nauty_bridge.c \
      -o native/win-x64/obj/ordered_nauty_bridge.o
) 2>&1 | tee -a "$OUT_DIR/compile-bridge.log"

echo "--- link ---"
{
  echo "gcc -shared native/win-x64/obj/ordered_nauty_bridge.o native/win-x64/obj/nauty.o native/win-x64/obj/nautil.o native/win-x64/obj/nausparse.o native/win-x64/obj/naugraph.o native/win-x64/obj/schreier.o native/win-x64/obj/naurng.o -static-libgcc -o native/win-x64/orderednauty.dll"
} | tee "$OUT_DIR/link.log"
(
  cd "$PROJECT_ROOT"
  gcc -shared \
      native/win-x64/obj/ordered_nauty_bridge.o \
      native/win-x64/obj/nauty.o \
      native/win-x64/obj/nautil.o \
      native/win-x64/obj/nausparse.o \
      native/win-x64/obj/naugraph.o \
      native/win-x64/obj/schreier.o \
      native/win-x64/obj/naurng.o \
      -static-libgcc \
      -o native/win-x64/orderednauty.dll
) 2>&1 | tee -a "$OUT_DIR/link.log"

[[ -f "$OUT_DLL" ]] || { echo "DLL was not created: $OUT_DLL"; exit 8; }

# Make the DLL visible to dotnet run / dotnet test output folders.
for cfg in Debug Release; do
  dest="$PROJECT_ROOT/bin/$cfg/net8.0"
  if [[ -d "$dest" ]]; then
    cp -f "$OUT_DLL" "$dest/orderednauty.dll"
    echo "Copied DLL to $dest/orderednauty.dll"
  fi
done

echo
echo "Build successful: $OUT_DLL"
ls -lh "$OUT_DLL"
