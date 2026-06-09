using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HPD.Agent.Sandbox.Policy;

internal readonly record struct CanonicalHost(
    string Value,
    bool IsIpLiteral,
    AddressFamily? AddressFamily);

/// <summary>
/// Canonicalizes proxy request hosts before allowlist matching.
/// </summary>
internal static partial class HostCanonicalizer
{
    public static bool TryCanonicalize(
        string? host,
        out CanonicalHost canonical,
        out string error)
    {
        canonical = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Host cannot be empty.";
            return false;
        }

        var bare = StripBrackets(host.Trim()).TrimEnd('.');
        if (bare.Length is 0 or > 255)
        {
            error = "Host length is invalid.";
            return false;
        }

        if (bare.Any(char.IsControl) || bare.Contains('%', StringComparison.Ordinal))
        {
            error = "Host contains invalid characters.";
            return false;
        }

        var lowered = bare.ToLowerInvariant();
        if (LooksLikeAmbiguousNumericAddress(lowered))
        {
            error = "Ambiguous numeric host forms are not allowed.";
            return false;
        }

        if (IPAddress.TryParse(bare, out var address))
        {
            canonical = new CanonicalHost(
                address.ToString(),
                IsIpLiteral: true,
                address.AddressFamily);
            return true;
        }

        if (!DnsHostRegex().IsMatch(lowered))
        {
            error = "Host contains invalid characters.";
            return false;
        }

        canonical = new CanonicalHost(lowered, IsIpLiteral: false, AddressFamily: null);
        return true;
    }

    private static string StripBrackets(string host) =>
        host.Length >= 2 && host[0] == '[' && host[^1] == ']'
            ? host[1..^1]
            : host;

    private static bool LooksLikeAmbiguousNumericAddress(string host)
    {
        if (IsConventionalIPv4(host))
            return false;

        if (host.Contains(':', StringComparison.Ordinal))
            return false;

        if (DecimalOnlyRegex().IsMatch(host))
            return true;

        var parts = host.Split('.');
        if (parts.Length is <= 1 or > 4)
            return false;

        return parts.All(p =>
            DecimalOnlyRegex().IsMatch(p) ||
            HexNumberRegex().IsMatch(p) ||
            OctalNumberRegex().IsMatch(p));
    }

    private static bool IsConventionalIPv4(string host)
    {
        var parts = host.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (var part in parts)
        {
            if (!DecimalOnlyRegex().IsMatch(part))
                return false;

            if (part.Length > 1 && part.StartsWith('0'))
                return false;

            if (!byte.TryParse(part, out _))
                return false;
        }

        return true;
    }

    [GeneratedRegex("^[a-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DnsHostRegex();

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalOnlyRegex();

    [GeneratedRegex("^0x[0-9a-f]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HexNumberRegex();

    [GeneratedRegex("^0[0-7]+$", RegexOptions.CultureInvariant)]
    private static partial Regex OctalNumberRegex();
}
