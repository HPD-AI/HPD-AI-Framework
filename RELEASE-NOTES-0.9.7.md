# HPD-Agent 0.9.7

HPD-Agent 0.9.7 continues pre-1.0 development. Public APIs, serialized events, and persistence formats may change before the 1.0 compatibility commitment.

## Highlights

- Debugging is built into the coding harness. Coding tools use HPD Environment sandbox and workspace bindings.
- Image tool results are preserved across provider requests, and thread history restoration and headless UI projections are improved.
- External function input contracts are preserved, including MCP and OpenAPI tool handling.
- Agent resources are public, and hosts can opt into configuration validation limits.
- Provider selection remains deferred until a run needs a client. Missing clients, models, local paths, credentials, and provider configuration now produce structured, provider-aware setup diagnostics.
- Agent Studio adds creation, configuration, inspection, lifecycle, and observability capabilities. Studio packages follow a separate release path.

## Client setup diagnostics

Provider validation now publishes stable `ProviderValidationIssue` values. Client resolution and authentication failures expose `ClientSetupDiagnostic` through typed exceptions and `MessageTurnErrorEvent.ClientSetup`. Hosted clients and the generated TypeScript SDK receive the same safe structured details. A direct `RunAsync(...)` call still throws the exception, so a direct host must catch `IClientSetupFailure` when it wants to continue.

## Sandbox migration

`WithSandbox(...)` is now part of `HPD-Agent.Framework`. Replace retired Agent sandbox packages with the framework and the appropriate Environment provider: `HPD-Environment.HostProcess`, `HPD-Environment.HostContainers`, or `HPD-Environment.Virtualization.Apple`. Configure the provider and workspace authorization in the host; runs can select an authorized workspace by ID. See the changelog for the package mapping.

