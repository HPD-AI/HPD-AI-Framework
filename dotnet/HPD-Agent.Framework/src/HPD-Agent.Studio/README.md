# HPD-Agent.Studio

HPD Agent Studio module contribution for `HPD-AI.Platform`.

## Use

Use `HPD-AI.Platform` to host the composed Studio shell. This package carries only the Agent module source and metadata.

```csharp
builder.Services.AddHPDAIPlatform()
    .AddAgentStudio();
```

## Trace Span Evidence

Agent Studio listens for HPD Agent `ActivitySource` spans and correlates them to the canonical
journal through `hpd.trace_id`, `hpd.span_id`, and `hpd.parent_span_id`. Enable the runtime observer
when you want Activity evidence:

```csharp
builder.Services.AddHPDAgentHost(cfg =>
{
    cfg.ConfigureEachAgentBuilder = agent => agent.WithTracing();
});

builder.Services.AddHPDAIPlatform()
    .AddAgentStudio();
```

Set `SpanStorePath` to retain spans across restarts with the built-in append-only JSONL provider:

```csharp
builder.Services.AddHPDAIPlatform()
    .AddAgentStudio(options =>
    {
        options.SpanStorePath = Path.Combine(builder.Environment.ContentRootPath,
            ".hpd", "agent-studio", "telemetry", "spans.jsonl");
    });
```

Without `SpanStorePath`, Studio uses a bounded process-local projection. A host may also replace
`IAgentStudioTelemetryStore` with another durable provider. When Activity evidence is absent, Trace
detail falls back to the journal-derived span projection and discloses that source explicitly.
The built-in JSONL provider is single-writer; use a host-specific provider for multi-process or
multi-instance deployments.

The runtime emits turn, model-iteration, function-call, and durable subagent Activities. Function
Activities include the exact selected action and resolved invocation policy when an audit event is
present. Provider usage and agent decisions remain span events, preserving their point-in-time
semantics.

Trace discovery merges journal traces with retained Activity traces. A telemetry-only trace remains
inspectable after journal retention, but its canonical event and message sections are empty because
the journal remains the authority for those facts.

Events & logs uses server-side filtering over the retained canonical journal by time, Agent,
session, thread, trace, span, event type, and error state. Page-local search runs only over the
returned page. The page also receives a true process-wide SSE live tail from the Agent runtime hub,
with explicit pause/resume and a client-visible event cursor. Replay remains the separate bounded
journal query; live delivery is lossy telemetry and does not replace durable replay.

Event payload inspection is separately authorized and fail-closed by default. A host may explicitly
opt into the default authority:

```csharp
builder.Services.Configure<AgentStudioPayloadOptions>(options => options.Enabled = true);
```

Production hosts should replace `IAgentStudioEventPayloadAuthority` with an HPD Auth-aware policy.
Payload projections are bounded and sanitize sensitive JSON keys before rendering.

Metrics applies the same Agent, session, thread, outcome, and time scope to journal and retained
Activity traces. Journal-only function-call counts remain sourced from canonical tool-call events.

Observability saved views store only the current route query, are keyed by principal generation,
and remain device-local. Authorized JSON export serializes the projection already returned for the
active query; it never performs an uncapped background read or broadens payload authorization.

## Reviewed Agent Creation

The Agent catalog links to a dedicated `agent.creation` page. Its top-right **New Agent** action
opens the composer, which does not use an existing Agent resource. `agent.create` targets the
current application resource, so Agent Studio can create the first definition in an empty
installation without inventing a placeholder Agent identity.

The composer uses semantic sections for identity, model, runtime, capabilities, reliability, and
advanced policy. Common controls cover provider/model selection, loop limits, subagent fork policy,
ToolHarnesses, middleware, collapsing, tool output, retries, Goals, retention, and shutdown. Every
remaining serializable `AgentConfig` section remains available in a focused advanced editor.
Controls materialize into the same portable definition as JSON and YAML, with either representation
visible beside the form.

The request carries an immutable Agent ID and the generated complete JSON `AgentConfig`. Preview
deserializes the draft through the canonical serializer, runs `AgentConfigValidator` with the host
validation limits, checks that the ID is not already present, and returns a five-minute canonical
configuration checksum. No definition is written during review.

Execution rechecks the application authority, exact review, acknowledgement, ID availability, and
configuration before calling `IAgentDefinitionService.CreateAgentAsync`. The host remains
authoritative for configuration validation and conflict detection, including a concurrent create
between preview and execution. Successful creation refreshes the catalog and reports the created
ID and name.

Application resource resolution is reusable across Studio modules. A framework module such as Agent
Studio may own an application-targeted command while the page still uses the same application
identity vocabulary as BASE; it does not expose or enable the BASE Studio module.

## Reviewed Session and Thread Deletion

Session detail exposes `agent.session.delete`; thread detail exposes `agent.thread.delete`. Both are
destructive Studio commands. The browser cannot delete through the runtime read or mutation routes
directly: it must first receive a five-minute, checksum-bound command preview tied to the exact
resource authority, page, command, input, and current response authority.

Execution is admitted only when all of the following still match the reviewed preview:

- command and page identity
- exact session or thread resource identity and authority checksum
- current Studio response-authority checksum
- preview checksum
- `confirm.session.delete` or `confirm.thread.delete`
- `impact.session.delete` or `impact.thread.delete`
- the preview-bound acknowledgement checksum

Destructive execution also requires host-owned multi-factor fresh authentication. The fresh-auth
authority is bound to the request identity, command, target, preview, principal generation, session,
protected scope, and integration checksum. A missing, stale, substituted, or reused authority fails
closed before lifecycle influence.

A session deletion removes the session and its durable threads through `IAgentSessionService`.
A thread deletion is non-recursive by default and fails when child threads exist. The reviewed
recursive option deletes the complete child-thread subtree only when the host permits recursive
thread deletion. Studio does not invent a second lifecycle store or cascade outside the Agent host
services.

The preview is consumed atomically at execute admission, so one review cannot launch concurrent
deletions. Failed-before-influence and indeterminate outcomes remain distinct in the UI. Successful
session deletion returns to the session catalog; successful thread deletion returns to the
authoritative parent session when it remains disclosed. Separately retained OTel spans may remain
historical evidence, but deleted Agent resources are no longer exposed through Studio.

## Frontend Source

The reusable Svelte source lives in the repository TypeScript area.

- `typescript/hpd-ai-studio/modules/hpd-agent-studio` owns the HPD Agent Studio module packaged here.
