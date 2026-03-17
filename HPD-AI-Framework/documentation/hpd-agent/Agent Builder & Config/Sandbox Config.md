# Sandbox Config

`SandboxConfig` defines the security sandbox applied to tool execution. When a sandbox is active, the agent's tools are constrained in what they can read, write, and connect to — protecting the host system from unintended or malicious tool behaviour.

The sandbox is applied per-agent and enforces restrictions at the OS level where supported.

---

## Static Presets

Four preset configs are provided for common scenarios. Prefer a preset as a starting point and adjust from there.

```csharp
// Default — reasonable write allow-list, SSH/AWS credential deny-list
var sandbox = SandboxConfig.CreateDefault();

// Permissive — minimal restrictions, suitable for trusted tool environments
var sandbox = SandboxConfig.CreatePermissive();

// MCP-specific defaults
var sandbox = SandboxConfig.CreateForMCP();

// Tightened defaults with stricter rules
var sandbox = SandboxConfig.CreateEnhanced();

// Enhanced + MCP
var sandbox = SandboxConfig.CreateEnhancedForMCP();
```

---

## Properties

### File System Access

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AllowWrite` | `string[]` | `[".", "/tmp"]` | Paths where tools are allowed to write |
| `DenyRead` | `string[]` | `["~/.ssh", "~/.aws", "~/.gnupg"]` | Paths that tools are never allowed to read |
| `DenyWrite` | `string[]` | `[]` | Paths that tools are never allowed to write |

### Network Access

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AllowedDomains` | `string[]?` | `[]` | Domains tools are permitted to connect to. Empty = all allowed |
| `DeniedDomains` | `string[]` | `[]` | Domains explicitly blocked |
| `ExternalHttpProxyPort` | `int?` | `null` | Route HTTP through an external proxy on this port |
| `ExternalSocksProxyPort` | `int?` | `null` | Route traffic through a SOCKS proxy on this port |

### Unix / System Access

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AllowAllUnixSockets` | `bool` | `false` | Allow tools to connect to any Unix domain socket |
| `AllowUnixSockets` | `string[]?` | `null` | Specific Unix socket paths to allow |
| `AllowPty` | `bool` | `false` | Allow tools to allocate a pseudoterminal |
| `AllowLocalBinding` | `bool` | `false` | Allow tools to bind to local ports |
| `AllowGitConfig` | `bool` | `false` | Allow reading Git config files |
| `AllowedEnvironmentVariables` | `string[]` | `["PATH", "HOME", "TERM", "LANG"]` | Environment variables tools can access |

### Scoping

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SandboxableFunctions` | `string[]` | `[]` | Function names the sandbox applies to. Empty = all functions |
| `ExcludedFunctions` | `string[]` | `[]` | Function names explicitly excluded from sandboxing |
| `MandatoryDenySearchDepth` | `int` | `3` | Directory recursion depth when evaluating deny-list paths |

### Behavior on Failure / Violation

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `OnInitializationFailure` | `SandboxFailureBehavior` | `Block` | What to do if the sandbox cannot be initialized |
| `OnViolation` | `SandboxViolationBehavior` | `EmitEvent` | What to do when a tool attempts a restricted operation |
| `EnableWeakerNestedSandbox` | `bool` | `false` | Allow a less strict sandbox inside an already-sandboxed process |
| `EnableViolationMonitoring` | `bool` | `false` | Continuously monitor for violations rather than only on access |
| `IgnoreViolationPatterns` | `string[]?` | `null` | Glob patterns for violations to silently ignore |

---

## Enums

### `SandboxFailureBehavior`

| Value | Description |
|-------|-------------|
| `Block` | Refuse to start the agent if the sandbox cannot be established |
| `Warn` | Log a warning and continue without sandboxing |
| `Ignore` | Silently continue without sandboxing |

### `SandboxViolationBehavior`

| Value | Description |
|-------|-------------|
| `EmitEvent` | Emit a violation event but allow the operation to proceed |
| `BlockAndEmit` | Block the operation and emit a violation event |
| `Ignore` | Allow the operation silently |

---

## Examples

### Restrict to specific domains

```csharp
var sandbox = SandboxConfig.CreateDefault();
sandbox.AllowedDomains = ["api.myservice.com", "cdn.myservice.com"];
sandbox.DeniedDomains = ["example-malicious.com"];
```

### Block violations hard

```csharp
var sandbox = SandboxConfig.CreateEnhanced();
sandbox.OnViolation = SandboxViolationBehavior.BlockAndEmit;
```

### Scope to specific tools only

```csharp
var sandbox = SandboxConfig.CreateDefault();
sandbox.SandboxableFunctions = ["ExecuteShellCommand", "WriteFile"];
sandbox.ExcludedFunctions = ["ReadPublicFile"];
```

---

## Validation

Call `sandbox.Validate()` after configuring to catch configuration errors before building the agent:

```csharp
var sandbox = SandboxConfig.CreateDefault();
sandbox.AllowWrite = ["/safe/output"];
sandbox.Validate();   // throws ArgumentException if config is inconsistent
```

---

## See Also

- [Agent Config](Agent%20Config.md)
- [Agent Builder](Agent%20Builder.md)
