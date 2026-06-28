using HPD.Agent;
using HPD.Agent.Bots.Discord;
using HPD.Agent.Bots.Slack;
using HPD.Agent.Bots.Slack.OAuth;
using HPD.Agent.Bots.Teams;
using HPD.Agent.Bots.Telegram;
using HPD.Agent.Bots.WhatsApp;
using HPD.Agent.AspNetCore;
using HPD.Agent.Providers.Anthropic;

// Catch any unobserved exceptions from fire-and-forget tasks (e.g. StreamToSlackAsync)
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine($"[UNOBSERVED EXCEPTION] {e.Exception}");
    e.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);
var agentId = builder.Configuration["Agent:Id"] ?? "dev-agent";

builder.Services.AddHPDAgent(cfg =>
{
    cfg.AgentContributors.Add(new DelegateAgentBuilderContributor(ab => ab
        .WithAnthropic(
            model: builder.Configuration["Agent:Model"] ?? "claude-sonnet-4-5-20250929",
            apiKey: builder.Configuration["Anthropic:ApiKey"])));

    cfg.PersistAfterTurn = true;
});

builder.Services.AddHttpClient();

if (IsEnabled(builder.Configuration, "Slack", defaultEnabled: HasSlackConfig(builder.Configuration)))
{
    builder.Services.AddSlackBot(c =>
    {
        c.SigningSecret       = Required(builder.Configuration, "Slack:SigningSecret");
        c.BotToken            = Required(builder.Configuration, "Slack:BotToken");
        c.AppToken            = Optional(builder.Configuration, "Slack:AppToken");
        c.AgentId             = agentId;
        c.UseNativeStreaming  = Bool(builder.Configuration, "Slack:UseNativeStreaming");
        c.StreamingDebounceMs = Int(builder.Configuration, "Slack:StreamingDebounceMs");
    }, registerDefaultSecretResolver: true);

    if (Has(builder.Configuration, "Slack:ClientId")
        && Has(builder.Configuration, "Slack:ClientSecret")
        && Has(builder.Configuration, "Slack:OAuth:RedirectUri"))
    {
        builder.Services.AddSlackOAuth(c =>
        {
            c.ClientId     = Required(builder.Configuration, "Slack:ClientId");
            c.ClientSecret = Required(builder.Configuration, "Slack:ClientSecret");
            c.RedirectUri  = Required(builder.Configuration, "Slack:OAuth:RedirectUri");
        });
    }
}

if (IsEnabled(builder.Configuration, "Discord"))
{
    builder.Services.AddDiscordBot(c =>
    {
        c.PublicKey           = Required(builder.Configuration, "Discord:PublicKey");
        c.BotToken            = Required(builder.Configuration, "Discord:BotToken");
        c.ApplicationId       = Required(builder.Configuration, "Discord:ApplicationId");
        c.AgentId             = agentId;
        c.GatewayToken        = Optional(builder.Configuration, "Discord:GatewayToken");
        c.GatewayForwardUrl   = Optional(builder.Configuration, "Discord:GatewayForwardUrl");
        c.StreamingDebounceMs = Int(builder.Configuration, "Discord:StreamingDebounceMs");
    }, registerInfrastructure: true);
}

if (IsEnabled(builder.Configuration, "Telegram"))
{
    var mode = Enum.TryParse<TelegramBotMode>(
        builder.Configuration["Telegram:Mode"],
        ignoreCase: true,
        out var configuredMode)
            ? configuredMode
            : TelegramBotMode.Webhook;

    Action<TelegramBotConfig> configureTelegram = c =>
    {
        c.BotToken            = Required(builder.Configuration, "Telegram:BotToken");
        c.SecretToken         = Optional(builder.Configuration, "Telegram:SecretToken");
        c.UserName            = Optional(builder.Configuration, "Telegram:UserName");
        c.ApiBaseUrl          = Optional(builder.Configuration, "Telegram:ApiBaseUrl");
        c.AgentId             = agentId;
        c.Mode                = mode;
        c.StreamingDebounceMs = Int(builder.Configuration, "Telegram:StreamingDebounceMs");
    };

    if (mode == TelegramBotMode.Polling)
        builder.Services.AddTelegramBotWithPolling(configureTelegram);
    else
        builder.Services.AddTelegramBot(configureTelegram, registerInfrastructure: true);
}

if (IsEnabled(builder.Configuration, "Whatsapp") || IsEnabled(builder.Configuration, "WhatsApp"))
{
    builder.Services.AddWhatsappBot(c =>
    {
        c.AccessToken   = RequiredAny(builder.Configuration, "Whatsapp:AccessToken", "WhatsApp:AccessToken");
        c.AppSecret     = RequiredAny(builder.Configuration, "Whatsapp:AppSecret", "WhatsApp:AppSecret");
        c.PhoneNumberId = RequiredAny(builder.Configuration, "Whatsapp:PhoneNumberId", "WhatsApp:PhoneNumberId");
        c.VerifyToken   = RequiredAny(builder.Configuration, "Whatsapp:VerifyToken", "WhatsApp:VerifyToken");
        c.ApiVersion    = OptionalAny(builder.Configuration, "Whatsapp:ApiVersion", "WhatsApp:ApiVersion") ?? c.ApiVersion;
        c.ApiUrl        = OptionalAny(builder.Configuration, "Whatsapp:ApiUrl", "WhatsApp:ApiUrl");
        c.UserName      = OptionalAny(builder.Configuration, "Whatsapp:UserName", "WhatsApp:UserName") ?? c.UserName;
        c.AgentId       = agentId;
    }, registerInfrastructure: true);
}

if (IsEnabled(builder.Configuration, "Teams"))
{
    builder.AddTeamsBot(c =>
    {
        c.AppId       = Required(builder.Configuration, "Teams:AppId");
        c.AppPassword = Optional(builder.Configuration, "Teams:AppPassword");
        c.AppTenantId = Optional(builder.Configuration, "Teams:TenantId");
        c.AppType     = builder.Configuration["Teams:AppType"] ?? "SingleTenant";
        c.UserName    = Optional(builder.Configuration, "Teams:UserName");
        c.AgentId     = agentId;
    });
}

var app = builder.Build();

app.MapGet("/", () => Results.Json(new
{
    ok = true,
    app = "HPD Agent DevApp",
    agentId,
    endpoints = new
    {
        slack = IsEnabled(app.Configuration, "Slack", defaultEnabled: HasSlackConfig(app.Configuration)) ? "/slack/events" : null,
        discord = IsEnabled(app.Configuration, "Discord") ? "/discord/interactions" : null,
        telegram = IsEnabled(app.Configuration, "Telegram") ? "/telegram/webhook" : null,
        whatsapp = IsEnabled(app.Configuration, "Whatsapp") || IsEnabled(app.Configuration, "WhatsApp") ? "/whatsapp/webhook" : null,
        teams = IsEnabled(app.Configuration, "Teams") ? "M365 Agents SDK endpoints" : null,
    }
}));

if (IsEnabled(app.Configuration, "Slack", defaultEnabled: HasSlackConfig(app.Configuration)))
{
    app.MapSlackWebhook("/slack/events");

    if (Has(app.Configuration, "Slack:ClientId")
        && Has(app.Configuration, "Slack:ClientSecret")
        && Has(app.Configuration, "Slack:OAuth:RedirectUri"))
    {
        app.MapSlackOAuth("/slack/install", "/slack/oauth/callback");
    }
}

if (IsEnabled(app.Configuration, "Discord"))
    app.MapDiscordWebhook("/discord/interactions");

if (IsEnabled(app.Configuration, "Telegram")
    && !string.Equals(app.Configuration["Telegram:Mode"], "Polling", StringComparison.OrdinalIgnoreCase))
{
    app.MapTelegramWebhook("/telegram/webhook");
}

if (IsEnabled(app.Configuration, "Whatsapp") || IsEnabled(app.Configuration, "WhatsApp"))
    app.MapWhatsappWebhook("/whatsapp/webhook");

if (IsEnabled(app.Configuration, "Teams"))
    app.MapTeamsBot();

app.Run();

static bool IsEnabled(IConfiguration config, string section, bool defaultEnabled = false)
    => bool.TryParse(config[$"{section}:Enabled"], out var enabled)
        ? enabled
        : defaultEnabled;

static bool HasSlackConfig(IConfiguration config)
    => Has(config, "Slack:SigningSecret") && Has(config, "Slack:BotToken");

static bool Has(IConfiguration config, string key)
    => !string.IsNullOrWhiteSpace(config[key]);

static string Required(IConfiguration config, string key)
    => config[key] ?? throw new InvalidOperationException($"{key} is required.");

static string RequiredAny(IConfiguration config, params string[] keys)
    => keys.Select(key => config[key]).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? throw new InvalidOperationException($"{string.Join(" or ", keys)} is required.");

static string? Optional(IConfiguration config, string key)
    => string.IsNullOrWhiteSpace(config[key]) ? null : config[key];

static string? OptionalAny(IConfiguration config, params string[] keys)
    => keys.Select(key => config[key]).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

static bool Bool(IConfiguration config, string key)
    => bool.TryParse(config[key], out var value) && value;

static int? Int(IConfiguration config, string key)
    => int.TryParse(config[key], out var value) ? value : null;
