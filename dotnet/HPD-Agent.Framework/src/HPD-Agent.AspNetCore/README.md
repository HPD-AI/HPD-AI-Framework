# HPD-Agent.AspNetCore

ASP.NET Core hosting layer for HPD-Agent Framework. Provides minimal-code APIs with committed, resumable SSE observation.

## Install

```bash
dotnet add package HPD-Agent.AspNetCore
```

## Use When

Use `HPD-Agent.AspNetCore` when an agent must expose session and thread
lifecycle, deduplicated input submission, content upload/download, execution
control, runtime state, and committed resumable SSE through ASP.NET Core.

Register the host with `AddHPDAgentHost(...)` and map its endpoints with
`MapHPDAgentHostApi(...)`. Add `HPD-Agent.AspNetCore.OpenApi` when consumers need
the generated OpenAPI 3.1 contract or the deterministic HPD C# client.
