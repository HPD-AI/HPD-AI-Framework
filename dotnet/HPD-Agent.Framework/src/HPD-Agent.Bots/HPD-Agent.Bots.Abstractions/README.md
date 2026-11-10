# HPD-Agent.Bots.Abstractions

Contracts, attributes, and source generation for connecting platform events to HPD agents.

Use this package with `HPD-Agent.Bots.AspNetCore` when building a custom adapter. The generated registry references the ASP.NET Core bridge even when another transport drives the adapter. For the built-in platforms, install the corresponding Slack, Discord, Telegram, WhatsApp, or Teams package.

```bash
dotnet add package HPD-Agent.Bots.Abstractions --version 1.0.0
dotnet add package HPD-Agent.Bots.AspNetCore --version 1.0.0
```

## Custom adapter

```csharp
using System.Text.Json.Serialization;
using HPD.Agent.Bots;

[HpdBot("contoso")]
[HpdStreaming(StreamingStrategy.BufferAndPost)]
public partial class ContosoBot
{
    [HpdBotEventHandler("message.created")]
    private Task<BotAdapterResponse> HandleMessageAsync(
        BotRequestContext context,
        ContosoMessage payload)
        => Task.FromResult(BotAdapterResponse.Text(payload.Text));
}

public sealed record ContosoMessage(string Text);

[JsonSerializable(typeof(ContosoMessage))]
internal partial class ContosoBotJsonContext : JsonSerializerContext;

public sealed class ContosoBotConfig
{
    public string? AgentId { get; set; }
}
```

The source generator emits `AddContosoBot(...)`, `MapContosoWebhook(...)`, neutral `IBotAdapter` dispatch, and registry registration. Handlers must be `private` or `internal`. Payloads must be records and must be included in a hand-written JSON context named `{AdapterClass}JsonContext`.

See the [custom adapter guide](https://hpd-ai.github.io/HPD-Agent/guides/bots/custom-adapters-and-source-generation) and [bots overview](https://hpd-ai.github.io/HPD-Agent/guides/bots/overview).
