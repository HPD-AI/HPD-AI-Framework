namespace HPD.Environment.Runtime;

using HPD.Environment.Contracts;

internal sealed record ProviderIncarnation(
    ProviderId ProviderId,
    ulong ProviderGeneration,
    ResourceGeneration ResourceGeneration,
    RuntimeHostStartGeneration? HostStartGeneration = null,
    EngineIncarnationGeneration? EngineGeneration = null,
    IReadOnlyDictionary<string, string>? ProviderDimensions = null);

internal static class ProviderIncarnationValidator
{
    public static Diagnostic? ValidateExact(
        ProviderIncarnation expected,
        ProviderIncarnation observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        if (expected.ProviderId != observed.ProviderId)
            return Stale(
                "provider",
                expected.ProviderId.Value,
                observed.ProviderId.Value);
        if (expected.ProviderGeneration !=
            observed.ProviderGeneration)
            return Stale(
                "provider generation",
                expected.ProviderGeneration,
                observed.ProviderGeneration);
        if (expected.ResourceGeneration !=
            observed.ResourceGeneration)
            return Stale(
                "resource generation",
                expected.ResourceGeneration.Value,
                observed.ResourceGeneration.Value);
        if (expected.HostStartGeneration !=
            observed.HostStartGeneration)
            return Stale(
                "host-start generation",
                expected.HostStartGeneration?.Value,
                observed.HostStartGeneration?.Value);
        if (expected.EngineGeneration != observed.EngineGeneration)
            return Stale(
                "engine generation",
                expected.EngineGeneration?.Value,
                observed.EngineGeneration?.Value);

        IReadOnlyDictionary<string, string> expectedDimensions =
            expected.ProviderDimensions ??
            new Dictionary<string, string>(
                0,
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> observedDimensions =
            observed.ProviderDimensions ??
            new Dictionary<string, string>(
                0,
                StringComparer.Ordinal);
        foreach ((string name, string value) in expectedDimensions)
        {
            if (!observedDimensions.TryGetValue(
                    name,
                    out string? observedValue) ||
                !string.Equals(
                    value,
                    observedValue,
                    StringComparison.Ordinal))
            {
                return Stale(
                    $"provider dimension '{name}'",
                    value,
                    observedValue);
            }
        }
        return null;
    }

    public static Diagnostic? ValidateMonotonic(
        ProviderIncarnation previous,
        ProviderIncarnation current)
    {
        if (previous.ProviderId != current.ProviderId)
            return Stale(
                "provider",
                previous.ProviderId.Value,
                current.ProviderId.Value);
        if (current.ProviderGeneration <
            previous.ProviderGeneration)
            return Regressed(
                "provider generation",
                previous.ProviderGeneration,
                current.ProviderGeneration);
        if (current.ResourceGeneration.Value <
            previous.ResourceGeneration.Value)
            return Regressed(
                "resource generation",
                previous.ResourceGeneration.Value,
                current.ResourceGeneration.Value);
        if (previous.HostStartGeneration is { } previousHost &&
            current.HostStartGeneration is { } currentHost &&
            currentHost.Value < previousHost.Value)
            return Regressed(
                "host-start generation",
                previousHost.Value,
                currentHost.Value);
        if (previous.EngineGeneration is { } previousEngine &&
            current.EngineGeneration is { } currentEngine &&
            currentEngine.Value < previousEngine.Value)
            return Regressed(
                "engine generation",
                previousEngine.Value,
                currentEngine.Value);
        return null;
    }

    private static Diagnostic Stale(
        string dimension,
        object? expected,
        object? observed) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(
                "hpd.environment.incarnation.stale"),
            Message =
                $"The {dimension} changed from '{expected ?? "(absent)"}' to '{observed ?? "(absent)"}'.",
        };

    private static Diagnostic Regressed(
        string dimension,
        object previous,
        object current) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(
                "hpd.environment.incarnation.regressed"),
            Message =
                $"The {dimension} regressed from '{previous}' to '{current}'.",
        };
}

internal static class ProviderCleanup
{
    public static async ValueTask<IReadOnlyList<Exception>>
        RunAllAsync(
            IEnumerable<Func<CancellationToken, ValueTask>> cleanup,
            TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (deadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deadline));
        using var timeout = new CancellationTokenSource(deadline);
        var failures = new List<Exception>();
        foreach (Func<CancellationToken, ValueTask> operation in cleanup)
        {
            try
            {
                await operation(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        return failures;
    }
}
