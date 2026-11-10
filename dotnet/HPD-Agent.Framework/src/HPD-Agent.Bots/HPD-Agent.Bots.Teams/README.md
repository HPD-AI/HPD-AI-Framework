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

`AddTeamsBot()` also registers the JWT bearer and authorization services required by
`MapTeamsBot()`. The default bearer setup validates Azure Bot Service tokens against
`https://login.botframework.com` using the configured `AppId` as the audience. The
`TokenValidation` configuration section can override audiences, trusted issuers,
tenant, cloud, and metadata URL.

For a host with a custom ASP.NET Core authentication scheme, supply it explicitly:

```csharp
builder.AddTeamsBot(
    options =>
    {
        options.AppId = builder.Configuration["Teams:AppId"]!;
        options.AppPassword = builder.Configuration["Teams:AppPassword"];
        options.AppTenantId = builder.Configuration["Teams:TenantId"];
    },
    configureAuthentication: services =>
    {
        // Register the host's authentication and JWT bearer options.
    });
```

`AddTeamsBot()` also registers the SDK's M365 attachment downloader. Teams file uploads are downloaded by the Agents SDK and exposed through turn-state input files before HPD message processing runs.

Graph-backed history APIs are opt-in. Register them only when the host already has a configured Microsoft Graph client:

```csharp
builder.Services.AddTeamsGraphHistory(graphClient);
```

## Configuration Shape

The host app must also provide the outbound Agents SDK connection settings expected by
`Microsoft.Agents.Hosting.AspNetCore`. The default inbound token validation reads the
following HPD and Agents SDK settings:

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

Inbound turns persist compact Teams routing metadata such as `teams.conversationId`, `teams.serviceUrl`, `teams.tenantId`, and `teams.channelContext`. Live proactive DM and Graph history behavior still need validation against a real Microsoft 365 tenant.
