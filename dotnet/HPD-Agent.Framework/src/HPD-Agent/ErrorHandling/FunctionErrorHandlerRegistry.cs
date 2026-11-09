using Microsoft.Extensions.AI;

namespace HPD.Agent.ErrorHandling;

/// <summary>Routes function failures to optional package-owned error handlers.</summary>
internal static class FunctionErrorHandlerRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IProviderErrorHandler> Handlers =
        new(StringComparer.Ordinal);

    internal static void Register(string sourceType, IProviderErrorHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentNullException.ThrowIfNull(handler);

        lock (Gate)
        {
            if (!Handlers.TryAdd(sourceType, handler))
                throw new InvalidOperationException(
                    $"A function error handler is already registered for source type '{sourceType}'.");
        }
    }

    internal static IProviderErrorHandler? Resolve(AIFunction? function)
    {
        if (function?.AdditionalProperties?.TryGetValue(
                "SourceType",
                out var sourceType) != true ||
            sourceType is not string key)
            return null;

        lock (Gate)
            return Handlers.TryGetValue(key, out var handler) ? handler : null;
    }
}
