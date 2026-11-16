# Generated HPD Agent TypeScript SDK

- Generator: `0.1.0`
- IR: `1.0`
- OpenAPI SHA-256: `sha256:2530d97e7c02461c889c9ef25cd112c209e19d16df914a2e0ce2871f05e49ba0`
- Input digest: `sha256:be4f985d04fabf388ad5fb7410ef875025d1c5cb0038dfb1a05ff0abe38a7cf3`
- IR SHA-256: `sha256:93134c5f72d4da475ee361e06f9ad7ff4e1f2f87e993a4e019116ca476a10149`
- Operations: 74
- Models: 3443
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

Agent definitions and per-run overrides use the generated
`AgentConfig`, `AgentConfigUpdate`, and `AgentRunConfig` objects.
`ToolHarnessReference` and `MiddlewareReference` support string and rich
object forms. The `model(...)` helper returns a generated
`ChatClientConfig` fragment.

```ts
const config = {
  name: 'Support Assistant',
  clients: { chat: model('openai', 'gpt-5-mini') },
  toolHarnesses: ['SupportTools'],
} satisfies AgentConfig;
```

Every generated service operation accepts a second `RequestOptions`
argument. Client-wide and per-call abort signals compose, and request
timeouts cover JSON, multipart, binary, and SSE transport operations.

Observation resolves when the stream opens. Await `observation.done`
for terminal stream or handler errors. Supply `onRebase` to reset your
projection before replaying a replacement journal.
