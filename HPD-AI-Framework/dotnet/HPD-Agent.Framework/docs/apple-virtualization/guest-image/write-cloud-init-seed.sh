#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../../../.." && pwd)"
payload_root="$repo_root/HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/hpd-guest-agent"
output_dir="/tmp/hpd-applevz-cidata"
iso_path=""
agent_version="0.1.0"
protocol_version="1.0"
vsock_port="7777"
install_docker="false"
install_containerd="false"
install_podman="false"
install_buildkit="false"
poweroff="false"
extract_boot_label=""

usage() {
  cat <<'USAGE'
Usage:
  write-cloud-init-seed.sh [options]

Creates a cloud-init NoCloud seed directory containing the HPD guest-agent
readiness payload and /hpd/container-smoke. If --iso is provided, also creates
a cidata ISO with hdiutil.

Options:
  --output-dir PATH             default: /tmp/hpd-applevz-cidata
  --iso PATH                    optional ISO output path
  --agent-version VERSION       default: 0.1.0
  --protocol-version VERSION    default: 1.0
  --vsock-port PORT             default: 7777
  --install-docker              add Docker package/service cloud-init commands
  --install-containerd          add containerd package/service cloud-init commands
  --install-podman              add rootful Podman package/service cloud-init commands
  --install-buildkit            add rootful BuildKit package/service cloud-init commands
  --poweroff                    shut down the guest after cloud-init completes
  --extract-boot-label LABEL    mount this FAT label and copy vmlinuz/initrd.img
  -h, --help
USAGE
}

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --output-dir) output_dir="$2"; shift 2 ;;
    --iso) iso_path="$2"; shift 2 ;;
    --agent-version) agent_version="$2"; shift 2 ;;
    --protocol-version) protocol_version="$2"; shift 2 ;;
    --vsock-port) vsock_port="$2"; shift 2 ;;
    --install-docker) install_docker="true"; shift ;;
    --install-containerd) install_containerd="true"; shift ;;
    --install-podman) install_podman="true"; shift ;;
    --install-buildkit) install_buildkit="true"; shift ;;
    --poweroff) poweroff="true"; shift ;;
    --extract-boot-label) extract_boot_label="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'unknown argument: %s\n\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
done

agent_py="$payload_root/src/hpd_guest_agent.py"
service_file="$payload_root/packaging/hpd-guest-agent.service"
smoke_file="$payload_root/packaging/container-smoke"

for path in "$agent_py" "$service_file" "$smoke_file"; do
  if [[ ! -f "$path" ]]; then
    printf 'missing payload file: %s\n' "$path" >&2
    exit 1
  fi
done

rm -rf "$output_dir"
mkdir -p "$output_dir"

cat > "$output_dir/meta-data" <<'EOF'
instance-id: hpd-applevz-guest
local-hostname: hpd-applevz
EOF

python3 - "$agent_py" "$service_file" "$smoke_file" "$output_dir/user-data" "$agent_version" "$protocol_version" "$vsock_port" "$install_docker" "$install_containerd" "$install_podman" "$install_buildkit" "$poweroff" "$extract_boot_label" <<'PY'
import base64
import pathlib
import sys

agent, service, smoke, output, agent_version, protocol_version, port, install_docker, install_containerd, install_podman, install_buildkit, poweroff, extract_boot_label = sys.argv[1:]

def b64(path):
    return base64.b64encode(pathlib.Path(path).read_bytes()).decode("ascii")

package_names = []
write_files_extra = ""
runcmd_extra_parts = []
if install_docker == "true":
    package_names.append("docker.io")
    runcmd_extra_parts.append("""  - [ systemctl, enable, --now, docker ]
  - [ sh, -c, 'for i in 1 2 3; do timeout 180 docker pull alpine:3.20 && exit 0; sleep 5; done; echo "docker pre-pull did not complete during image prep; smoke run may pull later" >&2; exit 0' ]
""")

if install_containerd == "true":
    package_names.append("containerd")
    runcmd_extra_parts.append("""  - [ systemctl, enable, --now, containerd ]
  - [ sh, -c, 'for i in 1 2 3 4 5 6; do ctr --address /run/containerd/containerd.sock images pull docker.io/library/alpine:3.20 && exit 0; sleep 5; done; exit 1' ]
""")

if install_podman == "true":
    package_names.append("podman")
    runcmd_extra_parts.append("""  - [ systemctl, enable, --now, podman.socket ]
  - [ sh, -c, 'for i in 1 2 3 4 5 6; do podman pull docker.io/library/alpine:3.20 && exit 0; sleep 5; done; exit 1' ]
""")

if install_buildkit == "true":
    package_names.extend(["ca-certificates", "curl", "tar"])
    write_files_extra = """  - path: /etc/systemd/system/buildkit.service
    owner: root:root
    permissions: '0644'
    content: |
      [Unit]
      Description=BuildKit daemon
      After=network-online.target
      Wants=network-online.target

      [Service]
      Type=simple
      ExecStartPre=/usr/bin/install -d -m 0755 /run/buildkit
      ExecStart=/usr/local/bin/buildkitd --addr unix:///run/buildkit/buildkitd.sock
      Restart=on-failure

      [Install]
      WantedBy=multi-user.target
"""
    runcmd_extra_parts.append("""  - [ sh, -c, 'arch="$(uname -m)"; case "$arch" in aarch64|arm64) url=https://github.com/containerd/nerdctl/releases/download/v2.2.2/nerdctl-full-2.2.2-linux-arm64.tar.gz ;; x86_64|amd64) url=https://github.com/containerd/nerdctl/releases/download/v2.2.2/nerdctl-full-2.2.2-linux-amd64.tar.gz ;; *) echo "unsupported buildkit architecture: $arch" >&2; exit 1 ;; esac; curl -L --fail "$url" | tar -xz -C /usr/local' ]
  - [ systemctl, daemon-reload ]
  - [ systemctl, enable, --now, buildkit.service ]
  - [ sh, -c, 'for i in 1 2 3 4 5 6; do buildctl --addr unix:///run/buildkit/buildkitd.sock debug workers >/dev/null && exit 0; sleep 5; done; exit 1' ]
""")

packages = ""
if package_names:
    unique = []
    for name in package_names:
        if name not in unique:
            unique.append(name)
    packages = "packages:\n" + "".join(f"  - {name}\n" for name in unique)

runcmd_extra = "".join(runcmd_extra_parts)

extract_boot = ""
if extract_boot_label:
    extract_boot = f"""  - [ mkdir, -p, /mnt/hpdboot ]
  - [ sh, -c, 'for i in 1 2 3 4 5 6 7 8 9 10; do mount LABEL={extract_boot_label} /mnt/hpdboot && exit 0; sleep 1; done; exit 1' ]
  - [ sh, -c, 'cp -L "$(ls -1 /boot/vmlinuz-* 2>/dev/null | sort -V | tail -n1)" /mnt/hpdboot/vmlinuz' ]
  - [ sh, -c, 'cp -L "$(ls -1 /boot/initrd.img-* 2>/dev/null | sort -V | tail -n1)" /mnt/hpdboot/initrd.img' ]
  - [ sync ]
  - [ umount, /mnt/hpdboot ]
"""

poweroff_command = ""
if poweroff == "true":
    poweroff_command = "  - [ poweroff ]\n"

content = f"""#cloud-config
{packages}write_files:
  - path: /usr/local/bin/hpd-guest-agent
    owner: root:root
    permissions: '0755'
    encoding: b64
    content: {b64(agent)}
  - path: /etc/systemd/system/hpd-guest-agent.service
    owner: root:root
    permissions: '0644'
    encoding: b64
    content: {b64(service)}
  - path: /hpd/container-smoke
    owner: root:root
    permissions: '0755'
    encoding: b64
    content: {b64(smoke)}
  - path: /etc/hpd/guest-agent/env
    owner: root:root
    permissions: '0644'
    content: |
      HPD_GUEST_AGENT_VERSION={agent_version}
      HPD_GUEST_AGENT_PROTOCOL_VERSION={protocol_version}
      HPD_GUEST_AGENT_VSOCK_PORT={port}
{write_files_extra}runcmd:
  - [ mkdir, -p, /hpd, /etc/hpd/guest-agent ]
  - [ chmod, '0755', /usr/local/bin/hpd-guest-agent, /hpd/container-smoke ]
  - [ systemctl, daemon-reload ]
  - [ systemctl, enable, --now, hpd-guest-agent.service ]
{runcmd_extra}{extract_boot}{poweroff_command}"""

pathlib.Path(output).write_text(content, encoding="utf-8")
PY

if [[ -n "$iso_path" ]]; then
  mkdir -p "$(dirname "$iso_path")"
  rm -f "$iso_path"
  hdiutil makehybrid -iso -joliet -default-volume-name cidata -o "$iso_path" "$output_dir" >/dev/null
  printf 'wrote cloud-init seed ISO: %s\n' "$iso_path"
fi

printf 'wrote cloud-init seed directory: %s\n' "$output_dir"
