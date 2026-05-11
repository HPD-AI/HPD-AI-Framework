#:sdk Microsoft.NET.Sdk.Web
#:property TargetFramework=net10.0
#:property UserSecretsId=hpdos-backend-dev
#:property PublishAot=false
#:property IsAotCompatible=false
#:property PublishSingleFile=true
#:property SelfContained=true
#:property RuntimeIdentifier=osx-arm64
#:property PackAsTool=false
#:property OptimizationPreference=Size
#:property DebugType=none
#:property NativeDebugSymbols=false
#:property DebuggerSupport=false
#:property StackTraceSupport=false
#:property EventSourceSupport=true
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:package OpenTelemetry.Exporter.OpenTelemetryProtocol@1.15.3
#:package OpenTelemetry.Extensions.Hosting@1.15.3
#:package OpenTelemetry.Instrumentation.AspNetCore@1.15.2
#:package OpenTelemetry.Instrumentation.Http@1.15.1
#:package OpenTelemetry.Instrumentation.Runtime@1.15.1
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent/HPD-Agent.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.AspNetCore/HPD-Agent.AspNetCore.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Bots/HPD-Agent.Bots.AspNetCore/HPD-Agent.Bots.AspNetCore.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Bots/HPD-Agent.Bots.Discord/HPD-Agent.Bots.Discord.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Bots/HPD-Agent.Bots.Slack/HPD-Agent.Bots.Slack.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Bots/HPD-Agent.Bots.Teams/HPD-Agent.Bots.Teams.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Bots/HPD-Agent.Bots.Telegram/HPD-Agent.Bots.Telegram.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Bots/HPD-Agent.Bots.WhatsApp/HPD-Agent.Bots.WhatsApp.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Hosting/HPD-Agent.Hosting.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.Anthropic/HPD-Agent.Providers.Anthropic.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.AzureAI/HPD-Agent.Providers.AzureAI.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.AzureAIInference/HPD-Agent.Providers.AzureAIInference.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.Bedrock/HPD-Agent.Providers.Bedrock.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.GoogleAI/HPD-Agent.Providers.GoogleAI.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.HuggingFace/HPD-Agent.Providers.HuggingFace.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.Mistral/HPD-Agent.Providers.Mistral.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.Ollama/HPD-Agent.Providers.Ollama.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.OpenAI/HPD-Agent.Providers.OpenAI.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Agent.Providers/HPD-Agent.Providers.OpenRouter/HPD-Agent.Providers.OpenRouter.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Auth.Framework/src/HPD.Auth/HPD.Auth.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Auth.Framework/src/HPD.Auth.Authentication/HPD.Auth.Authentication.csproj
#:project ../../HPD-AI-Framework/dotnet/HPD-Auth.Framework/src/HPD.Auth.Authorization/HPD.Auth.Authorization.csproj

using HPD.Agent;
using HPD.Agent.AspNetCore;
using HPD.Agent.Bots.Discord;
using HPD.Agent.Bots.Slack;
using HPD.Agent.Bots.Teams;
using HPD.Agent.Bots.Telegram;
using HPD.Agent.Bots.WhatsApp;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Authorization.Extensions;
using HPD.Auth.Extensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var backendDirectory = FindBackendDirectory();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = backendDirectory
});

var defaultDataRoot = builder.Environment.IsDevelopment()
    ? Path.Combine(backendDirectory, ".hpdos")
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HPDOS");

var dataRoot = ResolveStorePath(builder.Configuration["HPDOS:DataRoot"], defaultDataRoot, backendDirectory);
var agentStorePath = ResolveStorePath(
    builder.Configuration["HPDOS:AgentStorePath"],
    Path.Combine(dataRoot, "agents"),
    backendDirectory);
var sessionStorePath = ResolveStorePath(
    builder.Configuration["HPDOS:SessionStorePath"],
    Path.Combine(dataRoot, "sessions"),
    backendDirectory);

Directory.CreateDirectory(agentStorePath);
Directory.CreateDirectory(sessionStorePath);

var allowedOrigins = builder.Configuration
    .GetSection("HPDOS:AllowedOrigins")
    .Get<string[]>()
    ?? [];
var defaultAgentId = builder.Configuration["HPDOS:DefaultAgentId"] ?? "hpdos-agent";
var telemetrySourceName = builder.Configuration["HPDOS:Telemetry:SourceName"] ?? "HPD.Agent";
var telemetryEnabled = builder.Configuration.GetValue<bool?>("HPDOS:Telemetry:Enabled")
    ?? !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
var captureSensitiveTelemetry = builder.Configuration.GetValue<bool>("HPDOS:Telemetry:CaptureSensitiveData");
var includeRuntimeMetrics = builder.Configuration.GetValue("HPDOS:Telemetry:RuntimeMetrics", true);
var authEnabled = builder.Configuration.GetValue<bool>("HPDOS:Auth:Enabled");
var requireAgentApiAuth = authEnabled
    && builder.Configuration.GetValue<bool>("HPDOS:Auth:RequireAgentApiAuth");

if (telemetryEnabled)
{
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
    });

    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics =>
        {
            metrics
                .AddMeter(telemetrySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (includeRuntimeMetrics)
                metrics.AddRuntimeInstrumentation();
        })
        .WithTracing(tracing =>
        {
            tracing
                .AddSource(telemetrySourceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health")
                        && !context.Request.Path.StartsWithSegments("/alive");
                })
                .AddHttpClientInstrumentation();
        });

    if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        builder.Services.AddOpenTelemetry().UseOtlpExporter();
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("HPDOS.UI", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddHPDAgent("workspace", options =>
{
    options.AgentStore = new JsonAgentStore(agentStorePath);
    options.SessionStorePath = sessionStorePath;
    options.PersistAfterTurn = true;
    options.PersistAgentDefinitionsOnBuild = true;
    options.ConfigureAgent = agent =>
    {
        agent.WithAPIConfiguration(builder.Configuration);

        if (telemetryEnabled)
        {
            agent
                .WithTracing(telemetrySourceName)
                .WithTelemetry(telemetrySourceName, captureSensitiveTelemetry);
        }
    };
});

if (authEnabled)
{
    builder.Services
        .AddHPDAuth(options =>
        {
            builder.Configuration.GetSection("HPDOS:Auth").Bind(options);
            if (string.IsNullOrWhiteSpace(options.AppName))
                options.AppName = "HPD-OS";
        })
        .AddAuthentication()
        .AddAuthorization();
}

var slackEnabled = builder.Configuration.GetValue<bool>("HPDOS:Bots:Slack:Enabled");
var discordEnabled = builder.Configuration.GetValue<bool>("HPDOS:Bots:Discord:Enabled");
var telegramEnabled = builder.Configuration.GetValue<bool>("HPDOS:Bots:Telegram:Enabled");
var whatsAppEnabled = builder.Configuration.GetValue<bool>("HPDOS:Bots:WhatsApp:Enabled");
var teamsEnabled = builder.Configuration.GetValue<bool>("HPDOS:Bots:Teams:Enabled");

if (slackEnabled)
{
    builder.Services.AddSlackBot(options =>
    {
        builder.Configuration.GetSection("HPDOS:Bots:Slack").Bind(options);
        options.AgentId ??= defaultAgentId;
    }, registerDefaultSecretResolver: true);
}

if (discordEnabled)
{
    builder.Services.AddDiscordBot(options =>
    {
        builder.Configuration.GetSection("HPDOS:Bots:Discord").Bind(options);
        options.AgentId ??= defaultAgentId;
    }, registerInfrastructure: true);
}

if (telegramEnabled)
{
    builder.Services.AddTelegramBot(options =>
    {
        builder.Configuration.GetSection("HPDOS:Bots:Telegram").Bind(options);
        options.AgentId ??= defaultAgentId;
    }, registerInfrastructure: true);
}

if (whatsAppEnabled)
{
    builder.Services.AddWhatsappBot(options =>
    {
        builder.Configuration.GetSection("HPDOS:Bots:WhatsApp").Bind(options);
        options.AgentId ??= defaultAgentId;
    }, registerInfrastructure: true);
}

if (teamsEnabled)
{
    builder.AddTeamsBot(options =>
    {
        builder.Configuration.GetSection("HPDOS:Bots:Teams").Bind(options);
        options.AgentId ??= defaultAgentId;
    }, registerInfrastructure: true);
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

var app = builder.Build();

app.UseCors("HPDOS.UI");
app.UseDefaultFiles();
app.UseStaticFiles();

if (authEnabled)
    app.UseHPDAuth();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "hpdos-backend"
}));

app.MapGet("/api/hpdos/runtime", () => Results.Ok(new
{
    service = "hpdos-backend",
    agentApi = "/api/hpd-agent",
    authEnabled,
    requireAgentApiAuth,
    telemetryEnabled,
    telemetrySourceName,
    dataRoot,
    agentStorePath,
    sessionStorePath
}));

if (slackEnabled)
    app.MapSlackWebhook(builder.Configuration["HPDOS:Bots:Slack:WebhookPath"] ?? "/webhooks/slack");

if (discordEnabled)
    app.MapDiscordWebhook(builder.Configuration["HPDOS:Bots:Discord:WebhookPath"] ?? "/webhooks/discord");

if (telegramEnabled)
    app.MapTelegramWebhook(builder.Configuration["HPDOS:Bots:Telegram:WebhookPath"] ?? "/webhooks/telegram");

if (whatsAppEnabled)
    app.MapWhatsappWebhook(builder.Configuration["HPDOS:Bots:WhatsApp:WebhookPath"] ?? "/webhooks/whatsapp");

if (teamsEnabled)
{
    var requireAuth = builder.Configuration.GetValue<bool?>("HPDOS:Bots:Teams:RequireAuth");
    app.MapTeamsBot(requireAuth: requireAuth);
}

if (authEnabled)
    app.MapHPDAuthEndpoints();

var agentApi = app.MapGroup("/api/hpd-agent");
if (requireAgentApiAuth)
    agentApi.RequireAuthorization();
agentApi.MapHPDAgentApi("workspace");

app.Run();

static string ResolveStorePath(string? configuredPath, string fallbackPath, string basePath)
{
    if (string.IsNullOrWhiteSpace(configuredPath))
        return fallbackPath;

    return Path.IsPathRooted(configuredPath)
        ? configuredPath
        : Path.GetFullPath(Path.Combine(basePath, configuredPath));
}

static string FindBackendDirectory()
{
    var currentDirectory = Directory.GetCurrentDirectory();
    var nestedBackendApp = Path.Combine(currentDirectory, "backend", "backend.cs");
    if (File.Exists(nestedBackendApp))
        return Path.GetDirectoryName(nestedBackendApp)!;

    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "backend.cs"))
            && File.Exists(Path.Combine(directory.FullName, "appsettings.json")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return currentDirectory;
}
