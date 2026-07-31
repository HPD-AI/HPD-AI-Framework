namespace HPD.Base.Tests.Contracts;

public sealed class DeferredFeatureAbsenceTests
{
    [Theory]
    [InlineData("HPD.Base.AspNetCore")]
    [InlineData("HPD.Base.Files")]
    [InlineData("HPD.Base.Realtime")]
    [InlineData("HPD.Base.Search")]
    [InlineData("HPD.Base.Vector")]
    public void DeferredRuntimeNamespacesAreAbsent(string namespacePrefix)
    {
        var unexpected = typeof(IHPDBaseRuntime).Assembly
            .GetExportedTypes()
            .Where(type => (type.Namespace ?? string.Empty).StartsWith(namespacePrefix, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unexpected);
    }
}
