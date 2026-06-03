using HPD.Agent;
using HPD.Execution.Local;
using Microsoft.Extensions.AI;
using System.Text.Json;

var appsettingsPath = ResolveAppSettingsPath();
var options = CodingCliOptions.Parse(args);
if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

var agentBuilder = new AgentBuilder()
    .WithAPIConfiguration(appsettingsPath ?? "appsettings.json", optional: true)
    .WithName("Coding CLI Test Agent")
    .WithLocalExecution()
    .WithHarnessCollapsing()
    .WithToolHarness<CodingToolHarness>();

if (!TryConfigureProvider(agentBuilder, options, appsettingsPath, out var providerError))
{
    Console.Error.WriteLine(providerError);
    return 2;
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
    $"Interactive coding CLI started. session={options.SessionId} branch={options.BranchId}. Type exit or quit to leave.");
CliConsole.WriteErrorLine(
    ConsoleColor.DarkCyan,
    "Execution profile: local HPD Execution providers");

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

    if (!string.IsNullOrWhiteSpace(prompt))
    {
        try
        {
            await agent.RunAsync(prompt, sessionId: options.SessionId, branchId: options.BranchId);
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
    var apiKey = ResolveOpenRouterApiKey(appsettingsPath);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        error = """
        OpenRouter API key is required.

        Set OPENROUTER_API_KEY, or add one of these to appsettings.json:
          "Providers": { "Openrouter": { "ProviderKey": "openrouter", "ModelName": "deepseek/deepseek-v4-pro", "ApiKey": "..." } }
          "openrouter": { "ApiKey": "..." }

        """;
        return false;
    }

    builder.Config.SetChatClientConfig(new ClientProviderConfig
    {
        ProviderKey = "openrouter",
        ModelName = options.Model
            ?? Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
            ?? "deepseek/deepseek-v4-pro",
        ApiKey = apiKey
    });

    error = null;
    return true;
}

static string? ResolveOpenRouterApiKey(string? appsettingsPath)
{
    var environmentKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
    if (!string.IsNullOrWhiteSpace(environmentKey))
        return environmentKey;

    if (string.IsNullOrWhiteSpace(appsettingsPath) || !File.Exists(appsettingsPath))
        return null;

    using var stream = File.OpenRead(appsettingsPath);
    using var document = JsonDocument.Parse(stream);
    var root = document.RootElement;

    if (TryGetProperty(root, "Providers", out var providers) &&
        TryGetProperty(providers, "Openrouter", out var openRouter) &&
        TryGetStringProperty(openRouter, "ApiKey", out var providerApiKey))
    {
        return providerApiKey;
    }

    if (TryGetProperty(root, "openrouter", out var rootOpenRouter) &&
        TryGetStringProperty(rootOpenRouter, "ApiKey", out var rootProviderApiKey))
    {
        return rootProviderApiKey;
    }

    if (TryGetProperty(root, "ConnectionStrings", out var connectionStrings) &&
        TryGetStringProperty(connectionStrings, "Agent", out var connectionString))
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], "AccessKey", StringComparison.OrdinalIgnoreCase))
                return parts[1];
        }
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
      dotnet run --project test/HPD-Agent.CodingCli

    Options:
      --list-tools       Print registered coding toolharness tool names.
      --model VALUE      OpenRouter model id. Defaults to OPENROUTER_MODEL or deepseek/deepseek-v4-pro.
      --session VALUE    Session id. Defaults to coding-cli-session.
      --branch VALUE     Branch id. Defaults to main.
      --help             Show this help.
    """);
}

static string FormatToolResult(ToolResultPayload result) =>
    result.Text ?? result.Json?.GetRawText() ?? string.Empty;

// static string Preview(string? value, int maxLength = 500)
// {
//     if (string.IsNullOrWhiteSpace(value))
//         return string.Empty;
//
//     var normalized = NormalizeSingleLine(value);
//
//     return normalized.Length <= maxLength
//         ? normalized
//         : $"{normalized[..maxLength]}...";
// }
//
// static string NormalizeSingleLine(string value) =>
//     value
//         .ReplaceLineEndings(" ")
//         .Trim();

file sealed record CodingCliOptions(
    string? Prompt,
    string? Model,
    string SessionId,
    string BranchId,
    bool ListTools,
    bool ShowHelp)
{
    public static CodingCliOptions Parse(string[] args)
    {
        string? prompt = null;
        string? model = null;
        var sessionId = "coding-cli-session";
        var branchId = "main";
        var listTools = false;
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
                case "--model" when i + 1 < args.Length:
                    model = args[++i];
                    break;
                case "--session" when i + 1 < args.Length:
                    sessionId = args[++i];
                    break;
                case "--branch" when i + 1 < args.Length:
                    branchId = args[++i];
                    break;
                default:
                    prompt = prompt is null ? args[i] : $"{prompt} {args[i]}";
                    break;
            }
        }

        return new CodingCliOptions(prompt, model, sessionId, branchId, listTools, showHelp);
    }
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
