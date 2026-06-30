namespace HPD.Base.Files.AspNetCore.Tests.Contracts;

public sealed class DependencyContractTests
{
    [Fact]
    public void AspNetCoreProjectionDoesNotReferenceProviderSdks()
    {
        var referenced = typeof(HPDBaseFilesEndpointRouteBuilderExtensions).Assembly.GetReferencedAssemblies().Select(static assembly => assembly.Name).ToArray();

        referenced.Should().NotContain(["AWSSDK.S3", "Azure.Storage.Blobs", "Google.Cloud.Storage.V1"]);
    }
}
