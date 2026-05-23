namespace HPD.Execution.Local.Network;

internal enum ProcessIsolationProxyProtocol
{
    Http,
    Socks5
}

internal enum ProcessIsolationProxyEventKind
{
    NetworkPolicyDenied,
    RequestFilterDenied,
    MalformedRequest,
    UpstreamFailure
}

internal sealed record ProcessIsolationProxyEvent
{
    public required ProcessIsolationProxyProtocol Protocol { get; init; }
    public required ProcessIsolationProxyEventKind Kind { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Method { get; init; }
    public Uri? Uri { get; init; }
}

internal static class ProcessIsolationProxyEventExtensions
{
    public static ProcessIsolationViolation ToProcessIsolationViolation(this ProcessIsolationProxyEvent proxyEvent)
    {
        ArgumentNullException.ThrowIfNull(proxyEvent);

        return new ProcessIsolationViolation
        {
            Type = ProcessIsolationViolationType.NetworkAccess,
            Message = FormatMessage(proxyEvent),
            Timestamp = proxyEvent.Timestamp,
            Path = FormatTarget(proxyEvent)
        };
    }

    private static string FormatMessage(ProcessIsolationProxyEvent proxyEvent)
    {
        var target = FormatTarget(proxyEvent);
        var targetSuffix = target is { Length: > 0 }
            ? $" for {target}"
            : string.Empty;

        return proxyEvent.Kind switch
        {
            ProcessIsolationProxyEventKind.NetworkPolicyDenied =>
                $"{proxyEvent.Protocol} proxy denied network access{targetSuffix}: {proxyEvent.Reason}",
            ProcessIsolationProxyEventKind.RequestFilterDenied =>
                $"{proxyEvent.Protocol} proxy request filter denied request{targetSuffix}: {proxyEvent.Reason}",
            ProcessIsolationProxyEventKind.MalformedRequest =>
                $"{proxyEvent.Protocol} proxy rejected malformed request{targetSuffix}: {proxyEvent.Reason}",
            ProcessIsolationProxyEventKind.UpstreamFailure =>
                $"{proxyEvent.Protocol} proxy upstream failure{targetSuffix}: {proxyEvent.Reason}",
            _ => $"{proxyEvent.Protocol} proxy event{targetSuffix}: {proxyEvent.Reason}"
        };
    }

    private static string? FormatTarget(ProcessIsolationProxyEvent proxyEvent)
    {
        if (proxyEvent.Uri is not null)
            return proxyEvent.Uri.ToString();

        if (proxyEvent.Host is null)
            return null;

        return proxyEvent.Port is > 0
            ? $"{proxyEvent.Host}:{proxyEvent.Port}"
            : proxyEvent.Host;
    }
}
