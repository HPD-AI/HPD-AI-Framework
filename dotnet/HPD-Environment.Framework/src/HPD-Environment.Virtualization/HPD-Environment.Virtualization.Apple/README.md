# HPD Environment Apple Virtualization

This package owns the Apple Virtualization.framework provider, its signed
`hpd-vz` helper integration, guest-agent protocol, process execution, content
projection, networking, endpoint, authority, storage, and guest workspace file
operations.

Sandbox workspace access uses the selected execution unit's guest process lane
to run bounded file-service operations. Model-facing tools receive logical
workspace paths; host filesystem access is not used as a fallback.

The implementation requires verified guest readiness and a verified
content projection before the workspace process lane is used. File operations
deny traversal and process-visible symlinks, bound text and binary reads, use
content digests for stale-write checks, and preserve logical paths in results.

Real Apple Virtualization acceptance remains opt-in and requires the signed
helper, Virtualization.framework entitlement, a guest image, and the matching
guest payload.
