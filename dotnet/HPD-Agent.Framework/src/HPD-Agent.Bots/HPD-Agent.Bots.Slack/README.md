# HPD-Agent.Bots.Slack

Connect an HPD agent to Slack Events API, Socket Mode, Block Kit cards, permissions, and streaming output.

```bash
dotnet add package HPD-Agent.Bots.Slack --version 1.0.0
```

```csharp
using HPD.Agent.Bots.Slack;

builder.Services.AddSlackBot(options =>
{
    options.SigningSecret = builder.Configuration["Slack:SigningSecret"]!;
    options.BotToken = builder.Configuration["Slack:BotToken"]!;
    options.AgentId = "support-agent";
}, registerDefaultSecretResolver: true);

var app = builder.Build();
app.MapSlackWebhook("/slack/events");
```

Set the Events API, Interactivity, and slash-command request URLs to the mapped endpoint. For Socket Mode, set `AppToken` and omit the public webhook dependency.

Slack supports post-and-edit streaming, optional native assistant streaming, OAuth installation, modal and card events, attachments, and interactive permission buttons.

See the [Slack guide](https://hpd-ai.github.io/HPD-Agent/guides/bots/slack) for required scopes, events, OAuth storage, and troubleshooting.
