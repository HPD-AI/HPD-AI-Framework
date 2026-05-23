#!/usr/bin/env bash
set -euo pipefail

package_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
entitlements="$package_root/packaging/hpd-vz.entitlements"
configuration="debug"
identity="-"
helper_path=""
skip_build="false"

usage() {
  cat <<'USAGE'
Usage:
  sign-hpd-vz.sh [options]

Builds and signs the hpd-vz helper with the Apple virtualization entitlement.

Options:
  --configuration debug|release   default: debug
  --identity IDENTITY             default: - (ad-hoc signing)
  --helper PATH                   sign this helper path instead of the package build output
  --skip-build                    do not run swift build before signing
  -h, --help

Notes:
  Ad-hoc signing is enough to make the entitlement inspectable for local
  helper preflight, but release packaging should use a real Developer ID or
  app-bundle signing identity appropriate for distribution.
USAGE
}

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --configuration) configuration="$2"; shift 2 ;;
    --identity) identity="$2"; shift 2 ;;
    --helper) helper_path="$2"; shift 2 ;;
    --skip-build) skip_build="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'unknown argument: %s\n\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$configuration" in
  debug|release) ;;
  *) printf '--configuration must be debug or release: %s\n' "$configuration" >&2; exit 2 ;;
esac

if [[ "$skip_build" != "true" && -z "$helper_path" ]]; then
  if [[ "$configuration" == "release" ]]; then
    swift build --package-path "$package_root" -c release
  else
    swift build --package-path "$package_root"
  fi
fi

if [[ -z "$helper_path" ]]; then
  if [[ "$configuration" == "release" ]]; then
    helper_path="$package_root/.build/release/hpd-vz"
  else
    machine="$(uname -m)"
    host_triple_dir="$package_root/.build/${machine}-apple-macosx/debug/hpd-vz"
    generic_dir="$package_root/.build/debug/hpd-vz"
    if [[ -x "$host_triple_dir" ]]; then
      helper_path="$host_triple_dir"
    else
      helper_path="$generic_dir"
    fi
  fi
fi

if [[ ! -f "$helper_path" ]]; then
  printf 'hpd-vz helper not found: %s\n' "$helper_path" >&2
  exit 1
fi

codesign --force --sign "$identity" --entitlements "$entitlements" "$helper_path"
codesign --verify --verbose "$helper_path"

printf 'signed helper: %s\n' "$helper_path"
printf 'entitlements:\n'
codesign -d --entitlements :- "$helper_path"
