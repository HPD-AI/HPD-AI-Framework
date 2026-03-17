#!/usr/bin/env bash
# dev.sh — Start HPDOS in HMR dev mode
#
# Writes the Vite dev server URL to ~/.hpdos/dev-server-url so the MAUI GUI
# app can pick it up (macOS GUI apps don't inherit shell env vars).
#
# Usage:
#   ./scripts/dev.sh           # start vite dev + write URL file
#   ./scripts/dev.sh --clean   # remove the URL file and exit (stop dev mode)

set -euo pipefail

DEV_URL_FILE="$HOME/.hpdos/dev-server-url"
DEV_URL="http://localhost:5174"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${1:-}" == "--clean" ]]; then
  rm -f "$DEV_URL_FILE"
  echo "Dev mode disabled — removed $DEV_URL_FILE"
  exit 0
fi

# Write dev server URL for the MAUI GUI to discover
mkdir -p "$(dirname "$DEV_URL_FILE")"
echo "$DEV_URL" > "$DEV_URL_FILE"
echo "Dev mode enabled — wrote $DEV_URL_FILE"
echo "MAUI will proxy all requests to $DEV_URL"
echo ""

BACKEND_PID=""

cleanup() {
  echo ""
  echo "Stopping dev server..."
  rm -f "$DEV_URL_FILE"
  [[ -n "$BACKEND_PID" ]] && kill "$BACKEND_PID" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

# Start backend (Kestrel only, no browser) with CORS enabled for dev
HPDOS_DEV=1 dotnet run --project "$SCRIPT_DIR/../src-dotnet/HPDOS.CLI" -- backend &
BACKEND_PID=$!

# Wait for Kestrel to be ready — reads actual port from port file
PORT_FILE="$HOME/Library/Application Support/hpdos/port"
BACKEND_PORT=5173
echo "Waiting for backend..."
for i in $(seq 1 30); do
  if [[ -f "$PORT_FILE" ]]; then
    BACKEND_PORT=$(cat "$PORT_FILE")
  fi
  curl -sf "http://localhost:$BACKEND_PORT/api/status" > /dev/null 2>&1 && break
  sleep 1
done
echo "Backend ready on :$BACKEND_PORT"

cd "$SCRIPT_DIR/.."
( sleep 2 && open "$DEV_URL" ) &
# Pass backend port so vite.config.ts can proxy /api to the right port
HPDOS_BACKEND_PORT="$BACKEND_PORT" ./node_modules/.bin/vite
