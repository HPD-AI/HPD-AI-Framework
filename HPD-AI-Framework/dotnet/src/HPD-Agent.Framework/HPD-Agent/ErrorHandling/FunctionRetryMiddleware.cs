using HPD.Agent.ErrorHandling;
using HPD.Agent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware.Function;

/// <summary>
/// Middleware that provides provider-aware retry logic for both function execution and model calls.
/// Automatically retries transient failures using intelligent backoff strategies.
/// </summary>
/// <remarks>
/// <para><b>Retry Logic:</b></para>
/// <para>Uses a 3-tier priority system:</para>
/// <list type="number">
/// <item><b>Priority 1: Custom Strategy</b> - User-provided retry logic (if configured)</item>
/// <item><b>Priority 2: Provider-Aware</b> - Parse error with IProviderErrorHandler, respect Retry-After headers</item>
/// <item><b>Priority 3: Exponential Backoff</b> - Fallback strategy with jitter</item>
/// </list>
///
/// <para><b>Features:</b></para>
/// <list type="bullet">
/// <item>Retries both function calls AND model calls (streaming)</item>
/// <item>Respects provider Retry-After headers (e.g., Anthropic 429 responses)</item>
/// <item>Per-error-category retry limits (e.g., more retries for rate limits, fewer for server errors)</item>
/// <item>Smart error classification (transient, rate limit, client error, etc.)</item>
/// <item>Exponential backoff with jitter to avoid thundering herd</item>
/// <item>Emits retry events for observability</item>
/// </list>
///
/// <para><b>Model Call Retry Behavior:</b></para>
/// <para>
/// Model call streaming is buffered on the first attempt to enable retry capability.
/// If an error occurs during streaming, the entire model call is retried from the beginning.
/// This ensures consistent state but may result in duplicate API costs on retry.
/// </para>
/// </remarks>
public class RetryMiddleware : IAgentMiddleware
{
    private readonly ErrorHandlingConfig _config;
    private readonly IProviderErrorHandler _providerHandler;

    /// <summary>
    /// Creates a new function RetryMiddleware  with the specified configuration and error handler.
    /// </summary>
    /// <param name="config">Error handling configuration</param>
    /// <param name="providerErrorHandler">Provider-specific error handler for intelligent retry logic</param>
    public RetryMiddleware(ErrorHandlingConfig config, IProviderErrorHandler? providerErrorHandler = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _providerHandler = providerErrorHandler ?? new GenericErrorHandler();
    }

    /// <summary>
    /// Wraps model call streaming with provider-aware retry logic.
    /// Buffers the stream to enable retries on failure.
    /// </summary>
    public IAsyncEnumerable<ChatResponseUpdate>? WrapModelCallStreamingAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        return RetryModelCallAsync(request, handler, cancellationToken);
    }

    /// <summary>
    /// Wraps function execution with provider-aware retry logic.
    /// </summary>
    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        var maxRetries = _config.MaxRetries;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Call next handler in chain
                return await handler(request);
            }
            catch (Exception ex)
            {
                lastException = ex;

                // Check if we should retry
                var delay = await CalculateRetryDelayAsync(ex, attempt, cancellationToken);

                if (!delay.HasValue || attempt >= maxRetries)
                {
                    // Non-retryable or exhausted retries
                    throw;
                }

                // Emit retry event for observability
                request.EventCoordinator?.Emit(new FunctionRetryEvent(
                    FunctionName: request.Function?.Name ?? "Unknown",
                    Attempt: attempt + 1,
                    MaxRetries: maxRetries,
                    Delay: delay.Value,
                    ExceptionType: ex.GetType().Name,
                    ErrorMessage: ex.Message) { Exception = ex });

                // Wait before retry
                await Task.Delay(delay.Value, cancellationToken);
            }
        }

        throw lastException!;
    }

    /// <summary>
    /// Implements progressive streaming with retry capability using Channel-based approach.
    /// Works around C# CS1626 limitation (cannot yield in try-catch) by pumping tokens through a channel.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture:</b></para>
    /// <para>
    /// Uses System.Threading.Channels to decouple streaming from error handling:
    /// 1. Producer task: Runs handler with retry logic, writes updates to channel
    /// 2. Consumer (this method): Reads from channel and yields updates immediately
    /// </para>
    /// <para>
    /// This enables progressive streaming (no buffering) while maintaining retry capability.
    /// Similar to how Gemini CLI handles streaming retry in JavaScript.
    /// </para>
    /// </remarks>
    private async IAsyncEnumerable<ChatResponseUpdate> RetryModelCallAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxRetries = _config.MaxRetries;
        var channel = Channel.CreateUnbounded<ChatResponseUpdate>();
        Exception? producerException = null;

        // Producer task: Retry logic with streaming
        var producerTask = Task.Run(async () =>
        {
            Exception? lastException = null;
            TimeSpan? delay = null;

            try
            {
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // Emit retry event BEFORE retry attempt (like Gemini CLI)
                        if (attempt > 0)
                        {
                            request.EventCoordinator?.Emit(new ModelCallRetryEvent(
                                Attempt: attempt,
                                MaxRetries: maxRetries,
                                Delay: delay!.Value,
                                ExceptionType: lastException!.GetType().Name,
                                ErrorMessage: lastException!.Message) { Exception = lastException });
                        }

                        // Stream updates to channel - progressive streaming 
                        await foreach (var update in handler(request).WithCancellation(cancellationToken))
                        {
                            await channel.Writer.WriteAsync(update, cancellationToken);
                        }

                        // Success - complete channel and exit
                        channel.Writer.Complete();
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;

                        // Calculate retry delay
                        delay = await CalculateRetryDelayAsync(ex, attempt, cancellationToken);

                        if (!delay.HasValue || attempt >= maxRetries)
                        {
                            // Non-retryable or exhausted retries
                            throw;
                        }

                        // Wait before retry
                        await Task.Delay(delay.Value, cancellationToken);

                        // Loop continues - retry on next iteration
                    }
                }
            }
            catch (Exception ex)
            {
                producerException = ex;
                channel.Writer.Complete(ex);
            }
        }, cancellationToken);

        // Consumer: Yield updates as they arrive in channel
        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;  //  Progressive streaming, no try-catch!
        }

        // Wait for producer to finish
        await producerTask;

        // If producer failed, propagate exception
        if (producerException != null)
        {
            throw producerException;
        }
    }

    /// <summary>
    /// Calculates retry delay using 3-tier priority system.
    /// </summary>
    private async Task<TimeSpan?> CalculateRetryDelayAsync(
        Exception ex,
        int attempt,
        CancellationToken cancellationToken)
    {
        //     
        // PRIORITY 1: Custom Retry Strategy (user-provided)
        //     
        if (_config.CustomRetryStrategy != null)
        {
            var customDelay = await _config.CustomRetryStrategy(ex, attempt, cancellationToken);
            // Custom strategy result is authoritative - don't fall through
            return customDelay;
        }

        //     
        // PRIORITY 2: Provider-Aware Handling
        //     
        var errorDetails = _providerHandler.ParseError(ex);
        if (errorDetails != null)
        {
            // Check per-category retry limits
            if (_config.MaxRetriesByCategory != null &&
                _config.MaxRetriesByCategory.TryGetValue(errorDetails.Category, out var categoryMax))
            {
                if (attempt >= categoryMax)
                    return null; // Exceeded category-specific limit
            }

            // Get provider-calculated delay (respects Retry-After headers)
            var providerDelay = _providerHandler.GetRetryDelay(
                errorDetails,
                attempt,
                _config.RetryDelay,
                _config.BackoffMultiplier,
                _config.MaxRetryDelay);

            // Provider result is authoritative - don't fall through
            return providerDelay;
        }

        //     
        // PRIORITY 3: Exponential Backoff (fallback - only if no provider)
        //     
        return CalculateExponentialBackoff(attempt);
    }

    /// <summary>
    /// Calculates exponential backoff with jitter.
    /// </summary>
    private TimeSpan CalculateExponentialBackoff(int attempt)
    {
        // Use defaults if config values are not set
        var baseDelay = _config.RetryDelay;
        var backoffMultiplier = _config.BackoffMultiplier;
        var maxDelay = _config.MaxRetryDelay;

        var baseMs = baseDelay.TotalMilliseconds;
        var expDelayMs = baseMs * Math.Pow(backoffMultiplier, attempt);
        var cappedDelayMs = Math.Min(expDelayMs, maxDelay.TotalMilliseconds);

        // Add jitter (0.9x to 1.1x) to avoid thundering herd
        var jitter = 0.9 + (Random.Shared.NextDouble() * 0.2);

        return TimeSpan.FromMilliseconds(cappedDelayMs * jitter);
    }
}
