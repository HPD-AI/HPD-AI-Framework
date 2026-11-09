# HPD Agent Audio Telephony Qualification

This directory contains a private SIP/RTP qualification leaf for validating
PCMU/8000 audio through a real Asterisk PBX.

It is not currently published as a supported user-facing transport package.
The test is intentionally provider-dependent and requires a reachable PBX.

## Run

```bash
npm test
```

Optional environment variables:

- `HPD_PBX_HOST` defaults to `127.0.0.1`.
- `HPD_PBX_ADVERTISED_HOST` defaults to `HPD_PBX_HOST`.

The PBX and extension configuration used for qualification are under
`pbx/`. Do not describe this artifact as a production telephony integration
until it has a packaged API, deployment guidance, and the same structured
lifecycle/diagnostic coverage as the managed LiveKit transport.
