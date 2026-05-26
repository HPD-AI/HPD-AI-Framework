using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class ArchitectureContractTests
{
    [Fact]
    public void Finance_DoesNotReferenceRhodiumAssemblies()
    {
        var references = typeof(Black76).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("Rhodium.", StringComparison.Ordinal))
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(references);
    }

    [Fact]
    public void Finance_PublicSurfaceStaysInsideFinanceNamespace()
    {
        var offenders = typeof(Black76).Assembly.GetExportedTypes()
            .Where(type => type.Namespace is null || !type.Namespace.StartsWith("Helium.Finance", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Finance_PublicSurfaceDoesNotExposeTradingRuntimeVocabulary()
    {
        string[] forbiddenTerms =
        [
            "Account",
            "Backtest",
            "Broker",
            "Connector",
            "Fill",
            "InstrumentContract",
            "Order",
            "Position",
            "Rhodium",
            "Strategy",
            "Venue"
        ];

        var offenders = typeof(Black76).Assembly.GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .Where(name => forbiddenTerms.Any(term => name.Contains(term, StringComparison.Ordinal)))
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }
}
