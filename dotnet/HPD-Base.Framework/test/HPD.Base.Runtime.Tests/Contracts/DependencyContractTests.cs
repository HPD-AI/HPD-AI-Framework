namespace HPD.Base.Runtime.Tests.Contracts;

public sealed class DependencyContractTests
{
    private static readonly HashSet<string> AllowedRuntimeAssemblyReferences = new(StringComparer.Ordinal)
    {
        "System.Runtime",
        "System.Collections",
        "System.ComponentModel",
        "System.Linq",
        "System.Memory",
        "System.Text.Json",
        "System.Text.Encodings.Web",
        "System.Threading",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options",
        "HPD.Base.Abstractions",
        "HPD.Events"
    };

    [Fact]
    public void RuntimeAssemblyReferencesOnlyAllowedDependencies()
    {
        var unexpected = typeof(IHPDBaseRuntime).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !AllowedRuntimeAssemblyReferences.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("HPD.Auth")]
    [InlineData("EntityFramework")]
    [InlineData("Npgsql")]
    [InlineData("SignalR")]
    [InlineData("GraphQL")]
    [InlineData("OpenApi")]
    public void RuntimeAssemblyDoesNotReferenceDeferredOrHostedPackages(string forbiddenPrefix)
    {
        var unexpected = typeof(IHPDBaseRuntime).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unexpected);
    }
}
