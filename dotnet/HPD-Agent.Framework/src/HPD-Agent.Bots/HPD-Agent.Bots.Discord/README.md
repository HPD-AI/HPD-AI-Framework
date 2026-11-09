# HPD-Agent.Bots.Discord

Connect an HPD agent to Discord interactions and optional Gateway message or reaction events.

```bash
dotnet add package HPD-Agent.Bots.Discord --version 1.0.0
```

```csharp
using HPD.Agent.Bots.Discord;

builder.Services.AddDiscordBot(options =>
{
    options.PublicKey = builder.Configuration["Discord:PublicKey"]!;
    options.BotToken = builder.Configuration["Discord:BotToken"]!;
    options.ApplicationId = builder.Configuration["Discord:ApplicationId"]!;
    options.AgentId = "support-agent";
}, registerInfrastructure: true);

var app = builder.Build();
app.MapDiscordWebhook("/discord/interactions");
```

Set the Discord Interactions Endpoint URL to the mapped route. Configure `GatewayToken` and an absolute `GatewayForwardUrl` to receive regular message and reaction events through the Gateway.

Discord supports slash commands, buttons, modals, autocomplete, embeds, attachments, reaction events, and streamed follow-up edits. Permission requests are currently denied automatically rather than rendered as approval buttons.

See the [Discord guide](https://hpd-ai.github.io/HPD-Agent/guides/bots/discord) for intents, permissions, Gateway behavior, and troubleshooting.
