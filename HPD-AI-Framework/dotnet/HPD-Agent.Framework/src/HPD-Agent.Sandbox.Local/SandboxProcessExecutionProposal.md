# Sandbox Process Execution Proposal

## Summary

The local sandbox has strong policy and platform enforcement primitives. The
remaining design issue is where the sandbox binds to execution. A sandbox cannot
reliably protect process execution by inspecting arbitrary function arguments.
It must bind at the process-spawn boundary.

This proposal keeps `SandboxMiddleware` as the owner of sandbox runtime lifetime,
but moves the process boundary into a first-class runtime capability:
`ISandboxedProcessRunner`.

The desired final model is:

```text
builder config = global sandbox policy
attribute config = function/tool policy override
middleware = runtime lifecycle owner
process runner = actual spawn boundary
platform sandbox = OS implementation detail
```

In other words, `.WithSandbox(...)` should mean:

```text
this agent runtime has a sandboxed local process execution capability
```

not:

```text
middleware tries to guess and rewrite a command argument
```

The breaking design decision is intentional: process-capable tools must execute
through `ISandboxedProcessRunner`. Middleware argument rewriting is not part of
the target architecture.

## Goals

1. Make sandboxed process execution a first-class framework capability.
2. Let `SandboxMiddleware` own long-lived sandbox resources using runtime
   lifecycle hooks.
3. Let Bash, PowerShell, MCP process launchers, package-manager tools, test
   runners, and future tools execute through a shared sandbox runner.
4. Preserve `[Sandboxable]` as policy metadata and function/tool-level override
   configuration.
5. Remove command-string rewriting and command-argument guessing from the core
   sandbox path.
6. Fix the current config lifetime mismatch where `ISandbox` accepts per-call
   config but `SandboxManager` is effectively first-config-wins.
7. Improve developer experience so app authors enable sandboxing once and tool
   authors use a typed process API.

## Non-Goals

1. This proposal does not replace the existing Linux/macOS platform sandbox
   implementations.
2. This proposal does not require every tool to manually manage sandbox
   lifetime.
3. This proposal does not make `[Sandboxable]` the execution boundary.
4. This proposal does not preserve command-argument rewriting for backward
   compatibility.
5. This proposal does not attempt to solve remote/container sandboxing, though
   the interfaces should leave room for alternate implementations.

## Breaking Changes

This proposal intentionally breaks the old implicit sandboxing model.

Removed behavior:

- middleware does not infer process execution from function names
- middleware does not search for argument names like `command`, `cmd`, `shell`,
  `script`, or `bash`
- middleware does not replace function arguments with wrapped shell strings
- `[Sandboxable]` does not cause automatic process wrapping by itself
- public sandboxing documentation should not teach argument rewriting as a
  supported path

Required behavior:

- any tool that starts a process must call `ISandboxedProcessRunner`
- dangerous process tools such as Bash and PowerShell fail closed when the
  runner is unavailable, unless they expose an explicit unsandboxed mode
- builder configuration supplies global policy defaults
- `[Sandboxable]` supplies policy metadata and function/tool-level overrides
- runtime middleware owns sandbox lifetime and publishes the runner capability

The result is stricter, but easier to reason about: sandboxed process execution
is explicit in the tool implementation and centralized in one runner.

## Current State

### Runtime Hooks Already Exist

`IAgentMiddleware` already has runtime-level hooks:

```csharp
Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken);
Task AfterStartedAsync(AfterStartedContext context, CancellationToken cancellationToken);
Task BeforeStopAsync(BeforeStopContext context, CancellationToken cancellationToken);
Task AfterStoppedAsync(AfterStoppedContext context, CancellationToken cancellationToken);
```

`Agent.StartAsync` calls `BeforeStartAsync` before the runtime loop starts and
`AfterStartedAsync` after the loop is ready. `Agent.StopRuntimeAsync` calls
`BeforeStopAsync`, drains/stops runtime work, disposes registered resources, and
then calls `AfterStoppedAsync`.

`RuntimeHookContext` can register background tasks and disposables:

```csharp
context.RegisterBackgroundTask(...);
context.RegisterDisposable(...);
context.RegisterAsyncDisposable(...);
```

That means middleware is a natural owner for sandbox runtime resources:

- HTTP proxy
- SOCKS5 proxy
- TLS termination state
- MITM certificate authority
- violation drain tasks
- platform sandbox lifetime
- active process tracking
- cleanup and shutdown policy

### SandboxMiddleware Currently Has The Wrong Execution Boundary

`SandboxMiddleware` currently mixes two different concepts:

1. Runtime ownership: initialize platform sandbox, proxies, TLS material, and
   dependency checks.
2. Execution interception: inspect function arguments, guess which value is a
   command, replace it with a wrapped shell string, then call the original
   function.

That second responsibility is the fragile part. It means the sandbox is applied
at the function-call layer rather than at the actual process-spawn boundary.

The current path is effectively:

```text
function call
  -> middleware checks function metadata/name
  -> middleware guesses command argument
  -> middleware wraps string
  -> function maybe starts a process using that string
```

For a real Bash or PowerShell tool, this is backwards. The tool knows it is
about to start a process. The process runner must own the sandbox boundary.
Argument guessing should be removed rather than preserved as a second execution
model.

### ISandbox Wraps Commands But Does Not Run Processes

The public `ISandbox` shape currently wraps a command:

```csharp
Task<SandboxedCommand> WrapCommandAsync(
    string command,
    IEnumerable<string> args,
    SandboxConfig config,
    CancellationToken cancellationToken = default);
```

That is useful, but it is not a complete execution boundary. A process execution
boundary must own:

- executable and arguments
- shell text when appropriate
- working directory
- environment merging
- stdin
- stdout/stderr capture
- exit code
- timeout
- cancellation
- background execution
- PTY requirements
- active process cleanup
- process tree termination
- lifecycle events

### Config Lifetime Is Blurry

The public API implies per-call config:

```csharp
WrapCommandAsync(..., SandboxConfig config, ...)
```

but `SandboxManager` initializes platform sandbox state once. Later calls can
reuse the first initialized platform sandbox even if they pass a different
config. The target design should make this lifetime explicit in the
session/runner model instead of hiding it behind wrapping APIs.

The target model should separate:

```text
session-global infrastructure config
per-process effective policy
```

## Proposed Architecture

### High-Level Flow

```text
AgentBuilder.WithSandbox(globalConfig)
  -> registers SandboxMiddleware

SandboxMiddleware.BeforeStartAsync
  -> builds SandboxRuntimeSession
  -> validates dependencies
  -> starts shared proxies/TLS/platform support
  -> publishes ISandboxedProcessRunner
  -> registers session disposal with runtime context

BashTool / PowerShellTool / MCP launcher / test runner
  -> resolves ISandboxedProcessRunner from runtime capability context
  -> calls RunAsync with structured command and optional policy override

SandboxRuntimeSession / ISandboxedProcessRunner
  -> merges global config + attribute config + per-call override
  -> gets or creates compatible platform runtime
  -> wraps command using platform sandbox
  -> starts process
  -> captures output and tracks process
  -> reports violations/events

SandboxMiddleware.BeforeStopAsync / registered disposal
  -> drains or kills active sandboxed processes
  -> stops proxies
  -> disposes platform sandboxes
  -> emits final diagnostics
```

### Layering

```text
HPD.Agent.Sandbox
  SandboxConfig
  ISandboxedProcessRunner
  SandboxedProcessCommand
  SandboxedProcessOptions
  SandboxedProcessResult
  SandboxPolicyResolver abstractions

HPD.Agent runtime
  Runtime capability registry/accessor
  FunctionExecutionContext accessors

HPD.Agent.Sandbox.Local
  SandboxMiddleware
  SandboxRuntimeSession
  LocalSandboxedProcessRunner
  Linux/macOS/Windows platform integration
  Proxies/TLS/violation store/dependency checks
```

## New Public Contracts

### ISandboxedProcessRunner

```csharp
public interface ISandboxedProcessRunner
{
    Task<SandboxedProcessResult> RunAsync(
        SandboxedProcessCommand command,
        SandboxConfigOverride? configOverride = null,
        SandboxedProcessOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

This is the only API process-capable tools should use when sandboxing is
enabled.

### SandboxedProcessCommand

```csharp
public sealed record SandboxedProcessCommand
{
    public string? FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string? ShellCommand { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string?> Environment { get; init; } =
        new Dictionary<string, string?>();

    public bool UsesShell => ShellCommand is not null;

    public static SandboxedProcessCommand Shell(
        string command,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null);

    public static SandboxedProcessCommand Exec(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null);
}
```

There should be a clear distinction between shell text and structured argv.
Bash and PowerShell may use shell text. Most framework code should prefer
structured argv.

The command shape must be validated before execution:

- shell mode requires `ShellCommand` and must not set `FileName` or
  `Arguments`
- argv mode requires `FileName`, may set `Arguments`, and must not set
  `ShellCommand`
- callers should construct commands through `Shell(...)` or `Exec(...)`
  wherever possible

This avoids an ambiguous command object where both shell text and argv are set
and different layers disagree about which one is authoritative.

### SandboxedProcessOptions

```csharp
public sealed record SandboxedProcessOptions
{
    public string? StandardInput { get; init; }
    public TimeSpan? Timeout { get; init; }
    public bool CaptureStandardOutput { get; init; } = true;
    public bool CaptureStandardError { get; init; } = true;
    public bool MergeStandardError { get; init; }
    public bool AllowBackgroundExecution { get; init; }
    public bool KillProcessTreeOnCancel { get; init; } = true;
    public bool RequirePty { get; init; }
    public int? MaxCapturedBytesPerStream { get; init; }
    public TimeSpan OutputDrainTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
```

PTY support can be implemented after the first runner pass, but the option
should exist so Bash/interactive tools can express the need.

### SandboxedProcessResult

```csharp
public sealed record SandboxedProcessResult
{
    public required string ProcessId { get; init; }
    public int? SystemProcessId { get; init; }
    public int? ExitCode { get; init; }
    public required SandboxedProcessCompletionKind CompletionKind { get; init; }
    public required SandboxedProcessCapturedOutput Output { get; init; }
    public IReadOnlyList<SandboxViolation> Violations { get; init; } =
        Array.Empty<SandboxViolation>();
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();
}
```

The result should be suitable for Bash/PowerShell tools and for higher-level
tools that need exit status and captured output.

## Runtime Capability Registry

The current runtime contexts expose `Services`, but that is the existing
`IServiceProvider`. Middleware cannot safely add runtime-created objects to that
provider after the agent is built.

We need a runtime capability mechanism.

### Proposed API

```csharp
public interface IRuntimeCapabilityRegistry
{
    void Set<TCapability>(TCapability capability)
        where TCapability : notnull;

    bool TryGet<TCapability>(out TCapability capability)
        where TCapability : notnull;

    TCapability GetRequired<TCapability>()
        where TCapability : notnull;
}
```

`AgentRuntimeContext` would own one registry instance. Runtime and hook contexts
would expose it:

```csharp
public IRuntimeCapabilityRegistry RuntimeCapabilities { get; }
```

Function execution should also expose it:

```csharp
public IRuntimeCapabilityRegistry RuntimeCapabilities { get; }
```

Convenience extension methods can provide terse DX:

```csharp
public static ISandboxedProcessRunner GetSandboxedProcessRunner(
    this FunctionExecutionContext context);

public static TCapability GetRequiredRuntimeCapability<TCapability>(
    this FunctionExecutionContext context)
    where TCapability : notnull;
```

### Why Not Just DI?

DI is still useful for construction-time services. Runtime capabilities are
different:

- they are created when an agent runtime starts
- they are disposed when that runtime stops
- they may contain runtime IDs, event coordinators, cancellation tokens, and
  active process state
- middleware should be able to publish them after startup validation

That lifecycle does not fit a static build-time `IServiceProvider` cleanly.

## SandboxRuntimeSession

Add a local implementation object that owns shared sandbox infrastructure.

```csharp
internal sealed class SandboxRuntimeSession : IAsyncDisposable
{
    public SandboxConfig GlobalConfig { get; }
    public ISandboxedProcessRunner ProcessRunner { get; }

    public Task StartAsync(CancellationToken cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken);
}
```

Responsibilities:

- resolve TLS termination configuration
- create and start HTTP proxy when network filtering requires it
- create and start SOCKS5 proxy on Linux when needed
- create platform sandbox implementations
- run dependency checks
- create violation stores/drain tasks
- expose process runner
- track active process handles
- stop/kill active processes during runtime shutdown
- dispose proxies/platform sandboxes/cert material

## LocalSandboxedProcessRunner

`LocalSandboxedProcessRunner` is the concrete process execution boundary for
the local sandbox package.

Responsibilities:

1. Resolve the effective policy:

   ```text
   global sandbox config
   + generated/attribute function config if present
   + explicit per-call config override
   = effective sandbox config
   ```

2. Choose compatible runtime resources.

   Some policies can share session-level infrastructure. Others require a
   separate invocation runtime.

3. Wrap the command using platform sandbox primitives.

4. Start the process with `ProcessStartInfo`.

5. Apply cwd/env/stdin/stdout/stderr/timeout/cancellation behavior.

6. Track the process until it exits.

7. Kill process tree on cancellation/disposal when configured.

8. Drain and attach sandbox violations to the result.

9. Emit structured sandbox process events.

### Sharing Versus Per-Invocation Runtime

Not every config difference should force a full runtime rebuild. The session
should classify policy differences into two buckets:

#### Process-local Differences

These can often be applied per command:

- working directory
- environment
- allow/write path additions when platform wrapping can emit per-process policy
- pty option
- command timeout
- stdin/stdout behavior

#### Infrastructure Differences

These may require a distinct runtime or proxy:

- network mode changes
- allowed/denied domain changes
- request filter changes
- parent proxy changes
- TLS termination/MITM changes
- external MITM socket changes

The runner should use a cache keyed by a stable infrastructure config key, not
the full `SandboxConfig`. This avoids recreating proxies for every command while
also fixing first-config-wins.

## Policy Resolution

Add a policy resolver that can be used by middleware and process runner.

```csharp
public interface ISandboxPolicyResolver
{
    SandboxConfig Resolve(
        SandboxConfig globalConfig,
        SandboxConfigOverride? functionOverride,
        SandboxConfigOverride? callOverride);
}
```

Initial merge semantics:

- scalar values from more-specific config override less-specific config when
  explicitly set
- denied lists append and retain deny precedence at evaluation time
- filesystem allow lists append by default, because tool-specific writable
  paths usually narrow the practical working set without removing base temp
  paths
- network allow lists should prefer replacement by the most-specific explicit
  config, because appending allowed domains can accidentally broaden egress
- explicit append/replace modes can be added later for advanced policy authors
- network mode from the most-specific config wins when specified
- TLS termination and MITM config remain mutually exclusive
- validation runs after merge

Precedence:

```text
per-call override
  > [Sandboxable] / generated function metadata
  > builder global config
  > defaults
```

## SandboxMiddleware Refactor

### Current Role

Current middleware:

- lazily initializes in `BeforeMessageTurnAsync`
- blocks functions after violations
- wraps function calls by guessing command argument names
- drains violation events

### Proposed Role

Future middleware:

- initializes `SandboxRuntimeSession` in `BeforeStartAsync`
- publishes `ISandboxedProcessRunner` as a runtime capability
- registers session disposal with runtime context
- keeps violation/blocking behavior
- does not sandbox process execution by mutating function arguments

### New Lifecycle

```csharp
public async Task BeforeStartAsync(
    BeforeStartContext context,
    CancellationToken cancellationToken)
{
    var session = new SandboxRuntimeSession(_config, context, _logger);
    await session.StartAsync(cancellationToken);

    context.RuntimeCapabilities.Set<ISandboxedProcessRunner>(
        session.ProcessRunner);

    context.RegisterAsyncDisposable(session);
}
```

If startup fails:

- `DependencyFailureBehavior.Block` should cancel runtime start.
- `DependencyFailureBehavior.Warn` should emit warnings and either install a
  no-op/unsandboxed runner or avoid publishing the runner.
- Windows fallback behavior should remain explicit.

### Removed Execution Interception

`WrapFunctionCallAsync` should not be the sandbox execution path. A function
that starts a process must call `ISandboxedProcessRunner`. If it does not call
the runner, the sandbox should not pretend that process execution was protected.

The middleware may still use function hooks for policy state, violation
blocking, diagnostics, and metadata capture, but it should not guess command
argument names or rewrite shell strings.

## Attribute And Source Generator Semantics

`[Sandboxable]` remains valuable, but its meaning becomes policy metadata:

```csharp
[Sandboxable(
    NetworkMode = SandboxNetworkMode.Blocked,
    AllowWrite = ["./tmp"],
    DenyRead = ["~/.ssh"])]
```

It should not imply that middleware must guess and rewrite arguments.

Generated function metadata should continue to emit sandbox configuration data.
The runner/policy resolver should be able to retrieve the function-level config
for the current invocation and merge it into the effective policy.

Optional follow-up attributes can make process-shaped APIs more descriptive:

```csharp
[ShellCommandParameter]
[ExecutableParameter]
[ArgumentsParameter]
```

These attributes would describe process-shaped parameters for documentation,
analysis, or source-generated helpers. They should not reintroduce middleware
argument rewriting as an execution mechanism.

## Developer Experience

### App Author

```csharp
var agent = new AgentBuilder()
    .WithSandbox(config => config with
    {
        NetworkMode = SandboxNetworkMode.Filtered,
        AllowedDomains = ["github.com", "nuget.org", "npmjs.org"],
        DenyRead = ["~/.ssh"],
        DenyWrite = [".git/hooks", ".env"]
    })
    .WithTool<BashTool>()
    .WithTool<PowerShellTool>()
    .Build();
```

The app author turns sandboxing on once.

### Tool Author

```csharp
public sealed class BashTool
{
    public async Task<BashResult> RunAsync(
        string command,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var runner = context.GetRequiredRuntimeCapability<ISandboxedProcessRunner>();

        var result = await runner.RunAsync(
            SandboxedProcessCommand.Shell(command),
            cancellationToken: cancellationToken);

        return BashResult.FromProcessResult(result);
    }
}
```

### Tool-Level Policy Override

```csharp
[Sandboxable(
    NetworkMode = SandboxNetworkMode.Blocked,
    AllowWrite = ["./tmp"],
    DenyRead = ["~/.ssh", ".env"])]
public async Task<BashResult> RunLocalOnlyBuild(
    string command,
    FunctionExecutionContext context,
    CancellationToken cancellationToken)
{
    var runner = context.GetRequiredRuntimeCapability<ISandboxedProcessRunner>();

    var result = await runner.RunAsync(
        SandboxedProcessCommand.Shell(command),
        cancellationToken: cancellationToken);

    return BashResult.FromProcessResult(result);
}
```

### Explicit Per-Call Override

```csharp
var result = await runner.RunAsync(
    SandboxedProcessCommand.Shell("dotnet test"),
    configOverride: new SandboxConfigOverride
    {
        NetworkMode = SandboxNetworkMode.Blocked,
        AllowWrite = ["./artifacts", "./TestResults"]
    },
    options: new SandboxedProcessOptions
    {
        Timeout = TimeSpan.FromMinutes(5),
        MaxCapturedBytesPerStream = 2_000_000
    },
    cancellationToken);
```

### Required Sandbox Behavior

Tools can decide whether sandbox is required:

```csharp
if (!context.RuntimeCapabilities.TryGet<ISandboxedProcessRunner>(out var runner))
    throw new InvalidOperationException("This tool requires .WithSandbox(...).");
```

For dangerous tools like Bash and PowerShell, the recommended default should be
fail-closed unless the tool has an explicit unsandboxed mode.

## Events And Observability

Add process-level sandbox events:

- `SandboxProcessStartingEvent`
- `SandboxProcessStartedEvent`
- `SandboxProcessCompletedEvent`
- `SandboxProcessFailedEvent`
- `SandboxProcessTimedOutEvent`
- `SandboxProcessCancelledEvent`
- `SandboxProcessKilledEvent`

Useful event fields:

- runtime ID
- function call ID when available
- function name when available
- command kind: shell or argv
- redacted executable/arguments
- working directory
- effective network mode
- proxy ports
- platform
- exit code
- duration
- timeout
- violation count

Do not emit full command text by default if it can contain secrets. Provide a
redaction strategy.

## Security Properties

The new boundary improves safety because:

1. Process tools no longer depend on middleware guessing parameter names.
2. Structured argv can remain structured until the platform layer absolutely
   needs shell rendering.
3. Working directory and environment are controlled by the runner.
4. Cancellation and disposal can kill the active process tree.
5. Policy merge and validation happen before process start.
6. Per-process violations can be attached to the result and emitted.
7. All local process-capable tools converge on the same security primitive.

## Migration Plan

### Phase 1: Runtime Capability Registry

- Add `IRuntimeCapabilityRegistry`.
- Add a registry instance to `AgentRuntimeContext`.
- Expose the registry on runtime hook contexts.
- Expose the registry on function execution contexts.
- Add convenience extension methods for required capability lookup.
- Add tests for capability publication, lookup, overwrite behavior, and runtime
  isolation.

### Phase 2: Public Process Contracts

- Add `ISandboxedProcessRunner`.
- Add `SandboxedProcessCommand`.
- Add `SandboxedProcessOptions`.
- Add `SandboxedProcessResult`.
- Add basic tests for shell/argv command construction and validation.

### Phase 3: SandboxRuntimeSession

- Extract sandbox startup/cleanup from `SandboxMiddleware` into
  `SandboxRuntimeSession`.
- Move initialization from `BeforeMessageTurnAsync` to `BeforeStartAsync`.
- Register session disposal with runtime context.
- Preserve dependency behavior and sandbox initialization events.
- Add start/stop lifecycle tests.

### Phase 4: LocalSandboxedProcessRunner

- Implement process start/capture/timeout/cancellation.
- Track active processes.
- Kill active process trees on cancellation/disposal.
- Use platform wrapping before process start.
- Attach violations to results.
- Add unit tests with fake platform sandbox.
- Add conditional integration tests on macOS/Linux.

### Phase 5: Policy Resolver And Config Lifetime Fix

- Add explicit policy resolver.
- Add effective config merge tests.
- Add infrastructure config keying/caching.
- Ensure different per-call policies do not silently reuse incompatible
  first-config-wins state.

### Phase 6: Tool Migration

- Update Bash tool to use `ISandboxedProcessRunner`.
- Update PowerShell tool to use `ISandboxedProcessRunner`.
- Update MCP process launch paths where appropriate.
- Update package-manager/test-runner/local-server helpers when they exist.

### Phase 7: Remove Argument-Rewriting Sandbox Path

- Remove command-key guessing from `SandboxMiddleware`.
- Remove function argument mutation for sandbox execution.
- Remove tests that assert automatic command-string wrapping.
- Add tests that process-capable tools fail closed when the runner is missing.
- Add diagnostics that identify process-capable tools that did not resolve a
  sandbox runner when sandboxing is required.

## Test Plan

### Runtime Capability Tests

- capability can be published in `BeforeStartAsync`
- function context can resolve runtime capability
- runtime stop disposes registered capability/session
- separate agent runtimes do not share capabilities
- missing required capability throws useful error

### Process Runner Unit Tests

- shell command runs with captured stdout/stderr
- argv command preserves spaces and quotes
- working directory is applied
- environment variables are merged and removed correctly
- stdin is passed
- timeout kills process
- cancellation kills process
- disposal kills active process
- max output limit is enforced
- result includes exit code and output

### Sandbox Policy Tests

- global config applies to command
- attribute config overrides global config
- per-call config overrides attribute/global config
- deny lists preserve deny precedence
- incompatible network config uses distinct infrastructure
- validation happens after merge

### Middleware Lifecycle Tests

- `BeforeStartAsync` starts session once
- `BeforeStartAsync` publishes runner
- dependency failure blocks or warns according to config
- `BeforeStopAsync` drains/kills active processes
- registered disposal tears down session
- middleware does not rewrite function command arguments
- process-capable tools fail closed when no runner is available

### Platform Integration Tests

- Linux blocked network command cannot curl external host
- Linux filtered network command succeeds through proxy for allowed host
- Linux filtered network command fails for denied host
- Linux deny read/write policies apply to runner-started process
- Linux seccomp Unix socket policy applies to runner-started process
- macOS blocked sensitive path read fails
- macOS filtered network direct bypass is denied
- macOS proxy allowed/denied behavior applies to runner-started process
- sandbox stop kills long-running runner-started process

## Open Questions

1. Should runtime capabilities be mutable after `AfterStartedAsync`, or should
   registration close once startup completes?
2. Should `ISandboxedProcessRunner` live in `HPD.Agent.Sandbox` or a more
   general `HPD.Agent.Processes` namespace with sandbox as one policy provider?
3. Should Bash/PowerShell fail closed when no runner is available, or support an
   explicit unsandboxed mode?
4. Should path/domain list merge behavior eventually expose explicit
   append/replace operators, or are the initial deny-append, filesystem-append,
   network-replace defaults enough?
5. How much command text should be emitted in observability events by default?
6. Should PTY support be first implementation phase or a follow-up?
7. Should platform wrappers continue rendering structured commands to shell
   text, or should Linux/macOS implementations preserve argv deeper?

## Recommended Decisions

1. Runtime capability registration should be allowed during startup and locked
   after `AfterStartedAsync` unless explicitly marked dynamic.
2. `ISandboxedProcessRunner` should live in `HPD.Agent.Sandbox` initially
   because its behavior is policy-coupled.
3. Bash and PowerShell should fail closed unless explicitly configured for
   unsandboxed execution.
4. Per-call scalar config should override. Deny lists should append with deny
   precedence. Filesystem allow lists should append. Network allow lists should
   replace when a more-specific config explicitly supplies them. Later, add
   explicit append/replace semantics if needed.
5. Events should redact command text by default and include opt-in debug
   metadata only when configured.
6. PTY should be represented in the contracts now, but implemented after the
   initial non-PTY runner.
7. Structured argv should be preserved as far as possible. Shell rendering
   should be the escape hatch, not the default for every caller.

## Final Target

The end state should feel simple:

```csharp
var agent = new AgentBuilder()
    .WithSandbox(config => config with
    {
        NetworkMode = SandboxNetworkMode.Filtered,
        AllowedDomains = ["github.com", "nuget.org"]
    })
    .WithTool<BashTool>()
    .Build();
```

And inside any process-capable tool:

```csharp
var runner = context.GetRequiredRuntimeCapability<ISandboxedProcessRunner>();

var result = await runner.RunAsync(
    SandboxedProcessCommand.Shell(command),
    cancellationToken: cancellationToken);
```

The app author turns sandboxing on once. The middleware owns runtime lifetime.
The tool uses a typed execution boundary. The platform sandbox remains an
implementation detail.
