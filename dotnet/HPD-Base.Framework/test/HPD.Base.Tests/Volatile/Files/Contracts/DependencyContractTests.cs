namespace HPD.Base.Tests.Volatile.Files.Contracts;

public sealed class DependencyContractTests
{
    [Fact]
    public void VolatileProviderDoesNotReferenceExternalStorageSdks()
    {
        var referenced = typeof(VolatileFileStorageProvider).Assembly.GetReferencedAssemblies().Select(static assembly => assembly.Name).ToArray();

        referenced.Should().NotContain(["AWSSDK.S3", "Azure.Storage.Blobs", "Google.Cloud.Storage.V1"]);
    }
}
