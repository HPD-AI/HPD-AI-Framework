# Native tool outputs and retrieval

HPD can replace explicitly marked native tool-result properties with a
`ContentReference`. Unmarked properties stay inline. There is no automatic
size threshold, content-type inference, summarization, or MCP fallback.

## Mark an output

Mark the exact properties that should become retrievable content:

```csharp
public sealed record BacktestResult
{
    public required string RunId { get; init; }
    public required BacktestSummary Summary { get; init; }
    public required string AuthoritativeArtifactId { get; init; }

    [Offload(Sensitivity = ContentSensitivity.Restricted)]
    public required BacktestEvidence Evidence { get; init; }
}
```

`RunId`, `Summary`, and `AuthoritativeArtifactId` remain inline. `Evidence` is
stored as one session-owned bundle and the model receives a `ContentReference`
instead of the complete value. A null marked property remains null.

The source generator discovers marked properties and public fields, including
nested result objects. Result serialization uses the function's effective
`JsonSerializerOptions` and `JsonTypeInfo`.

## Stream a capture

Tool bodies create captures through `FunctionExecutionContext.Outputs` and do
not receive an `IContentStore`:

```csharp
public sealed record CommandResult
{
    public required string Summary { get; init; }

    [Offload]
    public required ContentCapturePart Stdout { get; init; }
}

var stdout = context.Outputs.CreatePart(
    "stdout",
    "text/plain",
    ContentSensitivity.Internal);

await stdout.WriteAsync(bytes);

return new CommandResult
{
    Summary = "completed",
    Stdout = stdout
};
```

All marked values from one result are written as one immutable bundle. The
bundle digest is checked before a reference becomes model-visible. Retrying the
same accepted result reuses the same logical bundle.

## Read stored content

When retrieval is enabled, HPD registers one action-contracted `Content` tool:

- `list` returns bounded metadata and an opaque stable cursor.
- `stat` returns metadata and bundle part names without opening the body.
- `read` returns bounded line or byte ranges, exact version and digest checks,
  and continuation metadata when more content remains.

Content is scoped by session and owned by a thread. Same-session direct forks
may use their source thread's immutable content; unrelated threads cannot.
Session deletion removes the session scope. Application-owned artifacts in
other scopes are unaffected.

## Configure policy

```csharp
var config = new AgentConfig
{
    Name = "analyst",
    ToolOutputs = new ToolOutputConfig
    {
        Enabled = true,
        MaxInlineResultBytes = 64 * 1024,
        FailureMode = ToolOutputFailureMode.FailClosed,
        ContentRetrievalEnabled = true,
        RetrievalLimits = new ContentRetrievalLimits
        {
            MaxListResultsPerCall = 50,
            MaxReadBytesPerCall = 64 * 1024,
            MaxReadBytesPerTurn = 256 * 1024,
            MaxReadCallsPerTurn = 8,
            MaxListCallsPerTurn = 8
        }
    }
};
```

`AgentRunConfig.ToolOutputs` and subagent policies use nullable overrides.
`Enabled` controls new projection only. `ContentRetrievalEnabled` independently
controls access to existing and new references.

`FailClosed` returns a bounded `tool_output_too_large` or
`tool_output_projection_failed` result. `PreserveInline` is an explicit trusted
development escape hatch. The inline budget is a final delivery guard, not an
automatic offload selector.

## Sensitivity levels

`Public`, `Internal`, and `Restricted` classify how carefully content may be
disclosed. They do not replace session/thread authorization or tool
permissions. Bundle sensitivity is the strictest sensitivity of any included
part.

- `Public`: safe to disclose broadly; model-retrievable.
- `Internal`: ordinary application/session data and the default;
  model-retrievable.
- `Restricted`: sensitive financial, personal, proprietary, or diagnostic
  evidence; metadata may be inspected, but the body is not model-retrievable.

## Restricted disclosure policy (future work)

This release implements sensitivity classification and fail-closed handling,
but it does not implement an explicit policy that allows the model to retrieve
`Restricted` content bodies.

Today:

- `Public` and `Internal` content are model-retrievable.
- A bundle containing `Restricted` content is itself `Restricted`.
- The resulting `ContentReference` uses `InternalOnly` disposition.
- `Content.stat` may report metadata, but `Content.read` rejects the body.

Future work must add an explicit, auditable policy for relaxing that boundary.
It must not be encoded in `ContentReference`, which is an address rather than a
credential. At minimum, the policy design needs:

- trusted agent/run/subagent configuration;
- session and thread authorization;
- a maximum permitted sensitivity;
- action-level permission and invocation policy;
- an optional purpose, expiry, and retrieval budget;
- bounded durable audit events;
- interaction with redaction and application-owned artifact policy.

Until that policy exists, applications must keep genuinely restricted data
behind a separately governed application retrieval surface or a redacted
projection. Raising `ContentRetrievalEnabled` or changing the read budget does
not permit `Restricted` reads.

## Delivery and lifetime

Model-retrievable tool output stays symbolic and is never expanded by
compaction. Provider-materialized attachments use
`StoredContentReferenceContent` until `ContentLifecycleMiddleware` converts
them to valid `DataContent`, `HostedFileContent`, or HTTP(S) `UriContent` at the
provider boundary. Internal stored-content schemes are never sent to a
provider.

`ContentReference` reports disposition, persistence lifetime, sensitivity,
expiry, provenance, size, bundle digest, and per-part metadata. It is an
address, not authorization. Session-owned content is a conversational sidecar,
not an authoritative financial, audit, deployment, or compliance artifact.
