using System.Net;
using System.Net.Sockets;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Policy;

namespace HPD.Sandbox.Local.Network;

internal static class ParentProxyResolver
{
    public static ParentProxyResolution Resolve(
        Uri destination,
        ParentProxyConfig? explicitConfig = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (IsLoopbackDestination(destination))
            return ParentProxyResolution.Direct("loopback");

        var noProxy = explicitConfig?.NoProxy ?? GetEnvironment(environment, "NO_PROXY");
        if (ShouldBypass(destination, noProxy))
            return ParentProxyResolution.Direct("no_proxy");

        var proxyValue = GetProxyValue(destination, explicitConfig, environment);
        if (string.IsNullOrWhiteSpace(proxyValue))
            return ParentProxyResolution.Direct("no parent proxy configured");

        return ParentProxyResolution.ViaProxy(ParseProxyUri(proxyValue));
    }

    internal static bool ShouldBypass(Uri destination, string? noProxy)
    {
        if (string.IsNullOrWhiteSpace(noProxy))
            return false;

        if (!HostCanonicalizer.TryCanonicalize(destination.Host, out var destinationHost, out _))
            return false;

        foreach (var rawEntry in noProxy.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawEntry == "*")
                return true;

            var entry = StripOptionalPort(rawEntry);
            if (TryMatchCidr(entry, destinationHost.Value))
                return true;

            if (!HostCanonicalizer.TryCanonicalize(entry.TrimStart('.'), out var entryHost, out _))
                continue;

            if (rawEntry.StartsWith(".", StringComparison.Ordinal))
            {
                if (destinationHost.Value == entryHost.Value ||
                    destinationHost.Value.EndsWith($".{entryHost.Value}", StringComparison.Ordinal))
                    return true;
                continue;
            }

            if (destinationHost.Value == entryHost.Value)
                return true;

            if (!entryHost.IsIpLiteral &&
                destinationHost.Value.EndsWith($".{entryHost.Value}", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static Uri ParseProxyUri(string value)
    {
        var candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : $"http://{value}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var proxyUri) ||
            (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(proxyUri.Host) ||
            proxyUri.Port <= 0)
        {
            throw new ArgumentException("Parent proxy must be an http/https URI or schemeless host:port value.");
        }

        return proxyUri;
    }

    private static string? GetProxyValue(
        Uri destination,
        ParentProxyConfig? explicitConfig,
        IReadOnlyDictionary<string, string?>? environment)
    {
        if (destination.Scheme == Uri.UriSchemeHttps)
        {
            return explicitConfig?.HttpsProxy ??
                explicitConfig?.HttpProxy ??
                GetEnvironment(environment, "HTTPS_PROXY") ??
                GetEnvironment(environment, "HTTP_PROXY");
        }

        return explicitConfig?.HttpProxy ??
            GetEnvironment(environment, "HTTP_PROXY");
    }

    private static string? GetEnvironment(IReadOnlyDictionary<string, string?>? environment, string name)
    {
        if (environment is not null)
        {
            if (environment.TryGetValue(name, out var exact) && !string.IsNullOrWhiteSpace(exact))
                return exact;

            var match = environment.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
                return match.Value;

            return null;
        }

        return Environment.GetEnvironmentVariable(name) ??
            Environment.GetEnvironmentVariable(name.ToLowerInvariant());
    }

    private static bool IsLoopbackDestination(Uri destination)
    {
        if (!HostCanonicalizer.TryCanonicalize(destination.Host, out var host, out _))
            return false;

        return host.Value == "localhost" ||
            host.Value == "127.0.0.1" ||
            host.Value == "::1";
    }

    private static string StripOptionalPort(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            var endBracket = trimmed.IndexOf(']');
            return endBracket > 0 ? trimmed[1..endBracket] : trimmed;
        }

        var colon = trimmed.LastIndexOf(':');
        if (colon > 0 && trimmed.IndexOf(':') == colon && int.TryParse(trimmed[(colon + 1)..], out _))
            return trimmed[..colon];

        return trimmed;
    }

    private static bool TryMatchCidr(string entry, string destinationHost)
    {
        var slash = entry.IndexOf('/');
        if (slash <= 0)
            return false;

        if (!IPAddress.TryParse(entry[..slash], out var network) ||
            !IPAddress.TryParse(destinationHost, out var address) ||
            !int.TryParse(entry[(slash + 1)..], out var prefixLength) ||
            network.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();
        var maxBits = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxBits)
            return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (networkBytes[i] != addressBytes[i])
                return false;
        }

        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xff << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
    }
}
