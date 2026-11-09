using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware.Function;

/// <summary>
/// Middleware that formats function execution and model-call errors for LLM consumption.
/// Detailed exception messages are included by default and can be suppressed for sanitized output.
/// </summary>
/// <remarks>
/// <para><b>Security Features:</b></para>
/// <para>
/// This middleware controls the exception detail exposed to the LLM. Because
/// <see cref="ErrorHandlingConfig.IncludeDetailedErrorsInChat"/> defaults to <c>true</c>,
/// detailed messages may expose:
/// - Stack traces
/// - Database connection strings
/// - File system paths
/// - API keys or tokens
/// - Internal implementation details
/// </para>
///
/// <para><b>Configuration:</b></para>
/// <para>
/// Controlled by <c>ErrorHandlingConfig.IncludeDetailedErrorsInChat</c>:
/// - <c>true</c> (default): Returns detailed messages with exception information
/// - <c>false</c>: Returns generic function errors and sanitized model errors
/// </para>
///
/// <para><b>Observability:</b></para>
/// <para>
/// Regardless of the formatting setting, the original exception remains available through the
/// middleware error and function contexts for logging, debugging, and observability.
/// </para>
///
/// <para><b>Recommended Middleware Order:</b></para>
/// <code>
/// .WithRetry()              // Outermost - retry the entire operation
/// .WithFunctionTimeout()    // Middle - timeout individual attempts
/// .WithErrorFormatting()    // Innermost - format errors after all retries exhausted
/// </code>
///
/// <para><b>Example Usage:</b></para>
/// <code>
/// // Default - detailed exception messages
/// var config = new AgentConfig();
/// await using var secureAgent = await new AgentBuilder(config)
///     .WithErrorFormatting()
///     .BuildAsync();
///
/// // Allow detailed errors (use only in trusted environments)
/// var detailedConfig = new AgentConfig
/// {
///     ErrorHandling = new ErrorHandlingConfig
///     {
///         IncludeDetailedErrorsInChat = true
///     }
/// };
/// await using var detailedAgent = await new AgentBuilder(detailedConfig)
///     .WithErrorFormatting()
///     .BuildAsync();
/// </code>
/// </remarks>
public class ErrorFormattingMiddleware : IAgentMiddleware
{
    private readonly bool _includeDetailedErrors;
    private readonly IProviderErrorHandler _providerHandler;

    /// <summary>
    /// Creates a new error formatting middleware with sanitized errors.
    /// The <see cref="ErrorHandlingConfig"/> constructor uses that configuration's default,
    /// which enables detailed errors.
    /// </summary>
    public ErrorFormattingMiddleware()
    {
        _includeDetailedErrors = false;
        _providerHandler = new GenericErrorHandler();
    }

    /// <summary>
    /// Creates a new error formatting middleware with the specified configuration.
    /// </summary>
    /// <param name="config">Error handling configuration</param>
    /// <param name="providerErrorHandler">Provider-specific error handler for category-aware messages</param>
    public ErrorFormattingMiddleware(ErrorHandlingConfig config, IProviderErrorHandler? providerErrorHandler = null)
    {
        _includeDetailedErrors = config?.IncludeDetailedErrorsInChat ?? false;
        _providerHandler = providerErrorHandler ?? new GenericErrorHandler();
    }

    /// <summary>
    /// Gets or sets whether to include detailed exception messages in function results sent to the LLM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Security Warning:</b> Setting this to <c>true</c> may expose sensitive information to the LLM and end users:
    /// - Database connection strings
    /// - File system paths
    /// - API keys or tokens
    /// - Internal implementation details
    /// </para>
    /// <para>
    /// A new <see cref="ErrorHandlingConfig"/> enables detailed errors by default. When <c>false</c>,
    /// function errors are reported to the LLM as generic messages like
    /// "Error: Function 'X' failed." The original exception remains available to application code
    /// through middleware contexts for logging and debugging.
    /// </para>
    /// <para>
    /// When <c>true</c>, the exception message is included in the function result, allowing the LLM
    /// to potentially self-correct (e.g., retry with different arguments). Disable it when model-facing
    /// errors may expose sensitive information.
    /// </para>
    /// </remarks>
    public bool IncludeDetailedErrorsInChat
    {
        get => _includeDetailedErrors;
        init => _includeDetailedErrors = value;
    }

    /// <summary>
    /// Wraps function execution. Errors are allowed to propagate naturally
    /// and are formatted in AfterFunctionAsync hook instead.
    /// </summary>
    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        // V2: Let exceptions propagate naturally through the pipeline
        // Error formatting happens in AfterFunctionAsync where we have proper context
        // This allows OnErrorAsync to be called and error tracking to work correctly
        return await handler(request);
    }

    /// <summary>
    /// Formats error messages for LLM after function execution completes.
    /// Acts as a security boundary between raw exceptions and the LLM.
    /// </summary>
    /// <remarks>
    /// This is the proper place to format errors - after the exception has been
    /// caught by Agent and OnErrorAsync has been called for error tracking.
    /// The formatted message is sent to the LLM, while the original exception
    /// remains intact in context.Exception for error tracking middleware.
    /// </remarks>
    public Task AfterFunctionAsync(
        AfterFunctionContext context,
        CancellationToken cancellationToken)
    {
        // If function threw an exception, format the result message for LLM
        // The exception itself remains intact in context.Exception for error tracking
        if (context.Exception != null)
        {
            var functionName = context.Function?.Name ?? "Unknown";

            // Format message based on security settings
            var formattedMessage = _includeDetailedErrors
                ? $"Error invoking function '{functionName}': {context.Exception.Message}"
                : $"Error: Function '{functionName}' failed.";

            // Set the sanitized error message that will be sent to the LLM
            // context.Result is mutable, so we can transform it here
            // The original exception stays in context.Exception for error tracking
            context.Result = formattedMessage;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Wraps model call streaming and formats any errors according to security settings.
    /// Uses Channel-based approach to catch errors during streaming while maintaining progressive streaming.
    /// </summary>
    public IAsyncEnumerable<AgentModelUpdate>? WrapModelTurnStreamingAsync(
        AgentModelTurnRequest request,
        Func<AgentModelTurnRequest, IAsyncEnumerable<AgentModelUpdate>> handler,
        CancellationToken cancellationToken)
    {
        return FormatModelCallErrorsAsync(request, handler, cancellationToken);
    }

    /// <summary>
    /// Internal implementation that catches and formats model call errors during streaming.
    /// Separate method enables yield return outside of try-catch (C# compiler requirement).
    /// </summary>
    private async IAsyncEnumerable<AgentModelUpdate> FormatModelCallErrorsAsync(
        AgentModelTurnRequest request,
        Func<AgentModelTurnRequest, IAsyncEnumerable<AgentModelUpdate>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AgentModelUpdate>();
        Exception? capturedException = null;

        // Producer task: Stream with error capture
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var update in handler(request).WithCancellation(cancellationToken))
                {
                    await channel.Writer.WriteAsync(update, cancellationToken);
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                capturedException = ex;
                channel.Writer.Complete();  // Complete normally, error handled separately
            }
        }, cancellationToken);

        // Consumer: Yield updates as they arrive
        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;  // Progressive streaming
        }

        // Wait for producer to finish
        await producerTask;

        // Format and re-throw error if one occurred
        if (capturedException != null)
        {
            if (_includeDetailedErrors)
            {
                throw new InvalidOperationException(
                    $"Model API call failed: {capturedException.Message}",
                    capturedException);
            }
            else
            {
                var message = FormatSanitizedModelError(capturedException);
                throw new InvalidOperationException(message, capturedException);
            }
        }
    }

    private string FormatSanitizedModelError(Exception ex)
    {
        // Try the exception and walk the inner chain to find one the provider handler can parse
        var details = _providerHandler.ParseError(ex);
        if (details == null && ex.InnerException != null)
            details = _providerHandler.ParseError(ex.InnerException);

        return details?.Category switch
        {
            ErrorCategory.ModelNotFound => "The model was not found. Use /model to select a valid model.",
            ErrorCategory.AuthError => "Authentication failed. Check your provider credentials via /providers.",
            ErrorCategory.RateLimitTerminal => "Rate limit quota exceeded. Check your provider account.",
            ErrorCategory.RateLimitRetryable => "Rate limited by the provider. Please try again shortly.",
            ErrorCategory.ContextWindow => "The conversation is too long for this model.",
            ErrorCategory.ClientError => "The request was rejected by the provider. Check your configuration.",
            ErrorCategory.ServerError => "The provider is experiencing issues. Please try again.",
            _ => "The model call failed. Please try again."
        };
    }
}
