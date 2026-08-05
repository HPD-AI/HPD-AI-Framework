using System.Text.Json;

namespace HPD.Base.Tests.FilesContracts.Serialization;

public sealed class FileDtoJsonTests
{
    [Fact]
    public void ObjectRefRoundTripsWithSourceGeneratedJson()
    {
        var value = new FileObjectRef
        {
            BucketId = new FileBucketId("avatars"),
            ObjectId = new FileObjectId("obj_123"),
            Revision = new FileObjectRevision("rev1"),
            ContentType = "image/png",
            SizeBytes = 42,
            Checksum = new FileObjectChecksum("sha256:abc"),
            Name = "avatar.png",
            Metadata = new Dictionary<string, string> { ["safe"] = "yes" }
        };

        var json = JsonSerializer.Serialize(value, HPDBaseFilesJsonSerializerContext.Default.FileObjectRef);
        json.Should().Contain("\"bucketId\":\"avatars\"");
        json.Should().NotContain("provider");

        var roundTrip = JsonSerializer.Deserialize(json, HPDBaseFilesJsonSerializerContext.Default.FileObjectRef);
        roundTrip.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void BucketDescriptorSerializesOnlyPortableFields()
    {
        var descriptor = new FileBucketDescriptor
        {
            BucketId = new FileBucketId("public"),
            DisplayName = "Public uploads",
            Visibility = FileBucketVisibility.PublicRead,
            ProviderRef = new FileProviderRef("demo"),
            PublicSafeMetadata = new Dictionary<string, string> { ["purpose"] = "tests" },
            AdminConfigSummary = new FileBucketAdminConfigSummary
            {
                ProviderRef = new FileProviderRef("demo"),
                NonSecretMetadata = new Dictionary<string, string> { ["storageClass"] = "standard" }
            }
        };

        var json = JsonSerializer.Serialize(descriptor, HPDBaseFilesJsonSerializerContext.Default.FileBucketDescriptor);
        json.Should().Contain("\"visibility\":\"publicRead\"");
        json.Should().NotContain("credential");
        json.Should().NotContain("signedUrl");
        json.Should().NotContain("rootPath");
    }
}
