#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

HPDOS_BACKEND_MODE=run \
HPDOS_DOTNET="$(command -v dotnet)" \
HPDOS_BACKEND_DIRECTORY="$(pwd)/../backend" \
HPDOS_WORKSPACE_ROOT="$(pwd)/../.." \
  ./node_modules/.bin/electrobun dev
