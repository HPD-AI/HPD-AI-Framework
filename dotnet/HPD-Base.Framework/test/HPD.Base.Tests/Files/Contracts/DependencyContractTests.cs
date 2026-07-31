namespace HPD.Base.Tests.Files.Contracts;

public sealed class DependencyContractTests
{
    [Fact]
    public void RuntimeDoesNotReferenceProviderSdksOrAspNetCore()
    {
        var referenced = typeof(FileModuleIds).Assembly.GetReferencedAssemblies().Select(static assembly => assembly.Name).ToArray();

        referenced.Should().NotContain(["AWSSDK.S3", "Azure.Storage.Blobs", "Google.Cloud.Storage.V1"]);
        referenced.Where(name => name is not null && name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)).Should().BeEmpty();
    }
}
