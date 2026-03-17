using HPD.Agent.Adapters.Slack;
using HPD.Agent.Adapters.Slack.OAuth;
using HPD.Agent.AspNetCore;
using HPD.Agent.Providers.Anthropic;

// Catch any unobserved exceptions from fire-and-forget tasks (e.g. StreamToSlackAsync)
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine($"[UNOBSERVED EXCEPTION] {e.Exception}");
    e.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHPDAgent(cfg =>
{
    cfg.ConfigureAgent = ab => ab
        .WithAnthropic(
            model: builder.Configuration["Agent:Model"] ?? "claude-sonnet-4-5-20250929",
            apiKey: builder.Configuration["Anthropic:ApiKey"]);

    cfg.PersistAfterTurn = true;
});

builder.Services.AddHttpClient();

builder.Services.AddSlackAdapter(c =>
{
    c.SigningSecret = builder.Configuration["Slack:SigningSecret"]!;
    c.BotToken      = builder.Configuration["Slack:BotToken"]!;
}, registerDefaultSecretResolver: true);

builder.Services.AddSlackOAuth(c =>
{
    c.ClientId     = builder.Configuration["Slack:ClientId"]!;
    c.ClientSecret = builder.Configuration["Slack:ClientSecret"]!;
    c.RedirectUri  = builder.Configuration["Slack:OAuth:RedirectUri"]!;
});

var app = builder.Build();

app.MapSlackWebhook("/slack/events");
app.MapSlackOAuth("/slack/install", "/slack/oauth/callback");

app.Run();
