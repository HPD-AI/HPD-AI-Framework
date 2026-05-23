#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  check-real-acceptance-prereqs.sh [env-file]

Checks host tools and, when an env file is supplied, validates the required
HPD_APPLEVZ_* paths/values used by the real-container acceptance harness.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

env_file="${1:-}"

failures=0

check_tool() {
  local name="$1"
  if command -v "$name" >/dev/null 2>&1; then
    printf 'ok   tool %-12s %s\n' "$name" "$(command -v "$name")"
  else
    printf 'miss tool %-12s\n' "$name"
    failures=$((failures + 1))
  fi
}

check_optional_tool() {
  local name="$1"
  if command -v "$name" >/dev/null 2>&1; then
    printf 'ok   optional-tool %-12s %s\n' "$name" "$(command -v "$name")"
  else
    printf 'warn optional-tool %-12s not found\n' "$name"
  fi
}

check_file() {
  local name="$1"
  local path="${!name:-}"
  if [[ -z "$path" ]]; then
    printf 'miss env  %-45s empty\n' "$name"
    failures=$((failures + 1))
  elif [[ -f "$path" ]]; then
    printf 'ok   file %-45s %s\n' "$name" "$path"
  else
    printf 'miss file %-45s %s\n' "$name" "$path"
    failures=$((failures + 1))
  fi
}

check_executable() {
  local name="$1"
  local path="${!name:-}"
  if [[ -z "$path" ]]; then
    printf 'miss env  %-45s empty\n' "$name"
    failures=$((failures + 1))
  elif [[ -x "$path" && -f "$path" ]]; then
    printf 'ok   exec %-45s %s\n' "$name" "$path"
  else
    printf 'miss exec %-45s %s\n' "$name" "$path"
    failures=$((failures + 1))
  fi
}

check_nonempty() {
  local name="$1"
  local value="${!name:-}"
  if [[ -z "$value" ]]; then
    printf 'miss env  %-45s empty\n' "$name"
    failures=$((failures + 1))
  else
    printf 'ok   env  %-45s %s\n' "$name" "$value"
  fi
}

check_bool_optional() {
  local name="$1"
  local value="${!name:-}"
  if [[ -z "$value" || "$value" == "true" || "$value" == "false" ]]; then
    printf 'ok   env  %-45s %s\n' "$name" "${value:-<unset>}"
  else
    printf 'miss env  %-45s must be true or false when set\n' "$name"
    failures=$((failures + 1))
  fi
}

check_tool swift
check_tool curl
check_tool bsdtar
check_optional_tool hdiutil
check_optional_tool qemu-img
check_optional_tool zstd
check_optional_tool xz
check_optional_tool guestfish

if [[ -n "$env_file" ]]; then
  if [[ ! -f "$env_file" ]]; then
    printf 'miss env-file %s\n' "$env_file"
    exit 1
  fi

  set -a
  # shellcheck disable=SC1090
  . "$env_file"
  set +a

  check_nonempty HPD_APPLEVZ_REAL_CONTAINER_SMOKE
  if [[ "${HPD_APPLEVZ_REAL_CONTAINER_SMOKE:-}" != "1" ]]; then
    printf 'miss env  HPD_APPLEVZ_REAL_CONTAINER_SMOKE must be 1\n'
    failures=$((failures + 1))
  fi

  check_executable HPD_APPLEVZ_REAL_HELPER_PATH
  check_file HPD_APPLEVZ_GUEST_KERNEL
  check_file HPD_APPLEVZ_GUEST_INITRD
  check_file HPD_APPLEVZ_GUEST_DISK
  check_nonempty HPD_APPLEVZ_GUEST_SERIAL_LOG
  check_nonempty HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION
  check_nonempty HPD_APPLEVZ_CONTAINER_ENGINE_KIND
  check_nonempty HPD_APPLEVZ_CONTAINER_ENGINE_API
  check_nonempty HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE
  check_nonempty HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH
  check_nonempty HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE
  check_bool_optional HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED
  check_bool_optional HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL
  check_bool_optional HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT

  socket_locus="${HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS:-runtime-host}"
  if [[ "$socket_locus" == "host" || "$socket_locus" == "execution-unit" ]]; then
    printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS cannot be %s\n' "$socket_locus"
    failures=$((failures + 1))
  else
    printf 'ok   env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS     %s\n' "$socket_locus"
  fi

  socket_path="${HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH:-}"
  if [[ -n "$socket_path" && "$socket_path" != /* ]]; then
    printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be absolute\n'
    failures=$((failures + 1))
  fi

  engine_kind="${HPD_APPLEVZ_CONTAINER_ENGINE_KIND:-}"
  engine_api="${HPD_APPLEVZ_CONTAINER_ENGINE_API:-}"
  authority_mode="${HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE:-}"
  if [[ "$engine_kind" == "Containerd" ]]; then
    if [[ "$engine_api" != "ContainerdApi" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_API must be ContainerdApi for Containerd\n'
      failures=$((failures + 1))
    fi
    if [[ "$authority_mode" != "Rootful" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE must be Rootful for Containerd\n'
      failures=$((failures + 1))
    fi
    if [[ "$socket_path" != "/run/containerd/containerd.sock" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be /run/containerd/containerd.sock for Containerd\n'
      failures=$((failures + 1))
    fi
  elif [[ "$engine_kind" == "Podman" ]]; then
    if [[ "$engine_api" != "PodmanApi" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_API must be PodmanApi for Podman\n'
      failures=$((failures + 1))
    fi
    if [[ "$authority_mode" == "Rootless" && "$socket_path" != "/run/user/1000/podman/podman.sock" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be /run/user/1000/podman/podman.sock for rootless Podman\n'
      failures=$((failures + 1))
    elif [[ "$authority_mode" == "Rootful" && "$socket_path" != "/run/podman/podman.sock" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be /run/podman/podman.sock for rootful Podman\n'
      failures=$((failures + 1))
    fi
  elif [[ "$engine_kind" == "BuildKit" ]]; then
    if [[ "$engine_api" != "BuildKitApi" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_API must be BuildKitApi for BuildKit\n'
      failures=$((failures + 1))
    fi
    if [[ "$authority_mode" == "Rootless" && "$socket_path" != "/run/user/1000/buildkit-default/buildkitd.sock" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be /run/user/1000/buildkit-default/buildkitd.sock for rootless BuildKit\n'
      failures=$((failures + 1))
    elif [[ "$authority_mode" == "Rootful" && "$socket_path" != "/run/buildkit/buildkitd.sock" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be /run/buildkit/buildkitd.sock for rootful BuildKit\n'
      failures=$((failures + 1))
    fi
  elif [[ "$engine_kind" == "DockerCompatible" ]]; then
    if [[ "$engine_api" != "DockerCompatible" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_API must be DockerCompatible for DockerCompatible\n'
      failures=$((failures + 1))
    fi
    if [[ "$authority_mode" == "Rootless" && "$socket_path" != "/run/user/1000/docker.sock" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be /run/user/1000/docker.sock for rootless DockerCompatible\n'
      failures=$((failures + 1))
    elif [[ "$authority_mode" == "Rootful" && "$socket_path" != "/var/run/docker.sock" ]]; then
      printf 'miss env  HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH must be /var/run/docker.sock for rootful DockerCompatible\n'
      failures=$((failures + 1))
    fi
  fi
fi

if [[ "$failures" -ne 0 ]]; then
  printf '\nfailed prerequisite checks: %d\n' "$failures"
  exit 1
fi

printf '\nreal acceptance prerequisite check passed\n'
