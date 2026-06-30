using System.Reflection;

namespace HPD.Base.AspNetCore.Tests.PublicApi;

public sealed class NoReflectionEndpointMappingTests
{
    [Fact]
    public void SourceDoesNotUseControllerOrReflectionEndpointDiscovery()
    {
        typeof(HPDBaseEndpointRouteBuilderExtensions).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.Name)
            .Should()
            .NotContain(["MapControllers", "AddControllers", "MakeGenericMethod"]);
    }
}
