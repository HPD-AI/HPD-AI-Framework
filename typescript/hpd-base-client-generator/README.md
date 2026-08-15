# @hpd/base-client-generator

Consumes the authenticated HPD Base generation snapshot v2 and atomically emits immutable
application or control-plane TypeScript bindings. The generator validates output with its own
pinned TypeScript 7.0.2 CLI through `process.execPath`; it never imports a compiler API.
