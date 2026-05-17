namespace HPD.Sandbox.Local.Network;

internal sealed record ParentProxyResolution(
    Uri? ProxyUri,
    bool IsBypassed,
    string? RedactedProxyUri,
    string Reason)
{
    public static ParentProxyResolution Direct(string reason) =>
        new(null, IsBypassed: true, RedactedProxyUri: null, reason);

    public static ParentProxyResolution ViaProxy(Uri proxyUri) =>
        new(proxyUri, IsBypassed: false, RedactedProxyUri: Redact(proxyUri), "proxy");

    private static string Redact(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.IsNullOrEmpty(uri.UserInfo) ? string.Empty : "REDACTED",
            Password = string.Empty,
        };

        return builder.Uri.ToString();
    }
}
