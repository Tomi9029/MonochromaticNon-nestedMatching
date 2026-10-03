#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NAUTY_DIR="${1:-${NAUTY_DIR:-}}"

if [[ -z "$NAUTY_DIR" ]]; then
  echo "Használat: $0 /path/to/nauty2_8_9" >&2
  exit 2
fi
NAUTY_DIR="$(cd "$NAUTY_DIR" && pwd)"

SOURCES=(
  nauty.c nautil.c nausparse.c naugraph.c schreier.c naurng.c
  traces.c gtools.c naututil.c nautinv.c gutil1.c gutil2.c gtnauty.c
  naugroup.c nautycliquer.c nauchromatic.c nauconnect.c
)
for src in "${SOURCES[@]}"; do
  [[ -f "$NAUTY_DIR/$src" ]] || { echo "Hiányzó nauty forrás: $NAUTY_DIR/$src" >&2; exit 3; }
done

OUT="$SCRIPT_DIR/linux-x64/liborderednauty.so"
echo "Build: $OUT"

gcc -std=c11 -O3 -fPIC -shared \
  -I"$NAUTY_DIR" \
  "$SCRIPT_DIR/ordered_nauty_bridge.c" \
  "${SOURCES[@]/#/$NAUTY_DIR/}" \
  -lm -o "$OUT"

echo "Kész: $OUT"
