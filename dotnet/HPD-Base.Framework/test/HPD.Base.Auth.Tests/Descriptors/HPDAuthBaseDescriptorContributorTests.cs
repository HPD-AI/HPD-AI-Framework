namespace HPD.Base.Auth.Tests.Descriptors;

public sealed class HPDBaseAuthDescriptorContributorTests
{
    [Fact]
    public async Task RuntimeManifestIncludesHPDAuthAdapterModuleAndCapabilities()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseAuthServices();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        var descriptors = provider.GetRequiredService<IBaseDescriptorProvider>();
        var result = await descriptors.GetExpandedManifestAsync(new BaseManifestExpansionRequest
        {
            Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Anonymous },
            Operation = new OperationContext
            {
                Operation = BaseOperationKind.SchemaRead,
                CollectionId = "metadata",
                Now = DateTimeOffset.UnixEpoch
            },
            View = VisibilityLevel.Public,
            Expand = ["capabilities", "health", "diagnostics"]
        });

        result.IsSuccess().Should().BeTrue();
        result.Value!.Manifest.Modules.Should().Contain(module => module.Id == HPDBaseAuthIds.Module);
        result.Value.Capabilities!.Families.Should().Contain(family => family.FamilyId == "auth.hpd-auth");
        result.Value.Health.Should().Contain(health => health.Id == HPDBaseAuthHealthIds.Registration);
    }
}
