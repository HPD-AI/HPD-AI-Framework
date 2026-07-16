using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Sandbox.Local;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

var appsettingsPath = ResolveAppSettingsPath();
var options = CodingCliOptions.Parse(args);
if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

var agentBuilder = CreateAgentBuilder(options.ConfigPath)
    .WithAPIConfiguration(appsettingsPath ?? "appsettings.json", optional: true)
    .WithLocalSandbox()
    .WithHarnessCollapsing()
    .WithToolHarness<CodingToolHarness>();

ConfigureDefaultCompaction(agentBuilder);

var sessionStorePath = ResolveProjectSessionStorePath();
agentBuilder.WithSessionStore(sessionStorePath);

if (string.IsNullOrWhiteSpace(options.ConfigPath))
{
    agentBuilder.WithName("Coding CLI Test Agent");
}

if (!TryConfigureProvider(agentBuilder, options, appsettingsPath, out var providerError))
{
    Console.Error.WriteLine(providerError);
    return 2;
}

using var loggerFactory = options.EnableLogging
    ? LoggerFactory.Create(logging =>
    {
        logging.SetMinimumLevel(options.VerboseLogging ? LogLevel.Trace : LogLevel.Information);
        logging.AddProvider(new CodingCliLoggerProvider(options.VerboseLogging));
    })
    : null;

if (options.EnableLogging)
{
    Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));
    Trace.AutoFlush = true;
}

if (loggerFactory is not null)
{
    agentBuilder.WithLogging(
        loggerFactory,
        options: options.VerboseLogging
            ? LoggingMiddlewareOptions.Verbose
            : LoggingMiddlewareOptions.Minimal);
}

var agent = await agentBuilder.BuildAsync();
var tools = agent.DefaultOptions?.Tools?.OfType<AIFunction>().Select(tool => tool.Name).Order().ToArray() ?? [];

if (options.ListTools)
{
    Console.WriteLine($"Agent: {agent.Name}");
    Console.WriteLine("Coding toolharness tools:");
    foreach (var tool in tools)
        Console.WriteLine($"- {tool}");

    return 0;
}

using var turnStartedSubscription = agent.Subscribe<MessageTurnStartedEvent>(evt =>
{
    CliConsole.WriteErrorLine(
        ConsoleColor.Blue,
        $"[turn:start] {evt.AgentName} {evt.MessageTurnId}");
});
// using var contextSnapshotSubscription = agent.Subscribe<IterationContextSnapshotEvent>(evt =>
// {
//     CliConsole.WriteErrorLine(
//         ConsoleColor.DarkCyan,
//         $"[context] iteration={evt.Iteration} injected_messages={evt.ContextMessageCount} tools={evt.ToolCount} total_model_messages={evt.TotalMessageCount}");
//
//     if (!string.IsNullOrWhiteSpace(evt.Instructions))
//     {
//         CliConsole.WriteErrorLine(
//             ConsoleColor.DarkCyan,
//             $"[context:instructions] {NormalizeSingleLine(evt.Instructions)}");
//     }
//
//     foreach (var message in evt.ContextMessages)
//     {
//         CliConsole.WriteErrorLine(
//             ConsoleColor.DarkCyan,
//             $"[context:message] role={message.Role} text={Preview(message.Text)}");
//     }
//
//     foreach (var tool in evt.Tools)
//     {
//         CliConsole.WriteErrorLine(
//             ConsoleColor.DarkCyan,
//             $"[context:tool] {tool.Name} toolharness={tool.ToolHarnessName ?? "-"} type={tool.CallType?.ToString() ?? "-"} container={tool.IsContainer} schema={Preview(tool.InputSchemaJson)}");
//     }
// });
// using var middlewareStateSnapshotSubscription = agent.Subscribe<MiddlewareStateSnapshotEvent>(evt =>
// {
//     CliConsole.WriteErrorLine(
//         ConsoleColor.DarkMagenta,
//         $"[state] phase={evt.Phase} iteration={evt.Iteration} states={evt.StateCount} batch={evt.BatchId ?? "-"} call={evt.FunctionCallId ?? "-"}");
//
//     foreach (var state in evt.States)
//     {
//         CliConsole.WriteErrorLine(
//             ConsoleColor.DarkMagenta,
//             $"[state:item] {state.PropertyName} scope={state.Scope} persistent={state.Persistent} version={state.Version} key={state.Key} json={Preview(state.Json?.GetRawText())} error={state.Error ?? "-"}");
//     }
// });
// using var middlewareStateChangedSubscription = agent.Subscribe<MiddlewareStateChangedEvent>(evt =>
// {
//     CliConsole.WriteErrorLine(
//         ConsoleColor.Magenta,
//         $"[state:changed] phase={evt.Phase} iteration={evt.Iteration} changes={evt.ChangeCount} batch={evt.BatchId ?? "-"} call={evt.FunctionCallId ?? "-"}");
//
//     foreach (var change in evt.Changes)
//     {
//         CliConsole.WriteErrorLine(
//             ConsoleColor.Magenta,
//             $"[state:change] {change.ChangeType} {change.PropertyName} scope={change.Scope} persistent={change.Persistent} before={Preview(change.Before?.GetRawText())} after={Preview(change.After?.GetRawText())} error={change.Error ?? "-"}");
//     }
// });
using var textSubscription = agent.Subscribe<TextDeltaEvent>(evt =>
{
    CliConsole.Write(Console.Out, ConsoleColor.Gray, evt.Text);
});
using var reasoningSubscription = agent.Subscribe<ReasoningDeltaEvent>(evt =>
{
    CliConsole.Write(Console.Error, ConsoleColor.DarkGray, evt.Text);
});
using var turnFinishedSubscription = agent.Subscribe<MessageTurnFinishedEvent>(evt =>
{
    CliConsole.WriteLine(Console.Out, ConsoleColor.Gray, string.Empty);
    CliConsole.WriteErrorLine(
        ConsoleColor.Blue,
        $"[turn:end] {evt.AgentName} {evt.MessageTurnId} duration={evt.Duration.TotalMilliseconds:0}ms");
});
using var compactionSubscription = agent.Subscribe<CompactionEvent>(evt =>
{
    CliConsole.WriteErrorLine(
        ConsoleColor.DarkCyan,
        $"[compact:{evt.Status}] origin={evt.Origin} continuation={evt.Continuation} strategy={evt.Strategy} " +
        $"compacted={evt.CompactedMessageCount?.ToString() ?? "-"} removed={evt.MessagesRemoved?.ToString() ?? "-"} " +
        $"reason={evt.Reason ?? "-"}");
});
using var compactionCheckpointSubscription = agent.Subscribe<ThreadHistoryCompactionCheckpointEvent>(evt =>
{
    CliConsole.WriteErrorLine(
        ConsoleColor.DarkCyan,
        $"[compact:checkpoint] id={evt.CompactionId} removed={evt.CompactedMessageIds.Count} mode={evt.CommitMode} summary={Preview(evt.ReplacementMessages.FirstOrDefault()?.Text, 240)}");
});
using var toolStartSubscription = agent.Subscribe<ToolCallStartEvent>(evt =>
{
    CliConsole.WriteErrorLine(
        ConsoleColor.Cyan,
        $"[tool:start] {evt.Name} call_id={evt.CallId} toolharness={evt.ToolHarnessName ?? "-"} type={evt.CallType?.ToString() ?? "-"}");
});
using var toolArgsSubscription = agent.Subscribe<ToolCallArgsEvent>(evt =>
{
    CliConsole.WriteErrorLine(
        ConsoleColor.Yellow,
        $"[tool:args] call_id={evt.CallId} args={evt.ArgsJson}");
});
using var toolResultSubscription = agent.Subscribe<ToolCallResultEvent>(evt =>
{
    CliConsole.WriteErrorLine(
        ConsoleColor.Green,
        $"[tool:result] call_id={evt.CallId} toolharness={evt.ToolHarnessName ?? "-"} type={evt.CallType?.ToString() ?? "-"}");
    CliConsole.WriteErrorLine(ConsoleColor.DarkGreen, FormatToolResult(evt.Result));
});
using var toolEndSubscription = agent.Subscribe<ToolCallEndEvent>(evt =>
{
    CliConsole.WriteErrorLine(ConsoleColor.Magenta, $"[tool:end] call_id={evt.CallId}");
});
using var errorSubscription = agent.SubscribeAny(evt =>
{
    if (evt is not IErrorEvent error)
        return;

    CliConsole.WriteErrorLine(
        ConsoleColor.Red,
        $"[error] {evt.GetType().Name}: {error.ErrorMessage}");

    if (error.Exception is not null)
    {
        CliConsole.WriteErrorLine(
            ConsoleColor.DarkRed,
            $"{error.Exception.GetType().Name}: {error.Exception.Message}");
    }
});

await EnsureSessionAsync(agent, options.SessionId);
CliConsole.WriteErrorLine(
    ConsoleColor.DarkCyan,
    $"Interactive coding CLI started. session={options.SessionId} thread={options.ThreadId}. Type compact, exit, or quit.");
CliConsole.WriteErrorLine(
    ConsoleColor.DarkCyan,
    $"Session store: {sessionStorePath}");
CliConsole.WriteErrorLine(
    ConsoleColor.DarkCyan,
    "Execution profile: local HPD sandbox backend");

var prompt = options.Prompt;
while (true)
{
    if (string.IsNullOrWhiteSpace(prompt))
    {
        CliConsole.Write(Console.Out, ConsoleColor.White, "You> ");
        prompt = Console.ReadLine();
    }

    if (prompt is null)
    {
        CliConsole.WriteLine(Console.Out, ConsoleColor.Gray, string.Empty);
        break;
    }

    if (string.Equals(prompt.Trim(), "exit", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(prompt.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (IsCompactCommand(prompt, out var hardRetention))
    {
        try
        {
            await RunManualCompactionAsync(agent, options, hardRetention);
        }
        catch (Exception ex)
        {
            CliConsole.WriteErrorLine(ConsoleColor.Red, $"[compact:error] {ex.Message}");
            if (ex.InnerException is not null)
                CliConsole.WriteErrorLine(ConsoleColor.DarkRed, $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }

        prompt = null;
        continue;
    }

    if (!string.IsNullOrWhiteSpace(prompt))
    {
        try
        {
            await agent.RunAsync(prompt, sessionId: options.SessionId, threadId: options.ThreadId);
        }
        catch (Exception ex)
        {
            CliConsole.WriteErrorLine(ConsoleColor.Red, $"[run:error] {ex.Message}");
            if (ex.InnerException is not null)
                CliConsole.WriteErrorLine(ConsoleColor.DarkRed, $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
    }

    prompt = null;
}

return 0;

static void ConfigureDefaultCompaction(AgentBuilder builder)
{
    builder.Config.Compaction ??= new CompactionConfig();
    builder.Config.Compaction.Automatic = new AutomaticCompactionPolicy
    {
        Trigger = new TurnCountCompactionTrigger(2),
        Compaction = new CompactionSpecification
        {
            Point = new CompactAtCurrentHead(),
            Preservation = new PreserveNoPreviousHistory(),
            Strategy = new SummarizingCompaction(),
            CommitMode = CompactionCommitMode.Hard
        },
        Continuation = CompactionContinuation.Continue
    };
}

static bool IsCompactCommand(string prompt, out bool hardRetention)
{
    hardRetention = true;
    var parts = prompt.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0 || !string.Equals(parts[0], "compact", StringComparison.OrdinalIgnoreCase))
        return false;

    if (parts.Length > 1 && string.Equals(parts[1], "soft", StringComparison.OrdinalIgnoreCase))
        hardRetention = false;

    return true;
}

static async Task RunManualCompactionAsync(
    Agent agent,
    CodingCliOptions options,
    bool hardRetention)
{
    var runConfig = new AgentRunConfig();
    var request = new ThreadCompactionRequest
    {
        Continuation = CompactionContinuation.StopAfterCompaction,
        Compaction = new CompactionSpecification
        {
            Point = new CompactAtCurrentHead(),
            Preservation = new PreserveNoPreviousHistory(),
            Strategy = new SummarizingCompaction(),
            CommitMode = hardRetention ? CompactionCommitMode.Hard : CompactionCommitMode.Soft
        }
    };

    CliConsole.WriteErrorLine(
        ConsoleColor.DarkCyan,
        $"[compact:manual] retention={(hardRetention ? "hard" : "soft")} behavior=StopAfterCompaction");

    await agent.RunAsync(new CompactThreadInputEvent
    {
        SessionId = options.SessionId,
        ThreadId = options.ThreadId,
        RunConfig = runConfig,
        Request = request
    });
}

static async Task EnsureSessionAsync(Agent agent, string sessionId)
{
    try
    {
        await agent.CreateSessionAsync(sessionId);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
    {
        // Reuse the existing CLI session when backed by a persistent session store.
    }
}

static bool TryConfigureProvider(
    AgentBuilder builder,
    CodingCliOptions options,
    string? appsettingsPath,
    out string? error)
{
    var chatConfig = builder.Config.ResolveClientConfig(HPD.Agent.Providers.ProviderClientFamily.Chat);
    if (chatConfig is not null)
    {
        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            builder.Config.EnsureChatClientConfig().ModelName = options.Model;
        }

        error = null;
        return true;
    }

    var apiKey = ResolveOpenRouterApiKey(appsettingsPath);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        error = """
        OpenRouter API key is required.

        Set OPENROUTER_API_KEY, or add this to appsettings.json:
          "Providers": { "openrouter": { "ProviderKey": "openrouter", "ModelName": "deepseek/deepseek-v4-pro", "ApiKey": "..." } }

        """;
        return false;
    }

    builder.Config.SetChatClientConfig(new ClientProviderConfig
    {
        ProviderKey = "openrouter",
        ModelName = options.Model
            ?? System.Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
            ?? "deepseek/deepseek-v4-pro",
        ApiKey = apiKey
    });

    error = null;
    return true;
}

static AgentBuilder CreateAgentBuilder(string? configPath)
{
    if (string.IsNullOrWhiteSpace(configPath))
        return new AgentBuilder();

    var config = HpdAgentConfigSerializer.ReadFile(configPath)
        ?? throw new InvalidOperationException($"Failed to load agent config from {configPath}.");

    return new AgentBuilder(config);
}

static string? ResolveOpenRouterApiKey(string? appsettingsPath)
{
    var environmentKey = System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
    if (!string.IsNullOrWhiteSpace(environmentKey))
        return environmentKey;

    if (string.IsNullOrWhiteSpace(appsettingsPath) || !File.Exists(appsettingsPath))
        return null;

    using var stream = File.OpenRead(appsettingsPath);
    using var document = JsonDocument.Parse(stream);
    var root = document.RootElement;

    if (TryGetProperty(root, "Providers", out var providers) &&
        TryGetProperty(providers, "openrouter", out var openRouter) &&
        TryGetStringProperty(openRouter, "ApiKey", out var providerApiKey))
    {
        return providerApiKey;
    }

    return null;
}

static string? ResolveAppSettingsPath()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
        Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
        Path.Combine(AppContext.BaseDirectory, "../../../appsettings.json")
    };

    return candidates.FirstOrDefault(File.Exists);
}

static string ResolveProjectSessionStorePath()
{
    var root = ResolveProjectRoot() ?? Directory.GetCurrentDirectory();
    return Path.Combine(root, ".hpd-agent-coding-cli-sessions");
}

static string? ResolveProjectRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "HPDOS.slnx")))
            return current.FullName;

        current = current.Parent;
    }

    current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "HPDOS.slnx")))
            return current.FullName;

        current = current.Parent;
    }

    return null;
}

static bool TryGetStringProperty(JsonElement element, string name, out string value)
{
    value = string.Empty;
    if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.String)
        return false;

    value = property.GetString() ?? string.Empty;
    return !string.IsNullOrWhiteSpace(value);
}

static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
{
    foreach (var property in element.EnumerateObject())
    {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            value = property.Value;
            return true;
        }
    }

    value = default;
    return false;
}

static void PrintUsage()
{
    Console.WriteLine("""
    HPD coding toolharness test CLI

    Usage:
      dotnet run --project test/HPD-Agent.CodingCli -- --model deepseek/deepseek-v4-pro "Read README.md"
      dotnet run --project test/HPD-Agent.CodingCli -- --config coding-agent.yaml --list-tools
      dotnet run --project test/HPD-Agent.CodingCli
      dotnet run --project test/HPD-Agent.CodingCli -- --log

    Interactive commands:
      compact           Force hard compaction and stop before the next model turn.
      compact hard      Same as compact.
      compact soft      Force model-visible compaction while preserving durable history.

    Options:
      --config VALUE     JSON/YAML AgentConfig file to load before CLI defaults are applied.
      --list-tools       Print registered coding toolharness tool names.
      --log              Enable normal ILogger output from .WithLogging().
      --log-verbose      Enable verbose ILogger output from .WithLogging().
      --model VALUE      OpenRouter model id. Defaults to OPENROUTER_MODEL or deepseek/deepseek-v4-pro.
      --session VALUE    Session id. Defaults to coding-cli-session.
      --thread VALUE     Thread id. Defaults to main.
      --help             Show this help.
    """);
}

static string FormatToolResult(ToolResultPayload result) =>
    result.Text ?? result.Json?.GetRawText() ?? string.Empty;

static string Preview(string? value, int maxLength = 500)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    var normalized = NormalizeSingleLine(value);

    return normalized.Length <= maxLength
        ? normalized
        : $"{normalized[..maxLength]}...";
}

static string NormalizeSingleLine(string value) =>
    value
        .ReplaceLineEndings(" ")
        .Trim();

file sealed record CodingCliOptions(
    string? Prompt,
    string? ConfigPath,
    string? Model,
    string SessionId,
    string ThreadId,
    bool ListTools,
    bool EnableLogging,
    bool VerboseLogging,
    bool ShowHelp)
{
    public static CodingCliOptions Parse(string[] args)
    {
        string? prompt = null;
        string? configPath = null;
        string? model = null;
        var sessionId = "coding-cli-session";
        var threadId = "main";
        var listTools = false;
        var enableLogging = false;
        var verboseLogging = false;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--list-tools":
                    listTools = true;
                    break;
                case "--log":
                    enableLogging = true;
                    break;
                case "--log-verbose":
                    enableLogging = true;
                    verboseLogging = true;
                    break;
                case "--config" when i + 1 < args.Length:
                    configPath = args[++i];
                    break;
                case "--model" when i + 1 < args.Length:
                    model = args[++i];
                    break;
                case "--session" when i + 1 < args.Length:
                    sessionId = args[++i];
                    break;
                case "--thread" when i + 1 < args.Length:
                    threadId = args[++i];
                    break;
                default:
                    prompt = prompt is null ? args[i] : $"{prompt} {args[i]}";
                    break;
            }
        }

        return new CodingCliOptions(prompt, configPath, model, sessionId, threadId, listTools, enableLogging, verboseLogging, showHelp);
    }
}

file sealed class CodingCliLoggerProvider(bool verbose) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new CodingCliLogger(categoryName, verbose);

    public void Dispose()
    {
    }
}

file sealed class CodingCliLogger(string categoryName, bool verbose) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (IsNoisyCategory(categoryName))
            return logLevel >= LogLevel.Warning;

        return verbose || logLevel >= LogLevel.Information;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
            return;

        var color = logLevel switch
        {
            LogLevel.Trace => ConsoleColor.DarkGray,
            LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Information => ConsoleColor.DarkBlue,
            LogLevel.Warning => ConsoleColor.DarkYellow,
            LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
            _ => ConsoleColor.DarkBlue
        };

        CliConsole.WriteErrorLine(
            color,
            $"[log:{logLevel}] {categoryName}: {Normalize(message)}");
        if (exception is not null)
        {
            CliConsole.WriteErrorLine(
                ConsoleColor.DarkRed,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string Normalize(string value)
        => value
            .ReplaceLineEndings(" ")
            .Trim();

    private static bool IsNoisyCategory(string category)
        => string.Equals(category, "HPD.Agent.Middleware.LoggingMiddleware", StringComparison.Ordinal) ||
           string.Equals(category, "HPD.Agent.LoggingEventObserver", StringComparison.Ordinal);
}

file static class CliConsole
{
    private static readonly object Sync = new();

    public static void Write(TextWriter writer, ConsoleColor color, string text)
    {
        lock (Sync)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            writer.Write(text);
            Console.ForegroundColor = previous;
        }
    }

    public static void WriteLine(TextWriter writer, ConsoleColor color, string text)
    {
        lock (Sync)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            writer.WriteLine(text);
            Console.ForegroundColor = previous;
        }
    }

    public static void WriteErrorLine(ConsoleColor color, string text)
        => WriteLine(Console.Error, color, text);
}
