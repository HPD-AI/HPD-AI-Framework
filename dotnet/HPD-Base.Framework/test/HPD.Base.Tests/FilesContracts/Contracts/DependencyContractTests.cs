namespace HPD.Base.Tests.FilesContracts.Contracts;

public sealed class DependencyContractTests
{
    [Fact]
    public void AbstractionsDoNotReferenceAspNetCoreOrProviderSdks()
    {
        var referenced = typeof(FileObjectRef).Assembly.GetReferencedAssemblies().Select(static assembly => assembly.Name).ToArray();

        referenced.Where(name => name is not null && name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)).Should().BeEmpty();
        referenced.Should().NotContain(["AWSSDK.S3", "Azure.Storage.Blobs", "Google.Cloud.Storage.V1"]);
    }
}
