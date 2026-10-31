# HPD-Agent.MultiAgent

Fluent multi-agent orchestration library built on HPD-Graph. Zero-ceremony agent workflows with unified event streaming.

## Install

```bash
dotnet add package HPD-Agent.MultiAgent
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Agent configuration files

Workflow JSON and YAML can embed an `agent` configuration or reference an ordinary
agent JSON/YAML file with `agentFile`. Specify exactly one source per node:

```yaml
name: Evidence review
agents:
  architecture:
    agentFile: agents/architecture.json
  configuration:
    agentFile: agents/configuration.yaml
settings:
  maxConcurrency: 3
```

Use `MultiAgent.FromFile(path)`, `MultiAgentFactory.BuildFromFileAsync(path)`, or
`MultiAgentConfigSerializer.ReadFile[Async](path)`. Relative agent paths resolve
against the workflow file's directory. References are resolved at load time;
exported workflow snapshots contain the resolved agent configurations. The plain
text deserializer does not have a file base path and does not resolve references.
Malformed, missing, or ambiguous sources fail rather than falling back to defaults.
`settings.maxConcurrency` bounds concurrently executing nodes in each graph layer.

## Structured answers from configuration

A structured node names a concrete host-owned output class using its
assembly-qualified name. For example:

```yaml
name: Evidence review
agents:
  reviewer:
    agentFile: agents/question-reviewer.yaml
    outputMode: Structured
    structuredOutputType: "MyApp.QuestionAnswer, MyApp"
    structuredOutputMode: Native
```

`structuredOutputMode` is `Native` (provider JSON-schema response format) or
`Tool` (an auto-generated answer-submission tool). Neither requires filesystem
write/edit tools. The host saves the returned answer. Missing, unknown, or
non-concrete output types fail during workflow configuration. A response that
produces no final typed result fails the node rather than accepting empty output.
The output type and mode are retained in exported workflow snapshots.

Type names are trusted application configuration, not model-selected input.
The CLR type and its serialization metadata must be available in the host.
The host event composition must also register the closed
`StructuredResultEvent<QuestionAnswer>` contract for typed-result journal
persistence (including ephemeral execution). Reuse that codec in the session
store; configuring a type name does not install application event contracts.

dynamic type lookup and generic execution require explicit preservation when
trimming and are not a guarantee of Native AOT support for arbitrary YAML types.

## Per-node run configuration

Reuse one agent definition and vary serializable run settings at the node:

```yaml
agents:
  reviewer:
    agentFile: agents/reviewer.yaml
    runConfig:
      systemInstructions:
        append: Review only the assigned question and supplied evidence.
      context:
        properties:
          reviewId: review-001
```

`runConfig` uses the Agent-owned `AgentRunConfig` serialization and snapshot path.
Live clients, delegates, event codecs, secrets, capability ceilings, and other
runtime-only values remain host-owned. Strict parsing rejects retired
`additionalInstructions` and unknown run members. Provider-specific client
payloads require the host's generated `ProviderComposition`.

## Trusted host binding

Create one `WorkflowHostBinding` for provider composition, event composition,
session storage, node type resolution, host authorization, and attempt-owned
resources:

```csharp
var host = new WorkflowHostBinding(
    providerComposition,
    eventComposition,
    sessionStore,
    hostSecurity);

var workflow = MultiAgent.FromFile("review-workflow.yaml", host);
```

The session store must use the exact host event codec. Override
`BindAttemptAsync` when an application needs a node/attempt-owned resource; the
returned `WorkflowRunBinding` is disposed after each attempt, including failure and
cancellation. Declarative capability requests can narrow the host ceiling but
cannot expand it.

## Failures and retries

`Stop`, `Isolate`, `SkipDependents`, and `Fallback` map to Graph-owned execution
semantics. A fallback node is recovery-only, has an explicit output contract, and
declares what happens if recovery exhausts:

```csharp
primary.OnErrorFallback("backup", ErrorMode.Isolate);
backup.RecoveryOnly = true;
```

Primary and recovery retry budgets are independent. `OnlyTransient` and
`RetryableExceptionTypeNames` are independent filters and round-trip through
configuration. Cancellation is never retried. Collectors can inspect
`HandlerInputs.SourceResults` to summarize isolated failures without treating a
failed node as successful output.

## Workspace access

Every workspace root states its access explicitly:

```json
{
  "version": 1,
  "defaultRootId": "outputs",
  "roots": [
    { "id": "evidence", "path": "/mounted/evidence", "access": "readOnly" },
    { "id": "outputs", "path": "/mounted/outputs", "access": "readWrite" }
  ]
}
```

Read-only roots receive no write grant. Write escalation cannot cross a read-only
root, and read-only/writable workspace roots may not overlap. Paths are canonicalized
through existing symlink ancestors before containment and authorization checks.
Host capability ceilings remain authoritative.
