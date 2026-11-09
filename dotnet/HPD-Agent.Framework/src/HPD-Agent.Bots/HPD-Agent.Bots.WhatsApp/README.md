# HPD-Agent.Bots.WhatsApp

Connect an HPD agent to the WhatsApp Business Cloud API.

```bash
dotnet add package HPD-Agent.Bots.WhatsApp --version 1.0.0
```

```csharp
using HPD.Agent.Bots.WhatsApp;

builder.Services.AddWhatsappBot(options =>
{
    options.AccessToken = builder.Configuration["WhatsApp:AccessToken"]!;
    options.AppSecret = builder.Configuration["WhatsApp:AppSecret"]!;
    options.PhoneNumberId = builder.Configuration["WhatsApp:PhoneNumberId"]!;
    options.VerifyToken = builder.Configuration["WhatsApp:VerifyToken"]!;
    options.AgentId = "support-agent";
}, registerInfrastructure: true);

var app = builder.Build();
app.MapWhatsappWebhook("/whatsapp/webhook");
```

Set the Meta webhook callback URL to the mapped route. The route handles GET verification challenges and signed POST events. Invalid verification returns `403`; invalid signatures return `401`.

WhatsApp supports text, media, documents, locations, replies, reactions, typing indicators, and reply-button cards. Responses are buffered because WhatsApp does not support message editing. Permission requests are currently denied automatically.

See the [WhatsApp guide](https://hpd-ai.github.io/HPD-Agent/guides/bots/whatsapp) for Meta setup, signature verification, message limits, and the customer-service window.
