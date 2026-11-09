# HPD-Agent.Bots.Telegram

Connect an HPD agent to Telegram through webhooks or long polling.

```bash
dotnet add package HPD-Agent.Bots.Telegram --version 1.0.0
```

Webhook mode:

```csharp
using HPD.Agent.Bots.Telegram;

builder.Services.AddTelegramBot(options =>
{
    options.BotToken = builder.Configuration["Telegram:BotToken"]!;
    options.SecretToken = builder.Configuration["Telegram:SecretToken"];
    options.UserName = builder.Configuration["Telegram:UserName"];
    options.AgentId = "support-agent";
    options.Mode = TelegramBotMode.Webhook;
}, registerInfrastructure: true);

var app = builder.Build();
app.MapTelegramWebhook("/telegram/webhook");
```

For polling, call `AddTelegramBotWithPolling(...)` and do not map the webhook endpoint.

Telegram supports message edits, inline keyboards, mentions, files, reactions, channel and topic routing, and runtime caching. Permission requests are currently denied automatically.

See the [Telegram guide](https://hpd-ai.github.io/HPD-Agent/guides/bots/telegram) for webhook registration, polling, group behavior, and callback limits.
