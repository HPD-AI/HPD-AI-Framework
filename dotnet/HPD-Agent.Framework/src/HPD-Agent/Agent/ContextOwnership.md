# Invocation context ownership

`AgentRunConfig.Context` is captured before invocation by the same Agent-owned
boundary used by MultiAgent. Capturing a dictionary now captures its supported
data recursively. Arbitrary objects no longer retain implicit shared identity.

Supported data consists of null, strings, booleans, characters, numeric primitives,
enums, GUIDs, date/time values, time spans, URIs, `JsonElement`, `JsonNode`,
string-keyed object dictionaries, string dictionaries, one-dimensional zero-based
arrays of supported values, `List<object>`, and `List<string>`. Dictionary and list
containers are independently owned; JSON elements survive disposal of their source
document. Cycles and more than 64 nested containers fail capture. Other collection
shapes need an explicit snapshot contract rather than reflection-based cloning.

Custom data implements `IAgentContextSnapshotValue`. Each call returns a fresh
value of the same type with independently owned nested mutable data. It must be
safe to capture the result again. Snapshot implementations must not acquire
disposable resources. Fully immutable values may instead implement
`IAgentImmutableContextValue`; this asserts immutability of the entire reachable
value, not just its outer record or read-only interface.

Tool metadata follows the same rule. Mutable metadata must provide a snapshot;
immutable metadata can declare immutability. A host deliberately sharing a service
lists its property or tool key in `SharedProperties` or `SharedToolInstances`:

```csharp
var context = new AgentContextRunConfig
{
    Properties = new Dictionary<string, object>
    {
        ["reviewId"] = "review-001",
        ["repository"] = repositoryClient
    },
    SharedProperties = new HashSet<string> { "repository" }
};
```

Sharing declarations are runtime-only and cannot grant authority through workflow
configuration. The host owns the shared instance's lifetime and disposal, guarantees
concurrent use is supported, and must not store mutable per-node permissions or
scratch state in it. Captures preserve its identity and copy the sharing declaration.
They do not dispose borrowed services.

An `AgentWorkspace` implements the snapshot contract and copies its root collection.
This isolates configuration data; workspace authorization and physical filesystem
enforcement remain separate contracts.

Unsupported ownership fails with `AgentRunConfigurationException` and a context
member path. Custom snapshot exceptions are redacted to avoid exposing context
contents. There is no switch to retain the previous implicit-sharing behavior.
