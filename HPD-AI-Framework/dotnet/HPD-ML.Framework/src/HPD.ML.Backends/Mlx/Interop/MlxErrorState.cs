namespace HPD.ML.Backends.Mlx.Interop;

internal static class MlxErrorState
{
    [ThreadStatic]
    private static string? _lastError;

    public static void SetLastError(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _lastError = message;
    }

    public static string ConsumeLastError(string fallback)
    {
        var message = _lastError;
        _lastError = null;
        return string.IsNullOrWhiteSpace(message) ? fallback : message;
    }
}

