namespace HPD.Base.Tests.Contracts;

public sealed class PublicNamespaceTests
{
    private static readonly string[] ForbiddenExtensionNamespaces =
    {
        "HPD.Base.AspNetCore",
        "HPD.Base.Sqlite",
        "HPD.Base.Auth",
        "HPD.Base.Testing",
        "HPD.Base.Studio"
    };

    [Fact]
    public void CoreAssemblyDoesNotOwnExtensionNamespaces()
    {
        var unexpected = typeof(IHPDBaseRuntime).Assembly
            .GetExportedTypes()
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .Where(ns => ForbiddenExtensionNamespaces.Any(prefix =>
                ns.Equals(prefix, StringComparison.Ordinal) ||
                ns.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }
}
