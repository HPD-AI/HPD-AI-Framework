# HPD-Agent.Bots.AspNetCore

ASP.NET Core endpoint mapping and webhook signature helpers for HPD bot adapters.

```bash
dotnet add package HPD-Agent.Bots.AspNetCore --version 1.0.0
```

Map an adapter explicitly when you need a platform-specific route:

```csharp
using HPD.Agent.Bots.Slack;

app.MapSlackWebhook("/slack/events");
```

Or map every registered adapter with its package-provided default:

```csharp
using HPD.Agent.Bots.AspNetCore.EndpointMapping;

app.MapHPDBots();
```

Teams uses its Microsoft 365 Agents SDK endpoint integration:

```csharp
app.MapTeamsBot();
```

The generated ASP.NET bridge preserves adapter status codes, response headers, and bodies. See [bot platform setup](https://hpd-ai.github.io/HPD-Agent/guides/bots/platform-setup).
