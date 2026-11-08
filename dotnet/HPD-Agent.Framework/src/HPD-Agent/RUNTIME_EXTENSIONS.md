# Runtime Agent Extensions

Runtime extensions add tenant/application-owned event and input schemas without
recompiling C# event classes. The framework does not use runtime Roslyn
compilation.

## Registration

```csharp
var schemaStore = new FileRuntimeAgentExtensionSchemaStore(
    "/var/lib/hpd/runtime-schemas");
var registry = new RuntimeAgentExtensionRegistry(store: schemaStore);
services.AddHpdRuntimeAgentExtensions(registry, schemaStore);
services.AddHpdRuntimeAgentExtensionHydration();

// The hosted service rehydrates durable schema declarations before traffic.

var schema = registry.Register(new RuntimeAgentExtensionSchema
{
    Namespace = "tenant.acme.analysis",
    Name = "progress",
    Version = 1,
    Usage =
        RuntimeAgentExtensionUsage.LiveOutput |
        RuntimeAgentExtensionUsage.DurableOutput |
        RuntimeAgentExtensionUsage.Input,
    PayloadSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "required": ["percent"],
          "properties": {
            "percent": {
              "type": "integer",
              "minimum": 0,
              "maximum": 100
            }
          },
          "additionalProperties": false
        }
        """).RootElement.Clone()
});
```

Namespaces outside `hpd.agent.*` and `hpd.core.*` are required. Schema versions
are immutable; publish a new version instead of replacing an existing one.

## Output events

Create and publish a validated live event:

```csharp
await context.PublishAsync(
    registry.CreateLiveEvent(
        schema.Key,
        JsonSerializer.SerializeToElement(new { percent = 40 })),
    cancellationToken);
```

Use `CreateDurableEvent` when the event belongs in replayable thread history.
Durable and live envelopes have stable framework discriminators:

- `RUNTIME_AGENT_LIVE_EVENT`
- `RUNTIME_AGENT_DURABLE_EVENT`
- `RUNTIME_AGENT_INPUT`

The envelope carries the extension namespace, schema name, version, schema
digest, and JSON payload. SDK clients can retain the payload as JSON or decode
it through a tenant-owned model.

## Input events

Attach one trusted handler to each input schema:

```csharp
registry.RegisterInputHandler(
    schema.Key,
    new AnalysisProgressHandler());
```

```csharp
public sealed class AnalysisProgressHandler : IRuntimeAgentInputHandler
{
    public ValueTask<RuntimeAgentInputHandlingResult> HandleAsync(
        RuntimeAgentInputEvent input,
        RuntimeAgentInputContext context,
        CancellationToken cancellationToken)
    {
        var percent = input.Payload.GetProperty("percent").GetInt32();
        // Validate authority and perform trusted application work.
        return ValueTask.FromResult(RuntimeAgentInputHandlingResult.Success);
    }
}
```

`RuntimeAgentInputEvent` is the only public runtime envelope. A matching schema,
digest, and registered handler are all required before dispatch.

## Validation and limits

Validation uses the standards-based `JsonSchema.Net` evaluator, including
composition, conditionals, local definitions, recursive local references, and
format validation. Runtime schemas may use only same-document `$ref` fragments;
external references are rejected to prevent registration-time network access.

Defaults include a 256 KiB schema, 64 KiB payload, depth 16, 2,048 nodes, 128
properties, 512 array items, and 32 KiB strings.

## Discovery

When runtime extensions are mapped, HPD exposes:

```text
GET /api/hpd-agent/runtime-extension-catalog
```

The catalog contains schema identities, usage flags, descriptions, payload
digests, the complete registered payload schemas, and one deterministic catalog
digest. Runtime schema registration does
not mutate the immutable generated event/input composition; it extends the
envelope vocabulary while preserving generated core contracts.

## Persistence

`FileRuntimeAgentExtensionSchemaStore` writes each immutable schema version to
an atomically replaced JSON file. `HydrateAsync` re-registers those declarations
at startup. Payload schemas and event envelopes therefore remain understandable
across process restarts. Trusted input handlers are code and must also be
registered again during host startup.

## Authorized schema management

HTTP schema registration is opt-in and disabled by default:

```csharp
app.MapGroup("/api/hpd-agent").MapHPDAgentApi(options =>
{
    options.MapRuntimeExtensionManagement = true;
});
```

Enabling the route without an
`IRuntimeAgentExtensionMutationAuthorizer` returns 403. The authorizer can
inspect the authenticated tenant and the requested namespace.

```csharp
public sealed class TenantSchemaAuthorizer
    : IRuntimeAgentExtensionMutationAuthorizer
{
    public ValueTask<RuntimeAgentExtensionAuthorizationResult> AuthorizeAsync(
        HttpContext context,
        RuntimeAgentExtensionSchema schema,
        CancellationToken cancellationToken)
    {
        var allowed =
            context.User.Identity?.IsAuthenticated == true &&
            schema.Namespace.StartsWith(
                $"tenant.{context.User.FindFirst("tenant")?.Value}.",
                StringComparison.Ordinal);

        return ValueTask.FromResult(
            allowed
                ? new RuntimeAgentExtensionAuthorizationResult(true)
                : RuntimeAgentExtensionAuthorizationResult.Deny(
                    "The authenticated tenant does not own this namespace."));
    }
}
```

Authorized requests use:

```text
POST /api/hpd-agent/runtime-extension-schemas
```

The HTTP route permits live and durable output schemas. Input schemas remain
host-registered so that the schema and trusted handler cannot be separated by an
untrusted API call.

## Typed SDK payloads

Capture the catalog response during SDK generation:

```bash
curl http://127.0.0.1:5188/api/hpd-agent/runtime-extension-catalog \
  > runtime-extension-catalog.json

dotnet run --project HPD-Agent.ClientGenerator -- \
  --input hpd-agent-v1.openapi.json \
  --runtime-catalog runtime-extension-catalog.json \
  --output generated/My.Client \
  --namespace My.Client
```

Object payload schemas become generated `HpdRuntimeExtensionPayload` records.
The generated codec works across runtime live, durable, and input envelopes:

```csharp
if (HpdRuntimeExtensionPayloadCodec.Decode(runtimeEvent)
    is RuntimePayloadTenantAcmeAnalysisProgressV1 progress)
{
    Console.WriteLine(progress.Percent);
}

var input = HpdRuntimeExtensionPayloadCodec.CreateInput(progress);
```

Payload schemas that are not top-level objects remain available as validated
`JsonElement`; the generator deliberately avoids synthesizing misleading CLR
aliases for them.

