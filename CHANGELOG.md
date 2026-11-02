# Changelog

All notable changes to the HPD AI Framework are documented in this file.

## 0.9.2 - 2026-11-02

### Added

- Declarative MCP server and OAuth configuration through `AgentConfig`.
- Source-generated permission descriptors and MCP capability-to-permission resolution.
- Inheritable tool-harness capabilities with centralized activation and sub-agent configuration overrides.
- Function-level capability collapsing through generated metadata and selectors.
- Unified declarative workflow runtime with typed output contracts, graph-host bindings, recovery validation, and node result metadata.
- Structured-output resolution and canonical tool-argument handling across nested agent projections.
- Scoped command, debugging, exploration, and tool-call activity state, including turn-scoped sandbox capability approval.
- A single-input `Release HPD Agent and Dependencies` workflow that publishes the complete Agent dependency train in dependency order.

### Changed

- HPD-Environment now owns process-isolation planning contracts, sandbox plans, domain-pattern parsing, and the sandbox isolation compiler/planner implementation.
- HPD-Agent now consumes those contracts from HPD-Environment; Environment no longer depends on Agent packages or Agent build targets.
- Environment Apple Virtualization packages now have a clean dependency direction: Environment must be published before Agent.
- Agent configuration serialization now shares the framework YAML/JSON configuration bridge.

### Fixed

- Workflow execution now drains pending event inboxes before reporting completion.
- Execute-command completion and output handling now preserve terminal results during streaming and completion transitions.
- YAML and JSON configuration serialization now use a consistent document and bridge representation.

### Breaking

- Sandbox planning contracts including `SandboxExecutionContext`, `SandboxEnforcementLocation`, `SandboxPlanEnvelope`, `ISandboxPlanner`, and `SandboxIsolationPlan` moved from `HPD.Agent.Sandbox.ProcessIsolation` to `HPD.Environment.Contracts`.
- `DomainPattern` and `DomainPatternKind` moved from `HPD.Agent.Sandbox.Policy` to `HPD.Environment.Contracts`.
- `SandboxIsolationPlanner` and `SandboxIsolationCompiler` moved from `HPD.Agent.Sandbox.ProcessIsolation` to `HPD.Environment.Runtime`.
- Consumers of those types must update namespace imports and publish HPD-Environment before HPD-Agent.
- The serialized sandbox-plan schema identifier remains `hpd.agent.sandbox.plan` for wire compatibility.
