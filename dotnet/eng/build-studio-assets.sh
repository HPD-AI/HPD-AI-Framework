#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

for module in \
  typescript/hpd-ai-studio/modules/hpd-base-studio \
  typescript/hpd-ai-studio/modules/hpd-agent-studio; do
  npm --prefix "$repo_root/$module" ci
  npm --prefix "$repo_root/$module" run build
done
