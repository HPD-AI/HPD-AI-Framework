# Generated HPD Agent TypeScript SDK

- Generator: `0.1.0`
- IR: `1.0`
- OpenAPI SHA-256: `sha256:ad200185e94b191cef3e50f823540233d361e90ef1b6f295a4904d822b401b82`
- Input digest: `sha256:be4f985d04fabf388ad5fb7410ef875025d1c5cb0038dfb1a05ff0abe38a7cf3`
- IR SHA-256: `sha256:995fcf8247fb90cdd100a965f1c318084f381ea637b0982694e98b303306c47e`
- Operations: 74
- Models: 2223
- Events: 145
- Public inputs: 7
- Supported capabilities: agents, audio, collapsing, content, evaluations, executions, forking, goals, interactions, json, middleware, multimodal-content, provider-authentication, runtime-extensions, server-sent-events, sessions, threads, unknown-events

The generated contract is the primary implementation. The hand-written
runtime owns fetch, retries, ProblemDetails, SSE framing, cursor state,
replay suppression, and the client-tool registry.

Provider WebSocket and multipart upload currently fail explicitly in this
target. They remain visible as source capabilities in `sdk-ir.json`.

The previous hand-written TypeScript SDK is retained as a behavioral
reference at `../archive/hpd-agent-client-manual`. Its 131 tests remain
the compatibility checklist for restoring advanced runtime facades as
generated integrations.
