namespace HPD.Sandbox.Local.Network;

internal enum SandboxProxyProtocol
{
    Http,
    Socks5
}

internal enum SandboxProxyEventKind
{
    NetworkPolicyDenied,
    RequestFilterDenied,
    MalformedRequest,
    UpstreamFailure
}

internal sealed record SandboxProxyEvent
{
    public required SandboxProxyProtocol Protocol { get; init; }
    public required SandboxProxyEventKind Kind { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Method { get; init; }
    public Uri? Uri { get; init; }
}

internal static class SandboxProxyEventExtensions
{
    public static SandboxViolation ToSandboxViolation(this SandboxProxyEvent proxyEvent)
    {
        ArgumentNullException.ThrowIfNull(proxyEvent);

        return new SandboxViolation
        {
            Type = ViolationType.NetworkAccess,
            Message = FormatMessage(proxyEvent),
            Timestamp = proxyEvent.Timestamp,
            Path = FormatTarget(proxyEvent)
        };
    }

    private static string FormatMessage(SandboxProxyEvent proxyEvent)
    {
        var target = FormatTarget(proxyEvent);
        var targetSuffix = target is { Length: > 0 }
            ? $" for {target}"
            : string.Empty;

        return proxyEvent.Kind switch
        {
            SandboxProxyEventKind.NetworkPolicyDenied =>
                $"{proxyEvent.Protocol} proxy denied network access{targetSuffix}: {proxyEvent.Reason}",
            SandboxProxyEventKind.RequestFilterDenied =>
                $"{proxyEvent.Protocol} proxy request filter denied request{targetSuffix}: {proxyEvent.Reason}",
            SandboxProxyEventKind.MalformedRequest =>
                $"{proxyEvent.Protocol} proxy rejected malformed request{targetSuffix}: {proxyEvent.Reason}",
            SandboxProxyEventKind.UpstreamFailure =>
                $"{proxyEvent.Protocol} proxy upstream failure{targetSuffix}: {proxyEvent.Reason}",
            _ => $"{proxyEvent.Protocol} proxy event{targetSuffix}: {proxyEvent.Reason}"
        };
    }

    private static string? FormatTarget(SandboxProxyEvent proxyEvent)
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
