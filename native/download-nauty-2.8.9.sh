#!/usr/bin/env bash
set -euo pipefail

OUT_DIR="${1:-$PWD/third_party}"
mkdir -p "$OUT_DIR"
cd "$OUT_DIR"

URL="https://pallini.di.uniroma1.it/nauty2_8_9.tar.gz"
ARCHIVE="nauty2_8_9.tar.gz"
DIR="nauty2_8_9"

if [[ ! -f "$ARCHIVE" ]]; then
  echo "Letöltés: $URL"
  curl -fL "$URL" -o "$ARCHIVE"
fi

if [[ ! -d "$DIR" ]]; then
  tar xzf "$ARCHIVE"
fi

echo "nauty forrás: $(cd "$DIR" && pwd)"
