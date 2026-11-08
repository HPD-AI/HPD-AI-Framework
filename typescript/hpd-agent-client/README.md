# Generated HPD Agent TypeScript SDK

- Generator: `0.1.0`
- IR: `1.0`
- OpenAPI SHA-256: `sha256:e02e37c5395aa7124e17a5fc80b21610c0ee3072575294fa1051541921068f7e`
- Input digest: `sha256:be4f985d04fabf388ad5fb7410ef875025d1c5cb0038dfb1a05ff0abe38a7cf3`
- IR SHA-256: `sha256:a60265fd60d9cdecd0cb656ab79f35b7eeb7a0674cede155b8883aa28eaf58d0`
- Operations: 36
- Models: 2332
- Events: 213
- Public inputs: 7
- Supported capabilities: agents, audio, collapsing, content, executions, forking, goals, interactions, json, middleware, provider-authentication, runtime-extensions, server-sent-events, sessions, threads, unknown-events

The generated contract is the primary implementation. The hand-written
runtime owns fetch, retries, ProblemDetails, SSE framing, cursor state,
replay suppression, and the client-tool registry.

Provider WebSocket and multipart upload currently fail explicitly in this
target. They remain visible as source capabilities in `sdk-ir.json`.

The previous hand-written TypeScript SDK is retained as a behavioral
reference at `../archive/hpd-agent-client-manual`. Its 131 tests remain
the compatibility checklist for restoring advanced runtime facades as
generated integrations.
