# hpd-vz

`hpd-vz` is the native Swift helper skeleton for the HPD Apple Virtualization provider.

This first scaffold is intentionally small:

- newline-delimited JSON envelopes over stdio;
- protocol-faithful `hello` and `health.probe` responses;
- structured protocol errors for unimplemented VM, projection, unit, process, and endpoint operations;
- an isolated `VirtualizationAdapter` boundary where real `Virtualization.framework` ownership will live later;
- `--fake` / `--local` mode for smoke tests that do not boot a VM.

The helper does not import HPD execution contracts. Its public boundary is the helper protocol shaped by `HPD-Execution.AppleVirtualization`.

Real VM lifecycle, virtiofs projection, virtio socket guest control, port forwarding, and process execution are stubs in this pass. A signed helper that creates `VZVirtualMachine` instances will need the `com.apple.security.virtualization` entitlement and packaging/signing validation before real host operations can be enabled.

## Real-Mode Preconditions

Normal builds and tests must not boot a VM. Real VM boot is opt-in from the .NET provider through explicit feature gates and a non-`--fake` helper invocation.

Before any future VM start, HPD checks:

- the configured `hpd-vz` path exists and is executable;
- fake helper mode is not selected;
- the selected guest architecture matches the host process architecture;
- Linux kernel, initrd, writable disk image, and serial log path are present;
- serial log parent directories can be created safely;
- optional virtiofs host paths exist;
- entitlement and signing facts are not treated as passed unless the signed running helper or Apple runtime can verify them.

For development signing, use:

```bash
packaging/sign-hpd-vz.sh
```

The script signs the built helper with `packaging/hpd-vz.entitlements`, which contains `com.apple.security.virtualization`. Ad-hoc signing is acceptable for local helper preflight and local VM experimentation. Release packaging should use a real signing identity and preserve the same entitlement.

Run this script after every `swift build` that rewrites `.build/.../hpd-vz`; a fresh Swift debug build can replace the local ad-hoc signature.
