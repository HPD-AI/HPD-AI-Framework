# Generated HPD Agent TypeScript SDK

- Generator: `0.1.0`
- IR: `1.0`
- OpenAPI SHA-256: `sha256:f29a4d95a311cd048a1010c94fbf344a642166981d8849bf28953b05c7101460`
- Input digest: `sha256:be4f985d04fabf388ad5fb7410ef875025d1c5cb0038dfb1a05ff0abe38a7cf3`
- IR SHA-256: `sha256:bc183862547c43d6f7b32d952eb0095f7379b365cf1166fc76e5fb4c7b264ebb`
- Operations: 74
- Models: 2325
- Events: 144
- Public inputs: 7
- Supported capabilities: agents, audio, collapsing, content, evaluations, executions, forking, goals, interactions, json, middleware, multimodal-content, provider-authentication, runtime-extensions, server-sent-events, sessions, threads, unknown-events

The generated contract is the primary implementation. The hand-written
runtime owns fetch, retries, ProblemDetails, SSE framing, cursor state,
replay suppression, and the client-tool registry.

Provider WebSocket currently fails explicitly in this target. The
source capability remains visible in `sdk-ir.json`.

The full hosted contract includes the session-first TypeScript client.
Create a session with `client.sessions.create(...)`, then use the
returned thread to observe routed events and send input. The client
exposes generated endpoints through `client.api`.

Observation resolves when the stream opens. Await `observation.done`
for terminal stream or handler errors. Supply `onRebase` to reset your
projection before replaying a replacement journal.
