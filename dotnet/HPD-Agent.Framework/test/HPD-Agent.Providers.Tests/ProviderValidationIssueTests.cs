using System.Collections.Immutable;
using HPD.Agent.Providers.Anthropic;

namespace HPD.Agent.Providers.Tests;

public sealed class ProviderValidationIssueTests
{
    [Fact]
    public void ProviderValidation_ReturnsStructuredIssue()
    {
        var validation = new AnthropicProvider().ValidateConfiguration(new EffectiveProviderClientConfig
        {
            Provider = new ResolvedProviderSelection
            {
                Backend = new ProviderBackendIdentity("anthropic", "platform"),
                Authentication = new EffectiveProviderAuthentication
                {
                    Configuration = new ApiKeyProviderAuthentication { SecretKey = "anthropic:ApiKey" },
                    Kind = ProviderAuthenticationKind.ApiKey,
                    StableReferenceIdentity = "anthropic:ApiKey",
                    Scopes = []
                }
            },
            Family = ProviderClientFamily.Chat,
            CustomHeaders = ImmutableDictionary<string, string>.Empty,
            ProviderConfiguration = EmptyPayload,
            FamilyOperation = EmptyPayload,
            FamilyDefaults = new ProviderFamilyDefaultsSnapshot
            {
                OutputModalities = [],
                StopSequences = []
            },
            Provenance = new ProviderConfigurationProvenance
            {
                Fields = ImmutableDictionary<string, ProviderConfigurationLayer>.Empty
            },
            ProviderManifestRevision = "test",
            ConstructionFingerprint = "test"
        });

        Assert.False(validation.IsValid);
        var issue = Assert.Single(validation.Errors);
        Assert.Equal("provider.model_required", issue.Code);
        Assert.Equal("modelName", issue.Path);
    }

    private static ProviderPayloadSnapshot EmptyPayload => new()
    {
        ContractId = "test",
        CanonicalPayload = [],
        Fingerprint = "test"
    };
}
