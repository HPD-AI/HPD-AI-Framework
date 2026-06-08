#!/usr/bin/env sh
set -eu

payload_root="${1:-$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)}"

install -d -m 0755 /usr/local/bin
install -d -m 0755 /etc/systemd/system
install -d -m 0755 /hpd

install -m 0755 "$payload_root/../src/hpd_guest_agent.py" /usr/local/bin/hpd-guest-agent
install -m 0644 "$payload_root/hpd-guest-agent.service" /etc/systemd/system/hpd-guest-agent.service
install -m 0755 "$payload_root/container-smoke" /hpd/container-smoke

if command -v systemctl >/dev/null 2>&1; then
  systemctl daemon-reload
  systemctl enable hpd-guest-agent.service
  systemctl restart hpd-guest-agent.service || true
fi

echo "hpd guest payload installed"
