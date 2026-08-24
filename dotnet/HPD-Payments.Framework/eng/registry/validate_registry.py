#!/usr/bin/env python3
import hashlib
import json
import re
import sys
from pathlib import Path

PROOF_STATES = {"Inspected", "Compiled", "Generated", "Linked", "Executed", "Failed", "Untested"}
AUTHORITIES = {
    "Scoped Identity", "Agreement", "Requested Transition", "Effective Commercial Fact",
    "Measured Fact", "Measurement Generation", "Valuation", "Obligation", "Issuance Fact",
    "Held Position", "Value Movement", "Entitlement Grant/Removal Fact", "Restriction Fact",
    "Capability Evidence", "External Effect", "Work Requirement", "Publication Obligation",
}

def digest(document):
    payload = dict(document)
    payload.pop("contentDigest", None)
    encoded = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()

def fail(message):
    raise SystemExit("registry validation failed: " + message)

canonical_path, claims_path = map(Path, sys.argv[1:3])
canonical = json.loads(canonical_path.read_text())
claims = json.loads(claims_path.read_text())

if canonical.get("schemaVersion") != "hpd.payments.canonical-capabilities.v1": fail("canonical schema")
if claims.get("schemaVersion") != "hpd.payments.claim-matrix.v1": fail("claim schema")
if canonical.get("contentDigest") != "sha256:" + digest(canonical): fail("canonical content digest")
if claims.get("contentDigest") != "sha256:" + digest(claims): fail("claim content digest")
rows = canonical.get("capabilities", [])
cells = claims.get("claims", [])
ids = [r["id"] for r in rows]
if len(ids) != 179 or len(set(ids)) != 179: fail("179 unique canonical IDs")
if len({r["prefix"] for r in rows}) != 33: fail("33 prefixes")
if {f"TEST-{n:03d}" for n in range(1, 7)} - set(ids): fail("TEST-001..006")
if set().union(*(set(r["authorityOwners"]) for r in rows)) != AUTHORITIES: fail("17-owner set")
if set().union(*(set(r["workflows"]) for r in rows)) != set(range(1, 21)): fail("20 workflows")
for r in rows:
    if r["hazards"] != [f"H{i}" for i in range(14)]: fail(r["id"] + " hazards")
    if r["ownershipCells"] != [f"OWN-{i:02d}" for i in range(1, 13)]: fail(r["id"] + " OWN")
    if r["extensionCells"] != ["EXT-DET-01","EXT-EFFECT-02","EXT-WORK-03","EXT-RESOURCE-04","EXT-ROTATE-05","EXT-UPGRADE-06","EXT-LANE-07","EXT-SEC-08","EXT-SER-09"]: fail(r["id"] + " EXT")
if len(cells) != 179 or len({c["cellId"] for c in cells}) != 179: fail("claim uniqueness")
if {c["canonicalId"] for c in cells} != set(ids): fail("orphan/missing claim")
for c in cells:
    if c["expectedProofState"] not in PROOF_STATES: fail(c["cellId"] + " proof state")
    for field in ("profile","lane","adapter","provider","graph","rid","toolchain","path","workload","requiredNegativeCell","applicability","rationale"):
        if field not in c: fail(c["cellId"] + " missing " + field)
accepted_res009 = [c for c in cells if c.get("res009Status") == "AcceptedPendingImplementation"]
if len(accepted_res009) != 28 or any(c["expectedProofState"] != "Untested" or c["applicability"] != "ApplicablePendingSelection" for c in accepted_res009): fail("RES-009 accepted disposition")
if claims.get("canonicalRegistryDigest") != canonical["contentDigest"]: fail("cross digest")
print(f"PASS canonical=179 unique=179 prefixes=33 owners=17 workflows=20 TEST=6 claims=179 duplicates=0 orphans=0 res009Accepted={len(accepted_res009)}")
print(f"canonicalDigest={canonical['contentDigest']}")
print(f"claimMatrixDigest={claims['contentDigest']}")
