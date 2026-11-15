# Generated HPD Agent TypeScript SDK

- Generator: `0.1.0`
- IR: `1.0`
- OpenAPI SHA-256: `sha256:101fdb2322bd618fd009d0469dd53ff9cc4c9542772595ac2d79ba01d3ac66ee`
- Input digest: `sha256:be4f985d04fabf388ad5fb7410ef875025d1c5cb0038dfb1a05ff0abe38a7cf3`
- IR SHA-256: `sha256:4a9d75f5fb98e40e6489b26596f3aaedd45a7eadd2f88155d4e83f8c81d6f7bd`
- Operations: 74
- Models: 2324
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
