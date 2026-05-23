# HPDOS Backend

The HPDOS backend is the local Kestrel runtime for the HPD-OS workspace shell.

It serves the static browser UI and hosts the HPD-Agent API used by that UI.
Agent definitions live in the configured agent store as JSON and are addressed by `agentId`.

## Endpoints

- `GET /`
- `GET /health`
- `GET /api/hpdos/runtime`
- `POST /api/hpd-agent/agents`
- `POST /api/hpd-agent/agents/{agentId}/sessions/{sessionId}/branches/{branchId}/stream`
- `POST /api/hpd-agent/agents/{agentId}/sessions/{sessionId}/branches/{branchId}/events/stream`
- `GET /api/hpd-agent/agents/{agentId}/sessions/{sessionId}/branches/{branchId}/ws`

## Run

```bash
dotnet run --file backend.cs
```

The default URL is `http://127.0.0.1:4317`.
Open that URL in a browser for the quick local chat surface.

You can also use the file-app shorthand:

```bash
dotnet backend.cs
```

In Development, the default data root is local to this project:

```txt
HPDOS/backend/.hpdos
```

The browser chat uses the HPD Agent API for session creation, branch history, client tools, and streaming turns.
The UI is split into a framework-free TSX view layer and a headless HPDOS core:

- `wwwroot/src/core` owns runtime, workspace, session, artifact, and chat orchestration.
- `wwwroot/src/view` owns DOM mounting and no-framework TSX components.
- `wwwroot/src/shared` owns browser-agnostic formatting helpers.

Build or type-check the UI with:

```bash
bun install
bun run check:ui
bun run build:ui
```

## Configuration

Configure stores and the default project directory with `appsettings.json` or environment variables:

- `HPDOS:DataRoot`
- `HPDOS:ProjectDirectory`
- `HPDOS:AgentStorePath`
- `HPDOS:SessionStorePath`

Relative store paths resolve from the `HPDOS/backend` directory.
`HPDOS:ProjectDirectory` becomes the default workspace root shown in the UI. The desktop shell sets it from `HPDOS__ProjectDirectory`; additional user-selected roots come from the Electrobun folder picker and are injected into `runConfig.contextOverrides.workspace`.

Provider API keys are resolved by HPD-Agent provider secret resolution, for example:

- `OPENAI_API_KEY`
- `ANTHROPIC_API_KEY`
- `OPENROUTER_API_KEY`
- `GOOGLE_API_KEY` or `GEMINI_API_KEY`

Keep API keys server-side. The browser chat sends only `providerKey` and `modelId`; the HPDOS backend resolves secrets from environment variables, ASP.NET configuration, or user secrets.

```bash
export OPENAI_API_KEY="..."
dotnet user-secrets set "openai:ApiKey" "..." --file backend.cs
```
