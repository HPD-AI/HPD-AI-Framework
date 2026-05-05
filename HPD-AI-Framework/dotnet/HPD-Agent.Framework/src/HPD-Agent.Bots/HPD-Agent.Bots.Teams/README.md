# HPD Teams Bot

Teams uses the Microsoft 365 Agents SDK for HTTP routing, JWT validation, activity deserialization, and proactive conversation endpoints. HPD owns the bot bridge after the SDK has produced an `ITurnContext`.

## Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddTeamsBot(options =>
{
    options.AppId = builder.Configuration["Teams:AppId"]!;
    options.AppPassword = builder.Configuration["Teams:AppPassword"];
    options.AppTenantId = builder.Configuration["Teams:TenantId"];
    options.AppType = "SingleTenant";
});

var app = builder.Build();

app.MapTeamsBot();

app.Run();
```

`MapTeamsBot()` maps the standard Agents SDK endpoints and the proactive endpoints discovered from `[ContinueConversation]` handlers on `TeamsAgent`.

`AddTeamsBot()` also registers the SDK's M365 attachment downloader. Teams file uploads are downloaded by the Agents SDK and exposed through turn-state input files before HPD message processing runs.

## Configuration Shape

The host app must also provide the Agents SDK auth settings expected by `Microsoft.Agents.Hosting.AspNetCore`. The exact values depend on tenant/app registration, but the shape mirrors the official M365 Agents samples:

```json
{
  "Teams": {
    "AppId": "<app-client-id>",
    "AppPassword": "<client-secret>",
    "TenantId": "<tenant-id>"
  },
  "TokenValidation": {
    "Enabled": true,
    "Audiences": ["<app-client-id>"],
    "TenantId": "<tenant-id>"
  },
  "AgentApplication": {
    "StartTypingTimer": false,
    "RemoveRecipientMention": false,
    "NormalizeMentions": false
  },
  "Connections": {
    "ServiceConnection": {
      "Settings": {
        "AuthType": "ClientSecret",
        "AuthorityEndpoint": "https://login.microsoftonline.com/<tenant-id>",
        "ClientId": "<app-client-id>",
        "ClientSecret": "<client-secret>",
        "Scopes": [
          "https://api.botframework.com/.default"
        ]
      }
    }
  },
  "ConnectionsMap": [
    {
      "ServiceUrl": "*",
      "Connection": "ServiceConnection"
    }
  ]
}
```

Inbound turns persist `teams.conversationReference` into HPD session metadata so proactive flows have the SDK conversation material available later. Live proactive DM and Graph history behavior still need validation against a real Microsoft 365 tenant.
