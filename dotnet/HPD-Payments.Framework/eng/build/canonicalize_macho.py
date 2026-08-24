#!/usr/bin/env python3
"""Canonicalize and ad-hoc sign one little-endian 64-bit Mach-O executable."""

from __future__ import annotations

import hashlib
import json
import os
import pathlib
import struct
import subprocess
import sys

LC_UUID = 0x1B
LC_CODE_SIGNATURE = 0x1D
MH_MAGIC_64 = 0xFEEDFACF
IDENTIFIER = "org.hpd.payments.conformance"
MARKER_SUFFIX = ".hpd-canonicalized.json"


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: canonicalize_macho.py <executable>")
    path = pathlib.Path(sys.argv[1]).resolve(strict=True)
    marker = pathlib.Path(str(path) + MARKER_SUFFIX)
    if marker.exists():
        raise SystemExit("artifact was already canonicalized; a fresh publish is required")
    original = path.read_bytes()
    data = bytearray(original)
    if len(data) < 32 or struct.unpack_from("<I", data)[0] != MH_MAGIC_64:
        raise SystemExit("input is not a little-endian 64-bit Mach-O file")

    command_count = struct.unpack_from("<I", data, 16)[0]
    cursor = 32
    uuid_offset = None
    signature = None
    for _ in range(command_count):
        command, size = struct.unpack_from("<II", data, cursor)
        if size < 8 or cursor + size > len(data):
            raise SystemExit("invalid Mach-O load-command table")
        if command == LC_UUID:
            if size != 24 or uuid_offset is not None:
                raise SystemExit("invalid or duplicate LC_UUID")
            uuid_offset = cursor + 8
        elif command == LC_CODE_SIGNATURE:
            if size != 16 or signature is not None:
                raise SystemExit("invalid or duplicate LC_CODE_SIGNATURE")
            signature = struct.unpack_from("<II", data, cursor + 8)
        cursor += size
    if uuid_offset is None or signature is None:
        raise SystemExit("Mach-O UUID or code signature is absent")

    signature_offset, signature_size = signature
    if signature_offset + signature_size > len(data):
        raise SystemExit("invalid Mach-O code-signature range")
    data[uuid_offset : uuid_offset + 16] = bytes(16)
    data[signature_offset : signature_offset + signature_size] = bytes(signature_size)
    canonical_uuid = bytearray(hashlib.sha256(data).digest()[:16])
    canonical_uuid[6] = (canonical_uuid[6] & 0x0F) | 0x50
    canonical_uuid[8] = (canonical_uuid[8] & 0x3F) | 0x80

    try:
        reservation_fd = os.open(marker, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o644)
    except FileExistsError:
        raise SystemExit("artifact was already canonicalized; a fresh publish is required") from None
    os.close(reservation_fd)
    try:
        output = bytearray(original)
        output[uuid_offset : uuid_offset + 16] = canonical_uuid
        path.write_bytes(output)
        subprocess.run(
            ["/usr/bin/codesign", "--force", "--sign", "-", "--timestamp=none", "--identifier", IDENTIFIER, str(path)],
            check=True,
        )
        subprocess.run(["/usr/bin/codesign", "--verify", "--strict", str(path)], check=True)
        output_digest = hashlib.sha256(path.read_bytes()).hexdigest()
        marker_payload = {
            "schema": "hpd.payments.macho-canonicalization.v1",
            "toolSha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
            "inputSha256": hashlib.sha256(original).hexdigest(),
            "outputSha256": output_digest,
            "canonicalUuid": canonical_uuid.hex(),
        }
        descriptor = json.dumps(marker_payload, sort_keys=True, separators=(",", ":")) + "\n"
        marker.write_text(descriptor, encoding="utf-8")
    except BaseException:
        marker.unlink(missing_ok=True)
        raise
    print(f"canonicalUuid={canonical_uuid.hex()} sha256={output_digest} marker={marker.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
