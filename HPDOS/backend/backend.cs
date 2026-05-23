using HPD.Agent;
using HPD.Agent.AspNetCore;
using HPD.Execution.Local;


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

var projectContext = HpdosProjectContext.Resolve(builder.Configuration, backendDirectory);

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

builder.Services.AddSingleton(projectContext);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

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

app.MapGroup("/api/hpd-agent").MapHPDAgentApi("hpdos");

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
