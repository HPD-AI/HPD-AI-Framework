using HPD.Agent;
using HPD.Agent.AspNetCore;
using HPD.Execution.Local;
using Microsoft.AspNetCore.Mvc;


var backendDirectory = FindBackendDirectory();
TraceStartup("backend directory resolved");
Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE",
    Environment.GetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE") ?? "false");

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = backendDirectory
});
builder.Configuration
    .SetBasePath(backendDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
TraceStartup("web builder created");

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

var projectContext = HpdosProjectContext.Resolve(builder.Configuration, backendDirectory);
TraceStartup("project context resolved");

builder.Services.AddHPDAgent("hpdos", options =>
{
    options.AgentStore = new JsonAgentStore(agentStorePath);
    options.SessionStorePath = sessionStorePath;
    options.PersistAfterTurn = true;
    options.PersistAgentDefinitionsOnBuild = true;
    options.ConfigureAgent = agent =>
    {
        agent.WithAPIConfiguration(builder.Configuration)
            .WithLocalExecution();
    };
});
TraceStartup("agent services configured");

builder.Services.AddSingleton(projectContext);
builder.Services.AddSingleton(new HpdosWorkspaceStoreService(dataRoot, projectContext));
builder.Services.AddSingleton<HpdosWorkspaceFileService>();
builder.Services.AddSingleton<HpdosTerminalService>();
TraceStartup("hpdos services configured");

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

var app = builder.Build();
TraceStartup("web app built");

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "hpdos-backend"
}));

app.MapGet("/api/hpdos/runtime", () => Results.Ok(new
{
    service = "hpdos-backend",
    agentApi = "/api/hpd-agent",
    project = projectContext
}));

app.MapGet("/api/hpdos/workspaces", async (HpdosWorkspaceStoreService workspaces, CancellationToken ct) =>
    Results.Ok(await workspaces.GetAsync(ct)));

app.MapPut("/api/hpdos/workspaces", async (HpdosWorkspaceStore store, HpdosWorkspaceStoreService workspaces, CancellationToken ct) =>
    Results.Ok(await workspaces.SaveAsync(store, ct)));

app.MapGet("/api/hpdos/workspace/files", async (
    string? rootId,
    string? path,
    HpdosWorkspaceFileService files,
    CancellationToken ct) =>
    await ToWorkspaceFileResult(() => files.ListAsync(rootId, path, ct)));

app.MapGet("/api/hpdos/workspace/find-files", async (
    string? rootId,
    string? query,
    string? type,
    int? limit,
    HpdosWorkspaceFileService files,
    CancellationToken ct) =>
    await ToWorkspaceFileResult(() => files.SearchAsync(rootId, query, type, limit, ct)));

app.MapGet("/api/hpdos/workspace/file", async (
    string? rootId,
    string? path,
    HpdosWorkspaceFileService files,
    CancellationToken ct) =>
    await ToWorkspaceFileResult(() => files.ReadAsync(rootId, path, ct)));

app.MapGet("/api/hpdos/terminals/shells", (HpdosTerminalService terminals) =>
    Results.Ok(terminals.Shells()));

app.MapGet("/api/hpdos/terminals", async (HpdosTerminalService terminals, CancellationToken ct) =>
    await ToTerminalResult(() => terminals.ListAsync(ct)));

app.MapGet("/api/hpdos/terminals/{id}", async (string id, HpdosTerminalService terminals, CancellationToken ct) =>
    await terminals.GetAsync(id, ct) is { } terminal
        ? Results.Ok(terminal)
        : Results.NotFound(new { error = "Unknown terminal." }));

app.MapPost("/api/hpdos/terminals", async (
    [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] HpdosCreateTerminalRequest? request,
    HpdosTerminalService terminals,
    CancellationToken ct) =>
    await ToTerminalResult(() => terminals.CreateAsync(request ?? new HpdosCreateTerminalRequest(null, null, null, null, null, null, null), ct)));

app.MapPatch("/api/hpdos/terminals/{id}", async (
    string id,
    HpdosUpdateTerminalRequest request,
    HpdosTerminalService terminals,
    CancellationToken ct) =>
    await terminals.UpdateAsync(id, request, ct) is { } terminal
        ? Results.Ok(terminal)
        : Results.NotFound(new { error = "Unknown terminal." }));

app.MapPost("/api/hpdos/terminals/{id}/resize", async (
    string id,
    int cols,
    int rows,
    HpdosTerminalService terminals,
    CancellationToken ct) =>
    await ToTerminalBoolResult(() => terminals.ResizeAsync(id, cols, rows, ct)));

app.MapPost("/api/hpdos/terminals/{id}/connect-token", async (string id, HpdosTerminalService terminals, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await terminals.CreateConnectTokenAsync(id, ct));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Unknown terminal." });
    }
});

app.MapDelete("/api/hpdos/terminals/{id}", async (string id, HpdosTerminalService terminals, CancellationToken ct) =>
    await ToTerminalBoolResult(() => terminals.DeleteAsync(id, ct)));

app.MapGet("/api/hpdos/terminals/{id}/connect", async (
    string id,
    string? ticket,
    long? cursor,
    HpdosTerminalService terminals,
    HttpContext context,
    CancellationToken ct) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
        return Results.BadRequest(new { error = "Expected a websocket request." });

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await terminals.ConnectAsync(id, ticket, cursor, socket, ct);
    return Results.Empty;
});

app.MapGroup("/api/hpd-agent").MapHPDAgentApi("hpdos");
TraceStartup("endpoints mapped");

app.Run();

static async Task<IResult> ToWorkspaceFileResult<T>(Func<Task<T>> work)
{
    try
    {
        return Results.Ok(await work());
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (IOException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
}

static async Task<IResult> ToTerminalResult<T>(Func<Task<T>> work)
{
    try
    {
        return Results.Ok(await work());
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (IOException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
}

static async Task<IResult> ToTerminalBoolResult(Func<Task<bool>> work)
{
    try
    {
        return await work()
            ? Results.NoContent()
            : Results.NotFound(new { error = "Unknown terminal." });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (IOException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
}

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

static void TraceStartup(string message)
{
    if (Environment.GetEnvironmentVariable("HPDOS__StartupTrace") == "true")
        Console.Error.WriteLine($"[HPDOS startup] {message}");
}
