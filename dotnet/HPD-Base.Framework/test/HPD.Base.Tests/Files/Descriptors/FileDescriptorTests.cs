using HPD.Base;
using System.Text.Json;

namespace HPD.Base.Tests.Files.Descriptors;

public sealed class FileDescriptorTests
{
    [Fact]
    public async Task DescriptorContributesFilesModuleAndRedactsSecrets()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseFiles(options =>
        {
            options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("private"),
                ProviderRef = new FileProviderRef("provider-a"),
                AdminConfigSummary = new FileBucketAdminConfigSummary
                {
                    ProviderRef = new FileProviderRef("provider-a"),
                    NonSecretMetadata = new Dictionary<string, string>
                    {
                        ["region"] = "test"
                    }
                }
            });
        });

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        var snapshot = provider.GetRequiredService<IBaseDescriptorRegistry>().Current;
        snapshot.Manifest.Modules.Should().Contain(module => module.Id == FileModuleIds.Module && module.Kind == BaseModuleKind.Files);
        snapshot.Manifest.Modules!.Single(module => module.Id == FileModuleIds.Module).ContributedHealthRefIds
            .Should().Contain(FileHealthIds.Bucket(new FileBucketId("private")));
        snapshot.Manifest.HealthRefs.Should().Contain(health => health.Id == FileHealthIds.Bucket(new FileBucketId("private")));
        snapshot.Capabilities.Families
            .Should().Contain(family => family.FamilyId == BaseCapabilityFamilies.Files);

        var json = JsonSerializer.Serialize(snapshot.Manifest.Modules!.Single(module => module.Id == FileModuleIds.Module));
        json.Should().Contain("private");
        json.Should().Contain("provider-a");
        json.Should().NotContain("credential");
        json.Should().NotContain("rootPath");
        snapshot.Manifest.EventTypes.Should().Contain(eventType => eventType.Type == FileEventTypeNames.ObjectUploaded);
    }
}
