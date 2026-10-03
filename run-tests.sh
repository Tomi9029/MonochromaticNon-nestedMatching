#!/usr/bin/env bash
set -euo pipefail

SELF_TEST_ARGS=(--self-test)
if [[ -f "$(dirname "$0")/native/linux-x64/liborderednauty.so" ]]; then
  SELF_TEST_ARGS+=(--require-nauty)
fi

dotnet run -- "${SELF_TEST_ARGS[@]}"
