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
