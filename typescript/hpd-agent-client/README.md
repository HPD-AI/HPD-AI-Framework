# Generated HPD Agent TypeScript SDK

- Generator: `0.1.0`
- IR: `1.0`
- OpenAPI SHA-256: `sha256:7d07ba8b66ed74773a972350add6d90396764e6d0f0c7a8c38ab5a253f3b1136`
- Input digest: `sha256:be4f985d04fabf388ad5fb7410ef875025d1c5cb0038dfb1a05ff0abe38a7cf3`
- IR SHA-256: `sha256:2d241b9c5e21768007c6b840cde577167f529272226a14cd0af66d49ceb1cf1e`
- Operations: 74
- Models: 3210
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

First-class resources include `client.agents`, `session.threads`,
`thread.content`, `thread.executions`, and `client.evaluations`.
`thread.history()` returns a finite protocol-level message projection.

Every generated service operation accepts a second `RequestOptions`
argument. Client-wide and per-call abort signals compose, and request
timeouts cover JSON, multipart, binary, and SSE transport operations.

Observation resolves when the stream opens. Await `observation.done`
for terminal stream or handler errors. Supply `onRebase` to reset your
projection before replaying a replacement journal.
