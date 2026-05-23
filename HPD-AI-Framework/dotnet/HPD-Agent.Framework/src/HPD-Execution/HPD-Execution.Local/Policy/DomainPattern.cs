using System.Net;
using System.Text.RegularExpressions;

namespace HPD.Execution.Local.Policy;

internal enum DomainPatternKind
{
    ExactHost,
    WildcardSubdomain,
    IpLiteral,
    Localhost
}

/// <summary>
/// Validated network domain pattern.
/// </summary>
internal sealed partial record DomainPattern
{
    private DomainPattern(string raw, string canonical, DomainPatternKind kind)
    {
        Raw = raw;
        Canonical = canonical;
        Kind = kind;
    }

    public string Raw { get; }
    public string Canonical { get; }
    public DomainPatternKind Kind { get; }

    public static DomainPattern Parse(string value)
    {
        if (!TryParse(value, out var pattern, out var error))
            throw new ArgumentException(error, nameof(value));

        return pattern;
    }

    public static bool TryParse(
        string? value,
        out DomainPattern pattern,
        out string error)
    {
        pattern = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Domain pattern cannot be empty.";
            return false;
        }

        var raw = value.Trim();
        if (ContainsControlCharacters(raw))
        {
            error = "Domain pattern cannot contain control characters.";
            return false;
        }

        if (raw.Contains("://", StringComparison.Ordinal) ||
            raw.Contains('/', StringComparison.Ordinal))
        {
            error = "Domain pattern must not include a protocol or path.";
            return false;
        }

        if (raw == "*")
        {
            error = "Domain pattern '*' is too broad.";
            return false;
        }

        if (raw.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            pattern = new DomainPattern(raw, "localhost", DomainPatternKind.Localhost);
            return true;
        }

        var ipCandidate = StripBrackets(raw);
        if (IPAddress.TryParse(ipCandidate, out var address))
        {
            pattern = new DomainPattern(raw, address.ToString(), DomainPatternKind.IpLiteral);
            return true;
        }

        if (raw.Contains(':', StringComparison.Ordinal))
        {
            error = "Domain pattern must not include a port.";
            return false;
        }

        var lowered = raw.TrimEnd('.').ToLowerInvariant();
        if (lowered.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = lowered[2..];
            if (!IsValidDomainName(suffix))
            {
                error = "Wildcard domain pattern must be followed by a valid domain.";
                return false;
            }

            var parts = suffix.Split('.');
            if (parts.Length < 2)
            {
                error = "Wildcard domain pattern is too broad.";
                return false;
            }

            pattern = new DomainPattern(raw, suffix, DomainPatternKind.WildcardSubdomain);
            return true;
        }

        if (lowered.Contains('*', StringComparison.Ordinal))
        {
            error = "Wildcard is only allowed as a leading '*.'.";
            return false;
        }

        if (!IsValidDomainName(lowered))
        {
            error = "Domain pattern must be a valid domain, localhost, or IP literal.";
            return false;
        }

        if (!lowered.Contains('.', StringComparison.Ordinal))
        {
            error = "Domain pattern is too broad; use a fully-qualified domain or localhost.";
            return false;
        }

        pattern = new DomainPattern(raw, lowered, DomainPatternKind.ExactHost);
        return true;
    }

    public bool Matches(CanonicalHost host)
    {
        return Kind switch
        {
            DomainPatternKind.Localhost => host.Value == "localhost",
            DomainPatternKind.IpLiteral => host.IsIpLiteral && host.Value == Canonical,
            DomainPatternKind.ExactHost => !host.IsIpLiteral && host.Value == Canonical,
            DomainPatternKind.WildcardSubdomain => !host.IsIpLiteral &&
                host.Value.EndsWith("." + Canonical, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool IsValidDomainName(string value)
    {
        if (value.Length is 0 or > 253) return false;
        if (value.StartsWith(".", StringComparison.Ordinal) ||
            value.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        if (!DomainRegex().IsMatch(value)) return false;

        var labels = value.Split('.');
        return labels.All(label =>
            label.Length is > 0 and <= 63 &&
            !label.StartsWith("-", StringComparison.Ordinal) &&
            !label.EndsWith("-", StringComparison.Ordinal));
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(char.IsControl);

    private static string StripBrackets(string host) =>
        host.Length >= 2 && host[0] == '[' && host[^1] == ']'
            ? host[1..^1]
            : host;

    [GeneratedRegex("^[a-z0-9._-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DomainRegex();
}
