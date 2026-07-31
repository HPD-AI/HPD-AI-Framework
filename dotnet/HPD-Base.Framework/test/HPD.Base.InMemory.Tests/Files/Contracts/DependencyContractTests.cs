namespace HPD.Base.InMemory.Tests.Files.Contracts;

public sealed class DependencyContractTests
{
    [Fact]
    public void InMemoryProviderDoesNotReferenceExternalStorageSdks()
    {
        var referenced = typeof(InMemoryFileStorageProvider).Assembly.GetReferencedAssemblies().Select(static assembly => assembly.Name).ToArray();

        referenced.Should().NotContain(["AWSSDK.S3", "Azure.Storage.Blobs", "Google.Cloud.Storage.V1"]);
    }
}
