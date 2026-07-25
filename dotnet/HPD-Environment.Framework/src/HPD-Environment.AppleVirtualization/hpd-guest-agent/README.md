# hpd-guest-agent

This is the first HPD Apple Virtualization guest payload.

It is intentionally narrow:

- listens on the HPD virtio-socket port, default `7777`;
- accepts newline-delimited JSON request envelopes;
- responds to `Hello` and `Ready`;
- advertises the capabilities needed for the real container smoke readiness gate;
- reports guest network interfaces/routes for endpoint publication;
- provides a bounded TCP proxy used by real Apple NAT endpoint acceptance when
  the guest address is not directly host-routable;
- starts and observes guest processes;
- projects guest engine authority sockets;
- installs `/hpd/container-smoke` as the smoke command contract.
- installs `/hpd/container-run` as the argument-safe Docker workload boundary.

The payload is still intentionally small, but it is no longer readiness-only. It
is sufficient for the current real container and real HTTP endpoint acceptance
paths.

## Local Protocol Check

```bash
printf '{"ProtocolVersion":"1.0","MessageType":0,"Operation":0,"RequestId":"hello","SequenceNumber":1}\n{"ProtocolVersion":"1.0","MessageType":0,"Operation":2,"RequestId":"ready","SequenceNumber":2}\n' \
  | src/hpd_guest_agent.py --stdio
```

## Image Installation

For a mounted/customized image, run:

```bash
packaging/install-hpd-guest-payload.sh
```

For a cloud-init path, from the repo root run:

```bash
docs/apple-virtualization/guest-image/write-cloud-init-seed.sh --iso /tmp/hpd-applevz-cidata.iso
```

The current `hpd-vz` VM builder does not yet attach a NoCloud seed disk. The seed
is still useful for preparing the Ubuntu image with external tooling or for the
next VM configuration slice that adds secondary cloud-init media.
