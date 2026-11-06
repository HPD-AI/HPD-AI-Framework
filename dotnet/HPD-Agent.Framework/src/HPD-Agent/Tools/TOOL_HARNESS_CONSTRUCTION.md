# ToolHarness construction

HPD constructs a ToolHarness once when an Agent is built and reuses that
instance for every run of the Agent. Construction is generated at compile time:
HPD does not select constructors by reflection or repair an invalid service
graph at runtime.

## Constructor contract

A concrete ToolHarness must expose exactly one public instance constructor.

Supported constructor parameters are:

- `IHarnessConfig<T>` for typed harness settings.
- Required reference-type services resolved through the construction context.
- Optional reference-type services with their declared fallback.
- Optional primitives or enums with their declared fallback.
- `ISecretResolver` as a normal reference-type service.

Required value types and `ref`, `in`, `out`, pointer, or inaccessible parameter
types are not supported. Values such as an `int` belong in `IHarnessConfig<T>`,
not in the service constructor.

The generator emits one direct constructor call. The same contract is used for
Native AOT.

## Define settings

Use a deeply immutable record with a parameterless default contract:

```csharp
public sealed record SearchSettings
{
    [HarnessRunSetting]
    public int MaxResults { get; init; } = 10;
}
```

Settings may contain supported scalar values, nested immutable records,
`ImmutableArray<T>`, and `ImmutableDictionary<string,TValue>`.

`[HarnessRunSetting]` opts a property into per-run overrides. Unmarked properties
are construction-only.

## Declare a ToolHarness

```csharp
public sealed class SearchHarness(
    IHarnessConfig<SearchSettings> settings,
    ISearchClient client)
{
    [AIFunction]
    public async Task<string> Search(
        string query,
        FunctionExecutionContext context)
    {
        var effective = settings.For(context);
        return await client.SearchAsync(query, effective.MaxResults);
    }
}
```

The settings handle is valid only for the exact built Agent and harness owner
that created it.

## Register and configure

```csharp
var agent = await new AgentBuilder(agentConfig)
    .WithToolHarness<SearchHarness>(options =>
        options.Configure(new SearchSettings
        {
            MaxResults = 20
        })
        .UseDependency(searchClient))
    .BuildAsync();
```

Dependency precedence is:

```text
UseDependency
-> application DI
-> generated dependency default
-> missing-dependency error
```

Application constructors are Agent-scoped. Do not inject a service tied to one
accepted input. Per-input services belong in execution-owned ToolHarness
middleware or `FunctionExecutionContext.Services`.

## Declare a dependency default

```csharp
[ToolHarnessDependency(
    typeof(ISearchClient),
    typeof(DefaultSearchClient))]
public sealed class SearchHarness(
    IHarnessConfig<SearchSettings> settings,
    ISearchClient client)
{
}
```

The implementation must be concrete, accessible, assignable to the declared
service, and expose one supported public constructor. A service may have only
one default in a base/derived harness hierarchy.

## Per-run overrides

```csharp
var runConfig = new AgentRunConfig();

runConfig.Harnesses
    .For<SearchHarness>()
    .Override(new SearchSettingsOverrides
    {
        MaxResults = 5
    });

var result = await agent.RunAsync(
    "Find the latest filing",
    runConfig: runConfig);
```

Omitted fields preserve their resolved value. Nested settings merge. Arrays and
dictionaries replace the complete collection. A reset restores the library
default:

```csharp
runConfig.Harnesses
    .For<SearchHarness>()
    .Override(new SearchSettingsOverrides(), "/maxResults");
```

The portable JSON/YAML form uses the same rules:

```yaml
harnesses:
  - name: SearchHarness
    config:
      maxResults: 5
```

## Supplied instances

To retain caller-owned construction state, register an existing instance:

```csharp
var harness = new ReportingHarness(existingDatabase);
builder.WithToolHarness(harness);
```

HPD uses and does not dispose the supplied object. Construction configuration,
dependency replacement, and generated per-run overrides are unavailable for a
supplied instance because HPD did not create or bind it.

## Ownership

| Acquisition | Disposal owner |
| --- | --- |
| HPD-created ToolHarness | Built Agent |
| HPD-created dependency default | Built Agent |
| Application DI service | Application provider |
| `UseDependency(instance)` | Caller |
| `WithToolHarness(instance)` | Caller |

Failed Agent construction rolls back HPD-created objects. Constructors that
allocate internal resources and then throw must clean up their own partial
state because HPD never receives the failed object.

## Compile-time diagnostics

```text
HPDHIN009  No public constructor
HPDHIN010  Multiple public constructors
HPDHIN011  Unsupported constructor parameter
HPDHIN012  Invalid dependency default
HPDHIN013  Duplicate dependency default
```

These are errors. They identify the harness, constructor, parameter, or
dependency declaration that must change.

## Migration

- Consolidate multiple public constructors into one.
- Make helper constructors non-public.
- Replace a plain `SomeConfig` constructor parameter with
  `IHarnessConfig<SomeConfig>`.
- Move required primitive values into harness settings.
- Move per-input services into execution middleware.
- Register a supplied instance when the application owns construction.

There is no constructor-selection attribute, reflection fallback, or
compatibility mode.
