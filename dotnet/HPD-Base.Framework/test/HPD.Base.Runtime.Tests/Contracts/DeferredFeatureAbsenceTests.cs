namespace HPD.Base.Runtime.Tests.Contracts;

public sealed class DeferredFeatureAbsenceTests
{
    [Theory]
    [InlineData("HPD.Base.Runtime.AspNetCore")]
    [InlineData("HPD.Base.Runtime.Files")]
    [InlineData("HPD.Base.Runtime.Realtime")]
    [InlineData("HPD.Base.Runtime.Search")]
    [InlineData("HPD.Base.Runtime.Vector")]
    public void DeferredRuntimeNamespacesAreAbsent(string namespacePrefix)
    {
        var unexpected = typeof(IHPDBaseRuntime).Assembly
            .GetExportedTypes()
            .Where(type => (type.Namespace ?? string.Empty).StartsWith(namespacePrefix, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unexpected);
    }
}
