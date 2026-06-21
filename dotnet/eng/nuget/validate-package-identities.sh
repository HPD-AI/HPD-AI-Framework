#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -eq 0 ]]; then
  echo "Usage: validate-package-identities.sh <project.csproj>..." >&2
  exit 2
fi

package_id_for_project() {
  local project="$1"
  local package_id
  package_id="$(perl -ne 'if (m#<PackageId>([^<]+)</PackageId>#) { print $1; exit }' "$project")"

  if [[ -n "$package_id" ]]; then
    echo "$package_id"
  else
    basename "$project" .csproj
  fi
}

validate_project() {
  local project="$1"
  local package_id="$2"

  case "$project" in
    */HPD-Agent.Framework/src/*)
      if [[ "$package_id" != HPD-Agent.* && "$package_id" != HPD-Agent.Framework ]]; then
        echo "Package identity error: agent project must use HPD-Agent.* prefix: $package_id ($project)" >&2
        return 1
      fi
      ;;
    */shared/src/Rhodium/src/*)
      if [[ "$package_id" != Rhodium.* ]]; then
        echo "Package identity error: Rhodium project must use Rhodium.* prefix: $package_id ($project)" >&2
        return 1
      fi
      ;;
    */HPD-Graph.Framework/src/*)
      if [[ "$package_id" != HPD-Graph.* ]]; then
        echo "Package identity error: graph project must use HPD-Graph.* prefix: $package_id ($project)" >&2
        return 1
      fi
      ;;
    */HPD-Auth.Framework/src/*)
      if [[ "$package_id" != HPD-Auth && "$package_id" != HPD-Auth-* ]]; then
        echo "Package identity error: auth project must use HPD-Auth prefix: $package_id ($project)" >&2
        return 1
      fi
      ;;
    */HPD-ML.Framework/src/*)
      if [[ "$package_id" != HPD-ML && "$package_id" != HPD-ML-* ]]; then
        echo "Package identity error: ML project must use HPD-ML prefix: $package_id ($project)" >&2
        return 1
      fi
      ;;
    */HPD-RAG.Framework/src/*)
      if [[ "$package_id" != HPD-RAG.* ]]; then
        echo "Package identity error: RAG project must use HPD-RAG.* prefix: $package_id ($project)" >&2
        return 1
      fi
      ;;
    */shared/src/HPD-*/* | */shared/src/HPD-RealtimeMedia/src/*/*)
      if [[ "$package_id" != HPD-* || "$package_id" == HPD-Agent.* ]]; then
        echo "Package identity error: shared HPD project must use HPD-* prefix, not HPD-Agent.*: $package_id ($project)" >&2
        return 1
      fi
      ;;
    */HPD-TUI.Framework/src/*)
      if [[ "$package_id" != HPD-* || "$package_id" == HPD-Agent.* ]]; then
        echo "Package identity error: standalone HPD project must use HPD-* prefix: $package_id ($project)" >&2
        return 1
      fi
      ;;
  esac
}

for project in "$@"; do
  if [[ ! -f "$project" ]]; then
    echo "Package identity error: project not found: $project" >&2
    exit 1
  fi

  package_id="$(package_id_for_project "$project")"
  validate_project "$project" "$package_id"
done

echo "Package identity validation passed."
