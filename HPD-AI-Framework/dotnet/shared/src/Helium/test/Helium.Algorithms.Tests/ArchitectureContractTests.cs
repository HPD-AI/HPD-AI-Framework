using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class ArchitectureContractTests
{
    [Fact]
    public void ExactAlgorithms_DoNotReferenceHardwareOrValidatedAssemblies()
    {
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "Helium.Hardware",
            "Helium.Validated"
        };

        var references = typeof(Determinant).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && forbidden.Contains(name))
            .ToArray();

        Assert.Empty(references);
    }
}
