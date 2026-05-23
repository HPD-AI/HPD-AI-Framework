#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../../../.." && pwd)"
script_dir="$repo_root/HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image"

image_url="https://cloud-images.ubuntu.com/releases/noble/release/ubuntu-24.04-server-cloudimg-arm64.img"
output_root="${HOME}/.hpd/applevz/images/ubuntu-24.04-arm64"
disk_size="16G"
memory="4096"
cpus="4"
timeout_seconds="1200"
install_docker="false"
install_containerd="false"
install_podman="false"
install_buildkit="false"
run_qemu="true"
force="false"

usage() {
  cat <<'USAGE'
Usage:
  prepare-ubuntu-qemu-image.sh [options]

Downloads the Ubuntu 24.04 arm64 cloud image, creates a cloud-init seed with
the HPD guest payload, boots once under qemu-system-aarch64, copies vmlinuz and
initrd.img out through a FAT transfer disk, prepares a VZ-compatible uncompressed
kernel image, and writes a real-acceptance env file.

Options:
  --output-root PATH          default: ~/.hpd/applevz/images/ubuntu-24.04-arm64
  --image-url URL             default: Ubuntu 24.04 arm64 cloud image
  --disk-size SIZE            default: 16G
  --memory MB                 default: 4096
  --cpus N                    default: 4
  --timeout SECONDS           default: 1200
  --install-docker            ask cloud-init to install docker.io
  --install-containerd        ask cloud-init to install containerd and prepare ctr smoke image
  --install-podman            ask cloud-init to install rootful Podman and prepare smoke image
  --install-buildkit          ask cloud-init to install rootful BuildKit and prepare build smoke
  --no-run                    create inputs but do not boot QEMU
  --force                     recreate prepared disk artifacts from cached base image
  -h, --help
USAGE
}

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --output-root) output_root="$2"; shift 2 ;;
    --image-url) image_url="$2"; shift 2 ;;
    --disk-size) disk_size="$2"; shift 2 ;;
    --memory) memory="$2"; shift 2 ;;
    --cpus) cpus="$2"; shift 2 ;;
    --timeout) timeout_seconds="$2"; shift 2 ;;
    --install-docker) install_docker="true"; shift ;;
    --install-containerd) install_containerd="true"; shift ;;
    --install-podman) install_podman="true"; shift ;;
    --install-buildkit) install_buildkit="true"; shift ;;
    --no-run) run_qemu="false"; shift ;;
    --force) force="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'unknown argument: %s\n\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

require_tool() {
  if ! command -v "$1" >/dev/null 2>&1; then
    printf 'missing required tool: %s\n' "$1" >&2
    exit 1
  fi
}

require_tool curl
require_tool qemu-img
require_tool qemu-system-aarch64
require_tool hdiutil
require_tool diskutil
require_tool gzip

mkdir -p "$output_root"

base_image="$output_root/base-ubuntu-24.04-arm64.img"
prepared_qcow2="$output_root/hpd-ubuntu-24.04-arm64.qcow2"
prepared_raw="$output_root/hpd-ubuntu-24.04-arm64.raw"
seed_dir="$output_root/cidata"
seed_iso="$output_root/cidata.iso"
boot_transfer="$output_root/hpdboot.raw"
vars_fd="$output_root/edk2-vars.fd"
serial_log="$output_root/qemu-prep.serial.log"
real_env="$output_root/hpd-applevz-real.env"
firmware_code="/opt/homebrew/share/qemu/edk2-aarch64-code.fd"
firmware_vars_template="/opt/homebrew/share/qemu/edk2-arm-vars.fd"
image_basename="$(basename "$image_url")"
unpacked_url="$(dirname "$image_url")/unpacked"
direct_kernel_url="$unpacked_url/${image_basename/.img/-vmlinuz-generic}"
direct_initrd_url="$unpacked_url/${image_basename/.img/-initrd-generic}"

if [[ ! -f "$firmware_code" ]]; then
  firmware_code="$(brew --prefix qemu)/share/qemu/edk2-aarch64-code.fd"
fi
if [[ ! -f "$firmware_vars_template" ]]; then
  firmware_vars_template="$(brew --prefix qemu)/share/qemu/edk2-arm-vars.fd"
fi

if [[ ! -f "$base_image" ]]; then
  curl -L --fail --output "$base_image.tmp" "$image_url"
  mv "$base_image.tmp" "$base_image"
fi

if [[ "$force" == "true" ]]; then
  rm -f "$prepared_qcow2" "$prepared_raw" "$output_root/vmlinuz" "$output_root/vmlinux" "$output_root/initrd.img" "$real_env"
fi

if [[ ! -f "$prepared_qcow2" ]]; then
  qemu-img convert -f qcow2 -O qcow2 "$base_image" "$prepared_qcow2"
fi
qemu-img resize "$prepared_qcow2" "$disk_size" >/dev/null

"$script_dir/write-cloud-init-seed.sh" \
  --output-dir "$seed_dir" \
  --iso "$seed_iso" \
  --extract-boot-label HPDBOOT \
  --poweroff \
  $( [[ "$install_docker" == "true" ]] && printf '%s' '--install-docker' ) \
  $( [[ "$install_containerd" == "true" ]] && printf '%s' '--install-containerd' ) \
  $( [[ "$install_podman" == "true" ]] && printf '%s' '--install-podman' ) \
  $( [[ "$install_buildkit" == "true" ]] && printf '%s' '--install-buildkit' )

create_fat_transfer_disk() {
  rm -f "$boot_transfer"
  dd if=/dev/zero of="$boot_transfer" bs=1m count=256 >/dev/null
  local device
  device="$(hdiutil attach -nomount -imagekey diskimage-class=CRawDiskImage "$boot_transfer" | awk 'NR == 1 { print $1 }')"
  if [[ -z "$device" ]]; then
    printf 'failed to attach boot transfer disk\n' >&2
    exit 1
  fi
  diskutil eraseDisk FAT32 HPDBOOT MBRFormat "$device" >/dev/null
  hdiutil detach "$device" >/dev/null
}

download_direct_boot_artifacts() {
  if curl -fsI "$direct_kernel_url" >/dev/null 2>&1 && curl -fsI "$direct_initrd_url" >/dev/null 2>&1; then
    curl -L --fail --output "$output_root/vmlinuz.tmp" "$direct_kernel_url"
    curl -L --fail --output "$output_root/initrd.img.tmp" "$direct_initrd_url"
    mv "$output_root/vmlinuz.tmp" "$output_root/vmlinuz"
    mv "$output_root/initrd.img.tmp" "$output_root/initrd.img"
    printf 'downloaded direct VZ boot artifacts:\n  %s\n  %s\n' "$direct_kernel_url" "$direct_initrd_url"
  elif [[ ! -f "$output_root/vmlinuz" || ! -f "$output_root/initrd.img" ]]; then
    printf 'direct boot artifacts unavailable and no extracted boot files exist:\n  %s\n  %s\n' "$direct_kernel_url" "$direct_initrd_url" >&2
    exit 1
  fi
}

prepare_vz_kernel() {
  local kernel="$output_root/vmlinuz"
  local vz_kernel="$output_root/vmlinux"
  if [[ ! -f "$kernel" ]]; then
    printf 'missing kernel artifact: %s\n' "$kernel" >&2
    exit 1
  fi

  if file "$kernel" | grep -qi 'gzip compressed data'; then
    gzip -dc "$kernel" > "$vz_kernel.tmp"
    mv "$vz_kernel.tmp" "$vz_kernel"
  else
    cp "$kernel" "$vz_kernel"
  fi

  if ! file "$vz_kernel" | grep -q 'Linux kernel ARM64 boot executable Image'; then
    printf 'prepared VZ kernel does not look like an uncompressed arm64 Image: %s\n' "$vz_kernel" >&2
    file "$vz_kernel" >&2
    exit 1
  fi
}

create_fat_transfer_disk
cp "$firmware_vars_template" "$vars_fd"

if [[ "$run_qemu" == "true" ]]; then
  set +e
  qemu-system-aarch64 \
    -machine virt,accel=hvf \
    -cpu host \
    -smp "$cpus" \
    -m "$memory" \
    -nographic \
    -drive if=pflash,format=raw,readonly=on,file="$firmware_code" \
    -drive if=pflash,format=raw,file="$vars_fd" \
    -drive if=virtio,format=qcow2,file="$prepared_qcow2" \
    -drive if=virtio,media=cdrom,format=raw,file="$seed_iso" \
    -drive if=virtio,format=raw,file="$boot_transfer" \
    -netdev user,id=net0 \
    -device virtio-net-pci,netdev=net0 \
    -serial "file:$serial_log" \
    -monitor none &
  qemu_pid=$!
  deadline=$((SECONDS + timeout_seconds))
  while kill -0 "$qemu_pid" >/dev/null 2>&1; do
    if (( SECONDS > deadline )); then
      kill "$qemu_pid" >/dev/null 2>&1 || true
      wait "$qemu_pid" >/dev/null 2>&1 || true
      printf 'QEMU preparation timed out after %s seconds. Serial log: %s\n' "$timeout_seconds" "$serial_log" >&2
      exit 1
    fi
    sleep 5
  done
  wait "$qemu_pid"
  qemu_status=$?
  set -e
  if [[ "$qemu_status" -ne 0 ]]; then
    printf 'QEMU preparation exited with status %s. Serial log: %s\n' "$qemu_status" "$serial_log" >&2
    exit "$qemu_status"
  fi
fi

extract_boot_files() {
  local mount_point="$output_root/hpdboot-mount"
  mkdir -p "$mount_point"
  local device
  device="$(hdiutil attach -nomount -imagekey diskimage-class=CRawDiskImage "$boot_transfer" | awk 'NR == 1 { print $1 }')"
  if [[ -z "$device" ]]; then
    printf 'failed to attach boot transfer disk for extraction\n' >&2
    exit 1
  fi
  local partition="${device}s1"
  if [[ ! -e "$partition" ]]; then
    partition="$device"
  fi
  mount -t msdos "$partition" "$mount_point"
  cp "$mount_point/vmlinuz" "$output_root/vmlinuz"
  cp "$mount_point/initrd.img" "$output_root/initrd.img"
  umount "$mount_point"
  hdiutil detach "$device" >/dev/null
}

if [[ "$run_qemu" == "true" ]]; then
  extract_boot_files
fi
download_direct_boot_artifacts
prepare_vz_kernel

qemu-img convert -f qcow2 -O raw "$prepared_qcow2" "$prepared_raw"

if [[ -f "$output_root/vmlinux" && -f "$output_root/initrd.img" ]]; then
  engine_kind="DockerCompatible"
  engine_api="DockerCompatible"
  engine_authority_mode="Rootless"
  engine_socket="/run/user/1000/docker.sock"
  smoke_image="alpine:3.20"
  if [[ "$install_docker" == "true" ]]; then
    engine_authority_mode="Rootful"
    engine_socket="/var/run/docker.sock"
  fi
  if [[ "$install_containerd" == "true" ]]; then
    engine_kind="Containerd"
    engine_api="ContainerdApi"
    engine_authority_mode="Rootful"
    engine_socket="/run/containerd/containerd.sock"
    smoke_image="docker.io/library/alpine:3.20"
  fi
  if [[ "$install_podman" == "true" ]]; then
    engine_kind="Podman"
    engine_api="PodmanApi"
    engine_authority_mode="Rootful"
    engine_socket="/run/podman/podman.sock"
    smoke_image="docker.io/library/alpine:3.20"
  fi
  if [[ "$install_buildkit" == "true" ]]; then
    engine_kind="BuildKit"
    engine_api="BuildKitApi"
    engine_authority_mode="Rootful"
    engine_socket="/run/buildkit/buildkitd.sock"
    smoke_image="hpd-buildkit-smoke:local"
  fi

  "$repo_root/HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/write-real-container-env.sh" \
    --kernel "$output_root/vmlinux" \
    --initrd "$output_root/initrd.img" \
    --disk "$prepared_raw" \
    --serial-log "$output_root/apple-vz.serial.log" \
    --guest-agent-version 0.1.0 \
    --kernel-cmdline "root=LABEL=cloudimg-rootfs ro rootwait console=hvc0" \
    --engine-kind "$engine_kind" \
    --engine-api "$engine_api" \
    --authority-mode "$engine_authority_mode" \
    --engine-socket "$engine_socket" \
    --smoke-image "$smoke_image" \
    --output "$real_env"
fi

cat <<EOF
prepared image root: $output_root
qcow2 disk:          $prepared_qcow2
raw disk:            $prepared_raw
seed iso:            $seed_iso
boot transfer disk:  $boot_transfer
serial log:          $serial_log
env file:            $real_env
EOF
