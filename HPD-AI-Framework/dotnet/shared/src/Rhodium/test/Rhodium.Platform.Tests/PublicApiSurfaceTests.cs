using System.Reflection;
using Rhodium.Kernel;
using Rhodium.Platform.Patterns;

namespace Rhodium.Platform.Tests;

public sealed class PublicApiSurfaceTests
{
    [Fact]
    public void PlatformAssembly_DoesNotExposeTransitionEraStrategyAuthoringTypes()
    {
        var assembly = typeof(Strategy).Assembly;
        var exportedNames = assembly
            .GetExportedTypes()
            .Select(static type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(exportedNames, static name => name == "Rhodium.Platform.StrategyBase");
        Assert.DoesNotContain(exportedNames, static name => name.EndsWith(".ITickVisitor", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedNames, static name => name == typeof(EngineLoops).FullName);
    }

    [Fact]
    public void Strategy_DoesNotExposeRawKernelOrManualRegistrationAuthoringMembers()
    {
        var publicInstanceMethods = typeof(Strategy)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .ToArray();

        Assert.DoesNotContain("__GeneratedRegisterIndicator", publicInstanceMethods);
        Assert.DoesNotContain("__GeneratedRegisterPortfolioField", publicInstanceMethods);

        var publicTickMethods = typeof(Strategy)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method => method.Name == "OnTick")
            .ToArray();

        Assert.DoesNotContain(publicTickMethods, static method =>
        {
            var parameters = method.GetParameters();
            return parameters.Length >= 2 &&
                   parameters[0].ParameterType == typeof(MarketKernel) &&
                   parameters[1].ParameterType == typeof(PortfolioContext).MakeByRefType();
        });
    }
}
