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
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.AspNetCore.DependencyInjection;
using HPDAgent.Graph.AspNetCore.EndpointMapping;
using HPDAgent.Graph.Core.Storage;
using HPDAgent.Graph.Hosting.Data;
using HPDAgent.Graph.Hosting.Lifecycle;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Text.Json;
using static UiFragments;

var backendDirectory = FindBackendDirectory();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = backendDirectory
});
var workspaceRoot = ResolveStorePath(
    builder.Configuration["HPDOS:WorkspaceRoot"],
    Path.GetFullPath(Path.Combine(backendDirectory, "..", "..")),
    backendDirectory);

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
var graphStorePath = ResolveStorePath(
    builder.Configuration["HPDOS:GraphStorePath"],
    Path.Combine(dataRoot, "graphs"),
    backendDirectory);

Directory.CreateDirectory(agentStorePath);
Directory.CreateDirectory(sessionStorePath);
Directory.CreateDirectory(graphStorePath);

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
        agent.WithAPIConfiguration(builder.Configuration)
            .WithHarness<CodingHarness>()
            .WithSandbox(SandboxConfig.CreateDefault() with
            {
                AllowWrite = [workspaceRoot, "/tmp"],
                NetworkMode = SandboxNetworkMode.Unrestricted
            });

        if (telemetryEnabled)
        {
            agent
                .WithTracing(telemetrySourceName)
                .WithTelemetry(telemetrySourceName, captureSensitiveTelemetry)
                ;
        }
    };
});

builder.Services.AddSingleton<IGraphDefinitionStore>(new JsonGraphDefinitionStore(graphStorePath));
builder.Services.AddSingleton<IWorkflowExecutionStore>(new JsonWorkflowExecutionStore(graphStorePath));
builder.Services.AddSingleton<IWorkflowLogStore>(new JsonWorkflowLogStore(graphStorePath));
builder.Services.AddSingleton<IScheduledGraphStore>(new JsonScheduledGraphStore(graphStorePath));
builder.Services.AddSingleton<IGraphCheckpointStore>(new JsonCheckpointStore(graphStorePath));
builder.Services.AddHPDGraphAspNetCore();
builder.Services.AddHPDGraphMaterialization();

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

var workflowApi = app.MapHPDGraphWorkflows("/api/workflows");
if (authEnabled)
    workflowApi.RequireAuthorization();

var ui = app.MapGroup("/ui");
if (authEnabled)
    ui.RequireAuthorization();

ui.MapGet("/chat", () => Html(ChatView()));

ui.MapGet("/workflows", async (GraphManager graphManager, IGeneratedHandlerCatalog catalog, SchedulingManager schedulingManager, CancellationToken ct) =>
{
    var workflows = await graphManager.ListDefinitionsAsync(ct).ConfigureAwait(false);
    var selected = workflows.FirstOrDefault()?.GraphId;
    var handlers = RenderHandlers(catalog.GetHandlers());
    var editor = selected is null
        ? RenderWorkflowEditor(SampleGraph(), handlers)
        : RenderWorkflowEditor((await graphManager.GetDefinitionAsync(selected, ct).ConfigureAwait(false))?.Config ?? SampleGraph(), handlers);

    var side = await RenderWorkflowSideAsync(selected, schedulingManager, ct).ConfigureAwait(false);
    return Html(WorkflowShell(workflows, selected, editor, side));
});

ui.MapGet("/workflows/list", async (string? search, GraphManager graphManager, CancellationToken ct) =>
{
    var workflows = await graphManager.ListDefinitionsAsync(ct).ConfigureAwait(false);
    if (!string.IsNullOrWhiteSpace(search))
    {
        workflows = workflows
            .Where(w => $"{w.Name} {w.GraphId}".Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    return Html(RenderWorkflowList(workflows, null));
});

ui.MapGet("/workflows/new", (IGeneratedHandlerCatalog catalog) => Html(RenderWorkflowEditor(SampleGraph(), RenderHandlers(catalog.GetHandlers()))));

ui.MapGet("/workflows/{graphId}", async (string graphId, GraphManager graphManager, SchedulingManager schedulingManager, IGeneratedHandlerCatalog catalog, CancellationToken ct) =>
{
    var workflow = await graphManager.GetDefinitionAsync(graphId, ct).ConfigureAwait(false);
    if (workflow is null)
        return Html(RenderNotice("Workflow not found."));

    var handlers = RenderHandlers(catalog.GetHandlers());
    var side = await RenderWorkflowSideAsync(graphId, schedulingManager, ct).ConfigureAwait(false);
    return Html(WorkflowSelected(workflow.Config, handlers, side));
});

ui.MapPost("/workflows/save", async (HttpRequest request, GraphManager graphManager, SchedulingManager schedulingManager, IGeneratedHandlerCatalog catalog, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
    var json = form["graphJson"].ToString();
    try
    {
        var graph = JsonSerializer.Deserialize<GraphConfig>(json, JsonOptions())
            ?? throw new InvalidOperationException("Graph JSON was empty.");
        StoredGraph saved;
        if (await graphManager.GetDefinitionAsync(graph.GraphId, ct).ConfigureAwait(false) is null)
            saved = await graphManager.CreateDefinitionAsync(graph, ct).ConfigureAwait(false);
        else
            saved = await graphManager.UpdateDefinitionAsync(graph.GraphId, graph, ct).ConfigureAwait(false);

        var workflows = await graphManager.ListDefinitionsAsync(ct).ConfigureAwait(false);
        var handlers = RenderHandlers(catalog.GetHandlers());
        var side = await RenderWorkflowSideAsync(saved.GraphId, schedulingManager, ct).ConfigureAwait(false);
        return Html(WorkflowSaved(saved.Config, handlers, workflows, saved.GraphId, side));
    }
    catch (Exception ex)
    {
        return Html(RenderEditorError(json, ex.Message));
    }
});

ui.MapPost("/workflows/{graphId}/run", async (string graphId, HttpRequest request, IWorkflowExecutionRunner runner, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
    try
    {
        var input = JsonSerializer.Deserialize<JsonElement>(EmptyJsonObjectIfBlank(form["executionInput"].ToString()), JsonOptions());
        var execution = await runner.StartAsync(graphId, new ExecuteWorkflowRequest
        {
            ExecutionId = BlankToNull(form["executionId"].ToString()),
            Input = input,
            TriggeredBy = "hpdos-ui",
            Mode = WorkflowExecutionMode.Background,
            StartImmediately = true
        }, ct).ConfigureAwait(false);

        return Html(RenderRunPanel(graphId, execution.ExecutionId, JsonSerializer.Serialize(execution, JsonOptions()), "Execution started."));
    }
    catch (Exception ex)
    {
        return Html(RenderRunPanel(graphId, form["executionId"].ToString(), "No execution yet.", ex.Message));
    }
});

ui.MapGet("/workflows/{graphId}/status/{executionId}", async (string graphId, string executionId, ExecutionManager executionManager, CancellationToken ct) =>
{
    var status = await executionManager.GetStatusAsync(graphId, executionId, ct).ConfigureAwait(false);
    return Html(RenderRunPanel(graphId, executionId, status is null ? "Execution not found." : JsonSerializer.Serialize(status, JsonOptions()), null));
});

ui.MapPost("/workflows/{graphId}/cancel/{executionId}", async (string graphId, string executionId, ExecutionManager executionManager, CancellationToken ct) =>
{
    try
    {
        await executionManager.CancelAsync(graphId, executionId, ct).ConfigureAwait(false);
        var status = await executionManager.GetStatusAsync(graphId, executionId, ct).ConfigureAwait(false);
        return Html(RenderRunPanel(graphId, executionId, status is null ? "Cancelled." : JsonSerializer.Serialize(status, JsonOptions()), "Execution cancelled."));
    }
    catch (Exception ex)
    {
        return Html(RenderRunPanel(graphId, executionId, "No execution yet.", ex.Message));
    }
});

ui.MapPost("/workflows/{graphId}/schedule", async (string graphId, HttpRequest request, SchedulingManager schedulingManager, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
    var schedule = new GraphScheduleConfig
    {
        CronExpression = form["cronExpression"].ToString(),
        TimeZoneId = string.IsNullOrWhiteSpace(form["timeZoneId"]) ? "UTC" : form["timeZoneId"].ToString()
    };
    var enabled = form["enabled"].ToString() != "false";
    try
    {
        var saved = await schedulingManager.GetScheduleAsync(graphId, ct).ConfigureAwait(false) is null
            ? await schedulingManager.CreateScheduleAsync(graphId, new CreateScheduleRequest { Schedule = schedule, Enabled = enabled }, ct).ConfigureAwait(false)
            : await schedulingManager.UpdateScheduleAsync(graphId, new UpdateScheduleRequest { Schedule = schedule, Enabled = enabled }, ct).ConfigureAwait(false);
        return Html(RenderSchedulePanel(graphId, saved, "Schedule saved."));
    }
    catch (Exception ex)
    {
        return Html(RenderSchedulePanel(graphId, null, ex.Message));
    }
});

ui.MapDelete("/workflows/{graphId}/schedule", async (string graphId, SchedulingManager schedulingManager, CancellationToken ct) =>
{
    await schedulingManager.DeleteScheduleAsync(graphId, ct).ConfigureAwait(false);
    return Html(RenderSchedulePanel(graphId, null, "Schedule deleted."));
});

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
        if (File.Exists(Path.Combine(directory.FullName, "appsettings.json"))
            && Directory.Exists(Path.Combine(directory.FullName, "wwwroot")))
        {
            return directory.FullName;
        }

        if (File.Exists(Path.Combine(directory.FullName, "backend.cs"))
            && File.Exists(Path.Combine(directory.FullName, "appsettings.json")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return currentDirectory;
}
