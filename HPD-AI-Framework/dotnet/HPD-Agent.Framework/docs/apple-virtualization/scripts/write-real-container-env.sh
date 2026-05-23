#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../../../.." && pwd)"
helper_package="$repo_root/HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/hpd-vz"
default_helper="$helper_package/.build/arm64-apple-macosx/debug/hpd-vz"

output="/tmp/hpd-applevz-real.env"
helper="$default_helper"
kernel=""
initrd=""
disk=""
serial_log="/tmp/hpd-applevz-serial.log"
guest_agent_version=""
engine_kind="DockerCompatible"
engine_api="DockerCompatible"
authority_mode="Rootless"
socket_locus="runtime-host"
engine_socket="/run/user/1000/docker.sock"
smoke_image="alpine:3.20"
bundle_root=""
kernel_cmdline=""
virtiofs_host_path=""
virtiofs_tag=""
provisioning_enabled="false"
allow_package_install="false"
allow_service_enablement="false"
build_helper="true"

usage() {
  cat <<'USAGE'
Usage:
  write-real-container-env.sh --kernel PATH --initrd PATH --disk PATH --guest-agent-version VERSION [options]

Required:
  --kernel PATH
  --initrd PATH
  --disk PATH
  --guest-agent-version VERSION

Options:
  --output PATH                         default: /tmp/hpd-applevz-real.env
  --helper PATH                         default: repo hpd-vz debug build
  --no-build-helper
  --serial-log PATH                     default: /tmp/hpd-applevz-serial.log
  --engine-kind VALUE                   default: DockerCompatible
  --engine-api VALUE                    default: DockerCompatible
  --authority-mode VALUE                default: Rootless
  --socket-locus VALUE                  default: runtime-host
  --engine-socket PATH                  default: /run/user/1000/docker.sock
  --smoke-image IMAGE                   default: alpine:3.20
  --bundle-root PATH
  --kernel-cmdline TEXT
  --virtiofs-host-path PATH
  --virtiofs-tag TAG
  --provisioning-enabled true|false     default: false
  --allow-package-install true|false    default: false
  --allow-service-enablement true|false default: false
USAGE
}

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --output) output="$2"; shift 2 ;;
    --helper) helper="$2"; shift 2 ;;
    --no-build-helper) build_helper="false"; shift ;;
    --kernel) kernel="$2"; shift 2 ;;
    --initrd) initrd="$2"; shift 2 ;;
    --disk) disk="$2"; shift 2 ;;
    --serial-log) serial_log="$2"; shift 2 ;;
    --guest-agent-version) guest_agent_version="$2"; shift 2 ;;
    --engine-kind) engine_kind="$2"; shift 2 ;;
    --engine-api) engine_api="$2"; shift 2 ;;
    --authority-mode) authority_mode="$2"; shift 2 ;;
    --socket-locus) socket_locus="$2"; shift 2 ;;
    --engine-socket) engine_socket="$2"; shift 2 ;;
    --smoke-image) smoke_image="$2"; shift 2 ;;
    --bundle-root) bundle_root="$2"; shift 2 ;;
    --kernel-cmdline) kernel_cmdline="$2"; shift 2 ;;
    --virtiofs-host-path) virtiofs_host_path="$2"; shift 2 ;;
    --virtiofs-tag) virtiofs_tag="$2"; shift 2 ;;
    --provisioning-enabled) provisioning_enabled="$2"; shift 2 ;;
    --allow-package-install) allow_package_install="$2"; shift 2 ;;
    --allow-service-enablement) allow_service_enablement="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'unknown argument: %s\n\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

require_file() {
  local label="$1"
  local path="$2"
  if [[ -z "$path" || ! -f "$path" ]]; then
    printf '%s must point to an existing file: %s\n' "$label" "${path:-<empty>}" >&2
    exit 1
  fi
}

require_nonempty() {
  local label="$1"
  local value="$2"
  if [[ -z "$value" ]]; then
    printf '%s is required\n' "$label" >&2
    exit 1
  fi
}

require_bool() {
  local label="$1"
  local value="$2"
  if [[ "$value" != "true" && "$value" != "false" ]]; then
    printf '%s must be true or false: %s\n' "$label" "$value" >&2
    exit 1
  fi
}

shell_escape() {
  printf '%q' "$1"
}

if [[ "$build_helper" == "true" && "$helper" == "$default_helper" ]]; then
  swift build --package-path "$helper_package" >/dev/null
fi

require_file "--helper" "$helper"
if [[ ! -x "$helper" ]]; then
  printf '--helper must be executable: %s\n' "$helper" >&2
  exit 1
fi

require_file "--kernel" "$kernel"
require_file "--initrd" "$initrd"
require_file "--disk" "$disk"
require_nonempty "--guest-agent-version" "$guest_agent_version"
require_nonempty "--engine-socket" "$engine_socket"
require_nonempty "--smoke-image" "$smoke_image"
require_bool "--provisioning-enabled" "$provisioning_enabled"
require_bool "--allow-package-install" "$allow_package_install"
require_bool "--allow-service-enablement" "$allow_service_enablement"

if [[ "$engine_socket" != /* ]]; then
  printf '--engine-socket must be an absolute guest-visible path: %s\n' "$engine_socket" >&2
  exit 1
fi

if [[ "$socket_locus" == "host" || "$socket_locus" == "execution-unit" ]]; then
  printf '--socket-locus cannot be %s for real acceptance\n' "$socket_locus" >&2
  exit 1
fi

if [[ "$engine_kind" == "Containerd" ]]; then
  if [[ "$engine_api" != "ContainerdApi" ]]; then
    printf 'Containerd real acceptance requires --engine-api ContainerdApi\n' >&2
    exit 1
  fi
  if [[ "$authority_mode" != "Rootful" ]]; then
    printf 'Containerd real acceptance requires --authority-mode Rootful\n' >&2
    exit 1
  fi
  if [[ "$engine_socket" != "/run/containerd/containerd.sock" ]]; then
    printf 'Containerd real acceptance requires --engine-socket /run/containerd/containerd.sock\n' >&2
    exit 1
  fi
fi

if [[ "$engine_kind" == "Podman" ]]; then
  if [[ "$engine_api" != "PodmanApi" ]]; then
    printf 'Podman real acceptance requires --engine-api PodmanApi\n' >&2
    exit 1
  fi
  if [[ "$authority_mode" == "Rootless" && "$engine_socket" != "/run/user/1000/podman/podman.sock" ]]; then
    printf 'Rootless Podman real acceptance requires --engine-socket /run/user/1000/podman/podman.sock\n' >&2
    exit 1
  fi
  if [[ "$authority_mode" == "Rootful" && "$engine_socket" != "/run/podman/podman.sock" ]]; then
    printf 'Rootful Podman real acceptance requires --engine-socket /run/podman/podman.sock\n' >&2
    exit 1
  fi
fi

if [[ "$engine_kind" == "BuildKit" ]]; then
  if [[ "$engine_api" != "BuildKitApi" ]]; then
    printf 'BuildKit real acceptance requires --engine-api BuildKitApi\n' >&2
    exit 1
  fi
  if [[ "$authority_mode" == "Rootless" && "$engine_socket" != "/run/user/1000/buildkit-default/buildkitd.sock" ]]; then
    printf 'Rootless BuildKit real acceptance requires --engine-socket /run/user/1000/buildkit-default/buildkitd.sock\n' >&2
    exit 1
  fi
  if [[ "$authority_mode" == "Rootful" && "$engine_socket" != "/run/buildkit/buildkitd.sock" ]]; then
    printf 'Rootful BuildKit real acceptance requires --engine-socket /run/buildkit/buildkitd.sock\n' >&2
    exit 1
  fi
fi

if [[ "$engine_kind" == "DockerCompatible" ]]; then
  if [[ "$engine_api" != "DockerCompatible" ]]; then
    printf 'Docker-compatible real acceptance requires --engine-api DockerCompatible\n' >&2
    exit 1
  fi
  if [[ "$authority_mode" == "Rootless" && "$engine_socket" != "/run/user/1000/docker.sock" ]]; then
    printf 'Rootless Docker-compatible real acceptance requires --engine-socket /run/user/1000/docker.sock\n' >&2
    exit 1
  fi
  if [[ "$authority_mode" == "Rootful" && "$engine_socket" != "/var/run/docker.sock" ]]; then
    printf 'Rootful Docker-compatible real acceptance requires --engine-socket /var/run/docker.sock\n' >&2
    exit 1
  fi
fi

mkdir -p "$(dirname "$output")"
mkdir -p "$(dirname "$serial_log")"

{
  printf 'export HPD_APPLEVZ_REAL_CONTAINER_SMOKE=1\n'
  printf 'export HPD_APPLEVZ_REAL_HELPER_PATH=%s\n' "$(shell_escape "$helper")"
  printf 'export HPD_APPLEVZ_GUEST_KERNEL=%s\n' "$(shell_escape "$kernel")"
  printf 'export HPD_APPLEVZ_GUEST_INITRD=%s\n' "$(shell_escape "$initrd")"
  printf 'export HPD_APPLEVZ_GUEST_DISK=%s\n' "$(shell_escape "$disk")"
  printf 'export HPD_APPLEVZ_GUEST_SERIAL_LOG=%s\n' "$(shell_escape "$serial_log")"
  printf 'export HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION=%s\n' "$(shell_escape "$guest_agent_version")"
  printf 'export HPD_APPLEVZ_CONTAINER_ENGINE_KIND=%s\n' "$(shell_escape "$engine_kind")"
  printf 'export HPD_APPLEVZ_CONTAINER_ENGINE_API=%s\n' "$(shell_escape "$engine_api")"
  printf 'export HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE=%s\n' "$(shell_escape "$authority_mode")"
  printf 'export HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS=%s\n' "$(shell_escape "$socket_locus")"
  printf 'export HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH=%s\n' "$(shell_escape "$engine_socket")"
  printf 'export HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE=%s\n' "$(shell_escape "$smoke_image")"
  printf 'export HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED=%s\n' "$(shell_escape "$provisioning_enabled")"
  printf 'export HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL=%s\n' "$(shell_escape "$allow_package_install")"
  printf 'export HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT=%s\n' "$(shell_escape "$allow_service_enablement")"
  [[ -n "$bundle_root" ]] && printf 'export HPD_APPLEVZ_GUEST_BUNDLE_ROOT=%s\n' "$(shell_escape "$bundle_root")"
  [[ -n "$kernel_cmdline" ]] && printf 'export HPD_APPLEVZ_GUEST_KERNEL_CMDLINE=%s\n' "$(shell_escape "$kernel_cmdline")"
  [[ -n "$virtiofs_host_path" ]] && printf 'export HPD_APPLEVZ_VIRTIOFS_HOST_PATH=%s\n' "$(shell_escape "$virtiofs_host_path")"
  [[ -n "$virtiofs_tag" ]] && printf 'export HPD_APPLEVZ_VIRTIOFS_TAG=%s\n' "$(shell_escape "$virtiofs_tag")"
} > "$output"

printf 'wrote %s\n' "$output"
