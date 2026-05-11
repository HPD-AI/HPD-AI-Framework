# HPDOS Backend

The HPDOS backend is the single local Kestrel runtime for HPD-OS UIs.

It hosts the HPD-Agent API once, so desktop, web, Tauri, mobile, CLI, or test UIs can all talk to the same runtime process.
Agent definitions live in the configured agent store as JSON and are addressed by `agentId`.

## Endpoints

- `GET /`
- `GET /health`
- `GET /api/hpdos/runtime`
- `POST /api/hpd-agent/agents`
- `POST /api/hpd-agent/sessions`
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

Create agent definitions through `POST /api/hpd-agent/agents`, then use the returned `agentId` in stream and WebSocket routes.

## Configuration

Configure stores and UI origins with `appsettings.json` or environment variables:

- `HPDOS:DataRoot`
- `HPDOS:AgentStorePath`
- `HPDOS:SessionStorePath`
- `HPDOS:AllowedOrigins`

Relative store paths resolve from the `HPDOS/backend` directory.

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
