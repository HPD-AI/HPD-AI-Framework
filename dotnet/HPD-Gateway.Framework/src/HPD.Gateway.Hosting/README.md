# HPD.Gateway.Hosting

This optional package realizes the closed Decision 0008 host contract through
Kestrel configuration-backed HTTPS/SNI selection.

The current slice supports only restart-bound data-plane HTTPS listeners with
exact or `*.` wildcard SNI entries and
`RejectUnmatchedOrMissingSni`. It deliberately emits no `*` SNI entry and no
default certificate, so Kestrel rejects missing or unmatched SNI during the
TLS handshake.

Certificate material is registered by host code as a startup-only absolute
PFX source selected through declaration `SecretReference` values. Certificate
paths, passwords, objects, keys, and provider errors do not enter host JSON,
canonical host identity, YARP configuration, or ordinary diagnostics.

The package does not provide dynamic listener reload, default-certificate
fallback, mTLS, HTTP/3, listener-aware Route matching, TLS passthrough, TCP,
UDP, certificate issuance, or secret storage.
