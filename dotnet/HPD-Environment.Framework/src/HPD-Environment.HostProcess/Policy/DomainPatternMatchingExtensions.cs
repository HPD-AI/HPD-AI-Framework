namespace HPD.Environment.HostProcess.Policy;

using HPD.Environment.Contracts;

internal static class DomainPatternMatchingExtensions
{
    public static bool Matches(this DomainPattern pattern, CanonicalHost host)
    {
        return pattern.Kind switch
        {
            DomainPatternKind.Localhost => host.Value == "localhost",
            DomainPatternKind.IpLiteral => host.IsIpLiteral && host.Value == pattern.Canonical,
            DomainPatternKind.ExactHost => !host.IsIpLiteral && host.Value == pattern.Canonical,
            DomainPatternKind.WildcardSubdomain => !host.IsIpLiteral &&
                host.Value.EndsWith("." + pattern.Canonical, StringComparison.Ordinal),
            _ => false,
        };
    }
}
