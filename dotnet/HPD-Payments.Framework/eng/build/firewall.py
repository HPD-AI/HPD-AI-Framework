#!/usr/bin/env python3
"""Read-only dual-repository write-firewall inventory."""
import hashlib
import os
import pathlib
import subprocess
import sys

PRODUCT = pathlib.Path(__file__).resolve().parents[2]
FRAMEWORK = PRODUCT.parents[1]
HPDOS = FRAMEWORK.parents[1]

def git(root: pathlib.Path, *args: str) -> bytes:
    return subprocess.run(
        ["/usr/bin/git", *args],
        cwd=root,
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    ).stdout

def identity(label: str, root: pathlib.Path) -> None:
    status = git(root, "status", "--porcelain=v2", "-z")
    print(
        f"{label}Root={root} "
        f"head={git(root, 'rev-parse', 'HEAD').decode().strip()} "
        f"branch={git(root, 'symbolic-ref', '--short', 'HEAD').decode().strip()} "
        f"statusBytes={len(status)} statusSha256={hashlib.sha256(status).hexdigest()}"
    )

mode = sys.argv[1] if len(sys.argv) == 2 else ""
if mode not in {"before", "after"}:
    raise SystemExit("usage: firewall.py before|after")
resolved = pathlib.Path(os.path.realpath(PRODUCT))
if resolved != PRODUCT or not PRODUCT.is_dir():
    raise SystemExit("product root containment failed")
for path in PRODUCT.rglob("*"):
    if path.is_symlink():
        raise SystemExit(f"symlink forbidden in product tree: {path.relative_to(PRODUCT)}")
identity("hpdos", HPDOS)
identity("framework", FRAMEWORK)
print(f"mode={mode} productRoot={PRODUCT} containment=PASS symlinks=0")
