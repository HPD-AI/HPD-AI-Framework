using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderFamilyPlanResolverTests
{
    private static readonly IProviderDescriptorRegistry Descriptors = ProviderComposition.Create([
        new([new Descriptor("openai", ["azure-openai"]), new Descriptor("anthropic", [])], [], [], [])
    ]).Descriptors;

    [Fact]
    public void SameProvider_OverlaysSpecifiedValuesAndCopiesMutableInputs()
    {
        var headers = new Dictionary<string, string> { ["x-tenant"] = "one" };
        var run = new ProviderClientConfig { ModelName = "gpt-run", CustomHeaders = headers };
        var plan = ProviderFamilyPlanResolver.Resolve(
            ProviderClientFamily.Chat,
            Descriptors,
            new ProviderClientConfig { ProviderKey = "openai", ModelName = "host" },
            null,
            new ProviderClientConfig { ProviderKey = "azure-openai", ModelName = "agent" },
            run);
        headers["x-tenant"] = "mutated";

        Assert.Equal("openai", plan.ProviderKey);
        Assert.Equal("gpt-run", plan.ModelName);
        Assert.Equal("one", plan.CustomHeaders!["x-tenant"]);
        Assert.Equal(ProviderConfigurationSource.RunOverride, plan.Provenance[nameof(ProviderClientConfig.ModelName)]);
    }

    [Fact]
    public void ProviderSwitch_DiscardsOldProviderBoundStateAndUsesMatchingProfile()
    {
        var plan = ProviderFamilyPlanResolver.Resolve(
            ProviderClientFamily.Chat,
            Descriptors,
            new ProviderClientConfig { ProviderKey = "openai", ModelName = "host", AuthenticationKey = "openai-key" },
            new ProviderClientConfig
            {
                ProviderKey = "anthropic",
                ModelName = "claude-profile",
                AuthenticationKey = "anthropic-key",
                ProviderConfig = new PlanProviderConfig { Region = "us" }
            },
            new ProviderClientConfig { ProviderKey = "openai", ModelName = "agent", Endpoint = "https://openai.test" },
            new ProviderClientConfig { ProviderKey = "anthropic" });

        Assert.Equal("anthropic", plan.ProviderKey);
        Assert.Equal("claude-profile", plan.ModelName);
        Assert.Equal("anthropic-key", plan.AuthenticationKey);
        Assert.Null(plan.Endpoint);
        Assert.Equal("us", Assert.IsType<PlanProviderConfig>(plan.ProviderConfig).Region);
    }

    [Fact]
    public void MissingActiveModel_FailsWithStablePathBeforeAuthentication()
    {
        var exception = Assert.Throws<AgentRunConfigurationException>(() => ProviderFamilyPlanResolver.Resolve(
            ProviderClientFamily.Chat,
            Descriptors,
            null,
            null,
            null,
            new ProviderClientConfig { ProviderKey = "openai" }));

        Assert.Equal("ModelNameRequired", exception.Code);
        Assert.Equal("Clients.Chat.ModelName", exception.Path);
    }

    private sealed class Descriptor(string key, IReadOnlyList<string> aliases) : IProviderDescriptor
    {
        public string ProviderKey => key;
        public string DisplayName => key;
        public Uri? DocumentationUri => null;
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat }
            };
        public IReadOnlyList<string> Aliases => aliases;
    }

    private sealed class PlanProviderConfig : IProviderConfig
    {
        public string? Region { get; init; }
    }
}
