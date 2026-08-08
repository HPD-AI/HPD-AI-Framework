using System.Reflection;
using HPD.Base.AspNetCore;

namespace HPD.Base.AspNetCore.Tests.PublicApi;

public sealed class PublicApiShapeTests
{
    [Fact]
    public void ExpectedPublicTypesAreExposed()
    {
        var publicTypes = typeof(HPDBaseEndpointRouteBuilderExtensions).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .ToArray();

        publicTypes.Should().Contain([
            typeof(HPDBaseAspNetCoreOptions).FullName!,
            typeof(HPDBasePublicEndpointOptions).FullName!,
            typeof(HPDBaseApplicationEndpointOptions).FullName!,
            typeof(HPDBaseEndpointDescriptor).FullName!,
            typeof(BaseHttpHeaders).FullName!,
            typeof(IBaseHttpPrincipalContextFactory).FullName!,
            typeof(IBaseHttpPrincipalMapper).FullName!,
            typeof(IBaseHttpOperationContextFactory).FullName!,
            typeof(IBaseHttpQueryBinder).FullName!,
            typeof(IBaseHttpResultMapper).FullName!,
            typeof(HPDBaseHttpResultMappingContext).FullName!,
            typeof(HPDBaseAspNetCoreJsonSerializerContext).FullName!,
            typeof(HPDBaseOpenApiOptions).FullName!,
            typeof(HPDBaseOpenApiEndpointOptions).FullName!,
            typeof(HPDBaseOpenApiDocumentNames).FullName!
        ]);
    }

    [Fact]
    public void InternalImplementationTypesDoNotLeak()
    {
        var exportedNames = typeof(HPDBaseEndpointRouteBuilderExtensions).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToArray();

        exportedNames.Should().NotContain([
            "BaseHttpQueryBinder",
            "BaseHttpResultMapper",
            "AspNetCoreRouteDescriptorFactory",
            "RecordEndpoints",
            "MetadataEndpoints"
        ]);
    }

    [Fact]
    public void ExtensionMethodsHaveExpectedSignatures()
    {
        typeof(HPDBaseEndpointRouteBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "MapHPDBasePublicApi" or "MapHPDBaseApplicationApi")
            .Should()
            .HaveCount(2);

        typeof(HPD.Base.AspNetCore.HPDBaseAspNetCoreServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "AddHPDBaseAspNetCore")
            .ReturnType
            .FullName
            .Should()
            .Be("Microsoft.Extensions.DependencyInjection.IServiceCollection");
    }
}
