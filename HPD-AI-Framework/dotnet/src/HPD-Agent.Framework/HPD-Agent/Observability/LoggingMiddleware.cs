using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HPD.Agent;

namespace HPD.Agent.Middleware;

/// <summary>
/// Configuration options for the unified logging middleware.
/// Control what gets logged at each lifecycle stage.
/// </summary>
public class LoggingMiddlewareOptions
{
    /// <summary>
    /// Log at message turn level (before/after user message processing).
    /// Includes: agent name, message count, instructions.
    /// </summary>
    public bool LogMessageTurn { get; set; } = true;

    /// <summary>
    /// Log at iteration level (before/after each LLM call).
    /// Includes: iteration number, tool call count, response preview.
    /// </summary>
    public bool LogIteration { get; set; } = true;

    /// <summary>
    /// Log at function level (before/after each function execution).
    /// Includes: function name, arguments, results, timing.
    /// </summary>
    public bool LogFunction { get; set; } = true;

    /// <summary>
    /// Include timing information for function calls.
    /// Only applies when LogFunction is true.
    /// </summary>
    public bool IncludeTiming { get; set; } = true;

    /// <summary>
    /// Include full arguments in function logs.
    /// When false, only logs function name.
    /// </summary>
    public bool IncludeArguments { get; set; } = true;

    /// <summary>
    /// Include full results in function logs.
    /// When false, only logs success/failure.
    /// </summary>
    public bool IncludeResults { get; set; } = true;

    /// <summary>
    /// Include full instructions in message turn logs.
    /// When false, only logs instruction length.
    /// </summary>
    public bool IncludeInstructions { get; set; } = true;

    /// <summary>
    /// Maximum length for logged strings (arguments, results, instructions).
    /// 0 = unlimited.
    /// </summary>
    public int MaxStringLength { get; set; } = 1000;

    /// <summary>
    /// Log toolkit expansion and collapse events (when a [Collapse] toolkit is called by the LLM).
    /// Includes: toolkit name, functions being expanded into scope, timing.
    /// </summary>
    public bool LogToolkitExpansion { get; set; } = true;

    /// <summary>
    /// Prefix for all log messages.
    /// </summary>
    public string LogPrefix { get; set; } = "[HPD-Agent]";

    /// <summary>
    /// Creates default options that log message turns and functions with full details.
    /// </summary>
    public static LoggingMiddlewareOptions Default => new();

    /// <summary>
    /// Creates minimal options that only log function names (no args/results).
    /// </summary>
    public static LoggingMiddlewareOptions Minimal => new()
    {
        LogMessageTurn = false,
        LogIteration = false,
        LogFunction = true,
        IncludeArguments = false,
        IncludeResults = false,
        IncludeTiming = true
    };

    /// <summary>
    /// Creates verbose options that log everything.
    /// </summary>
    public static LoggingMiddlewareOptions Verbose => new()
    {
        LogMessageTurn = true,
        LogIteration = true,
        LogFunction = true,
        IncludeArguments = true,
        IncludeResults = true,
        IncludeTiming = true,
        IncludeInstructions = true,
        MaxStringLength = 0 // unlimited
    };
}

/// <summary>
/// Unified logging middleware that consolidates all logging concerns.
/// Configurable to log at message turn, iteration, and/or function levels.
/// </summary>
/// <remarks>
/// <para>Replaces the following separate middlewares:</para>
/// <list type="bullet">
/// <item><c>LoggingAIFunctionMiddleware</c> - Old IAIFunctionMiddleware for function logging</item>
/// <item><c>FunctionLoggingMiddleware</c> - IAgentMiddleware for function logging with timing</item>
/// <item><c>PromptLoggingAgentMiddleware</c> - IAgentMiddleware for instruction logging</item>
/// </list>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Default logging (message turns + functions)
/// builder.WithMiddleware(new LoggingMiddleware(loggerFactory));
///
/// // Minimal logging (just function names with timing)
/// builder.WithMiddleware(new LoggingMiddleware(loggerFactory, LoggingMiddlewareOptions.Minimal));
///
/// // Verbose logging (everything)
/// builder.WithMiddleware(new LoggingMiddleware(loggerFactory, LoggingMiddlewareOptions.Verbose));
///
/// // Custom configuration
/// builder.WithMiddleware(new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
/// {
///     LogFunction = true,
///     LogIteration = true,
///     IncludeArguments = false,
///     MaxStringLength = 500
/// }));
/// </code>
/// </remarks>
public class LoggingMiddleware : IAgentMiddleware
{
    private readonly ILogger? _logger;
    private readonly LoggingMiddlewareOptions _options;
    private readonly ConcurrentDictionary<string, System.Diagnostics.Stopwatch> _timers = new();
    private static int _turnCounter = 0;
    private static int _iterationCounter = 0;

    /// <summary>
    /// Creates a new logging middleware with default options.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory. If not provided, writes to Console.</param>
    public LoggingMiddleware(ILoggerFactory? loggerFactory = null)
        : this(loggerFactory, LoggingMiddlewareOptions.Default)
    {
    }

    /// <summary>
    /// Creates a new logging middleware with custom options.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory. If not provided, writes to Console.</param>
    /// <param name="options">Configuration options for what to log.</param>
    public LoggingMiddleware(ILoggerFactory? loggerFactory, LoggingMiddlewareOptions options)
    {
        _logger = loggerFactory?.CreateLogger<LoggingMiddleware>();
        _options = options ?? LoggingMiddlewareOptions.Default;
    }

    //     
    // MESSAGE TURN LEVEL
    //     

    /// <inheritdoc/>
    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (!_options.LogMessageTurn) return Task.CompletedTask;

        var turnNumber = Interlocked.Increment(ref _turnCounter);
        var sb = new StringBuilder();

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine($"{_options.LogPrefix} MESSAGE TURN #{turnNumber} - START");
        sb.AppendLine($"  Agent: {context.AgentName}");
        sb.AppendLine($"  UserMessage: {context.UserMessage?.Text?.Length ?? 0} chars");
        sb.AppendLine($"  ConversationHistory: {context.ConversationHistory?.Count ?? 0} messages");
        sb.AppendLine($"  ConversationId: {context.ConversationId}");

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════════════════");

        LogMessage(sb.ToString());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (!_options.LogMessageTurn) return Task.CompletedTask;

        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine($"{_options.LogPrefix} MESSAGE TURN - END");
        sb.AppendLine($"  Agent: {context.AgentName}");
        sb.AppendLine($"  Final response: {(context.FinalResponse != null ? "Yes" : "No")}");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════════════════");

        LogMessage(sb.ToString());
        return Task.CompletedTask;
    }

    //     
    // ITERATION LEVEL
    //     

    /// <inheritdoc/>
    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        if (!_options.LogIteration) return Task.CompletedTask;

        var iterationNumber = Interlocked.Increment(ref _iterationCounter);
        var sb = new StringBuilder();
        
        sb.AppendLine();
        sb.AppendLine("───────────────────────────────────────────────────────");
        sb.AppendLine($"{_options.LogPrefix} ITERATION #{iterationNumber} (Agent iteration: {context.Iteration})");
        sb.AppendLine($"  Agent: {context.AgentName}");

        // Show available tools
        if (context.Options?.Tools != null && context.Options.Tools.Count > 0)
        {
            sb.AppendLine($"  Available Tools ({context.Options.Tools.Count}):");
            foreach (var tool in context.Options.Tools)
            {
                if (tool is Microsoft.Extensions.AI.AIFunction func)
                {
                    sb.Append($"    - {func.Name}");
                    
                    // Show if it's a container
                    if (func.AdditionalProperties?.TryGetValue("IsContainer", out var isContainerVal) == true 
                        && isContainerVal is bool isContainer && isContainer)
                    {
                        sb.Append(" [CONTAINER]");
                    }
                    
                    sb.AppendLine();
                }
            }
        }

        if (_options.IncludeInstructions && !string.IsNullOrEmpty(context.Options?.Instructions))
        {
            var instructions = TruncateString(context.Options.Instructions);
            sb.AppendLine($"  System Instructions: {instructions}");
        }

        // Show additional instructions if present (e.g., plan mode, custom overrides)
        if (_options.IncludeInstructions && !string.IsNullOrEmpty(context.RunConfig?.AdditionalSystemInstructions))
        {
            var additional = TruncateString(context.RunConfig.AdditionalSystemInstructions);
            sb.AppendLine($"  Additional Instructions: {additional}");
        }

        // Show messages being sent to LLM (helps debug middleware injections like EnvironmentContextMiddleware)
        if (context.Messages != null && context.Messages.Count > 0)
        {
            sb.AppendLine($"  Messages to send to LLM: {context.Messages.Count}");
            // Show first 3 messages with truncated content
            foreach (var msg in context.Messages.Take(3))
            {
                var roleStr = msg.Role.ToString();
                var textPreview = msg.Text != null
                    ? (msg.Text.Length > 100 ? msg.Text.Substring(0, 100) + "..." : msg.Text)
                    : "<no text>";
                var contentCount = msg.Contents?.Count ?? 0;
                sb.AppendLine($"    - [{roleStr}] {textPreview} ({contentCount} content items)");
            }
            if (context.Messages.Count > 3)
            {
                sb.AppendLine($"    ... and {context.Messages.Count - 3} more messages");
            }
        }

        sb.AppendLine("───────────────────────────────────────────────────────");

        LogMessage(sb.ToString());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AfterIterationAsync(AfterIterationContext context, CancellationToken cancellationToken)
    {
        if (!_options.LogIteration) return Task.CompletedTask;

        var sb = new StringBuilder();

        sb.AppendLine("───────────────────────────────────────────────────────");
        sb.AppendLine($"{_options.LogPrefix} ITERATION END (Agent iteration: {context.Iteration})");
        sb.AppendLine($"  Tool calls executed: {context.ToolResults?.Count() ?? 0}");

        if (context.ToolResults?.Any() == true)
        {
            var errors = context.ToolResults.Count(r => r.Exception != null);
            if (errors > 0)
            {
                sb.AppendLine($"  Errors: {errors}");
            }
        }

        sb.AppendLine("───────────────────────────────────────────────────────");

        LogMessage(sb.ToString());
        return Task.CompletedTask;
    }

    //
    // FUNCTION LEVEL
    //

    /// <inheritdoc/>
    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        if (!_options.LogFunction && !_options.LogToolkitExpansion) return Task.CompletedTask;

        var functionName = context.Function?.Name ?? "<unknown>";
        var callId = context.FunctionCallId ?? Guid.NewGuid().ToString();

        // Start timing if enabled
        if (_options.IncludeTiming)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _timers[callId] = stopwatch;
        }

        var sb = new StringBuilder();
        sb.AppendLine("─────────────────────────────────────────────────────────────────────────────────────────────────");

        // Detect toolkit expansion (collapsed toolkit being called by the LLM)
        var props = context.Function?.AdditionalProperties;
        var isContainer = props?.TryGetValue("IsContainer", out var icVal) == true && icVal is true;
        var isCollapse = props?.TryGetValue("IsCollapse", out var colVal) == true && colVal is true;

        if (isContainer && isCollapse && _options.LogToolkitExpansion)
        {
            var toolkitName = props?.TryGetValue("ToolkitName", out var tnVal) == true && tnVal is string tn ? tn : functionName;
            sb.AppendLine($"{_options.LogPrefix}[TOOLKIT EXPAND] {toolkitName}");

            // List child functions being expanded into scope
            if (props?.TryGetValue("FunctionNames", out var fnVal) == true && fnVal is System.Collections.Generic.IEnumerable<string> fnNames)
            {
                sb.AppendLine($"  Expanding functions:");
                foreach (var fn in fnNames)
                    sb.AppendLine($"    + {fn}");
            }
        }
        else if (_options.LogFunction)
        {
            sb.Append($"{_options.LogPrefix}[PRE] {functionName}");

            if (_options.IncludeArguments)
            {
                var args = FormatArgs(context.Arguments);
                sb.Append($" | Args: {TruncateString(args)}");
            }
            sb.AppendLine();
        }
        else
        {
            return Task.CompletedTask;
        }

        LogMessage(sb.ToString());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
    {
        if (!_options.LogFunction && !_options.LogToolkitExpansion) return Task.CompletedTask;

        var functionName = context.Function?.Name ?? "<unknown>";
        var callId = context.FunctionCallId ?? "";
        var exception = context.Exception;

        // Get elapsed time
        long elapsedMs = 0;
        if (_options.IncludeTiming && _timers.TryRemove(callId, out var stopwatch))
        {
            stopwatch.Stop();
            elapsedMs = stopwatch.ElapsedMilliseconds;
        }

        var sb = new StringBuilder();

        var props = context.Function?.AdditionalProperties;
        var isContainer = props?.TryGetValue("IsContainer", out var icVal) == true && icVal is true;
        var isCollapse = props?.TryGetValue("IsCollapse", out var colVal) == true && colVal is true;

        if (isContainer && isCollapse && _options.LogToolkitExpansion)
        {
            var toolkitName = props?.TryGetValue("ToolkitName", out var tnVal) == true && tnVal is string tn ? tn : functionName;
            if (exception != null)
            {
                sb.Append($"{_options.LogPrefix}[TOOLKIT EXPAND FAILED] {toolkitName}");
                if (_options.IncludeTiming) sb.Append($" ({elapsedMs}ms)");
                sb.Append($" | Error: {exception.Message}");
            }
            else
            {
                sb.Append($"{_options.LogPrefix}[TOOLKIT COLLAPSE] {toolkitName}");
                if (_options.IncludeTiming) sb.Append($" ({elapsedMs}ms)");

                // Show which toolkit-scoped middlewares are active for this toolkit
                var containerState = context.GetMiddlewareState<ContainerMiddlewareState>();
                if (containerState != null
                    && containerState.ToolkitPipelines.TryGetValue(toolkitName, out var pipeline)
                    && !pipeline.IsEmpty)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  Active scoped middleware ({pipeline.Count}):");
                    foreach (var mw in pipeline.Middlewares)
                        sb.Append($"    • {mw.GetType().Name}");
                }
            }
        }
        else if (_options.LogFunction && exception != null)
        {
            sb.Append($"{_options.LogPrefix}[POST] {functionName} FAILED");
            if (_options.IncludeTiming) sb.Append($" ({elapsedMs}ms)");
            sb.Append($" | Error: {exception.Message}");
        }
        else if (_options.LogFunction)
        {
            sb.Append($"{_options.LogPrefix}[POST] {functionName} OK");
            if (_options.IncludeTiming) sb.Append($" ({elapsedMs}ms)");
            if (_options.IncludeResults)
            {
                var result = FormatResult(context.Result);
                sb.Append($" | Result: {TruncateString(result)}");
            }
        }
        else
        {
            return Task.CompletedTask;
        }
        sb.AppendLine();
        sb.AppendLine("─────────────────────────────────────────────────────────────────────────────────────────────────");

        LogMessage(sb.ToString());
        return Task.CompletedTask;
    }

    //     
    // HELPER METHODS
    //     

    private void LogMessage(string message)
    {
        if (_logger != null)
        {
            _logger.LogInformation("{Message}", message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    private string TruncateString(string? value)
    {
        if (value == null) return "<null>";
        if (_options.MaxStringLength <= 0) return value;
        if (value.Length <= _options.MaxStringLength) return value;

        return value.Substring(0, _options.MaxStringLength) + "...";
    }

    private static string FormatArgs(object? args)
    {
        if (args == null) return "<null>";
        if (args is System.Collections.IDictionary dict)
        {
            var items = new StringBuilder();
            foreach (var key in dict.Keys)
            {
                items.Append($"{key}: {dict[key]}, ");
            }
            return items.Length > 0 ? items.ToString().TrimEnd(',', ' ') : "<empty>";
        }
        return args.ToString() ?? "<unknown>";
    }

    private static string FormatResult(object? result)
    {
        if (result == null) return "<null>";

        // If it's a ValidationErrorResponse, serialize it to JSON for readability
        if (result is ValidationErrorResponse validationError)
        {
            try
            {
                return JsonSerializer.Serialize(validationError, HPDJsonContext.Default.ValidationErrorResponse);
            }
            catch
            {
                return result.ToString() ?? "<unknown>";
            }
        }

        // For other objects, try JSON serialization first, fall back to ToString()
        try
        {
            // Use AOT-compatible source-generated context instead of reflection-based serialization
            return JsonSerializer.Serialize(
                result,
                result.GetType(),
                HPDJsonContext.Default);
        }
        catch
        {
            return result.ToString() ?? "<unknown>";
        }
    }
}
