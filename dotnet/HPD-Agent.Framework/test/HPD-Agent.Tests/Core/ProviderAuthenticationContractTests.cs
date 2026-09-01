using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using Xunit;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderAuthenticationContractTests
{
    [Fact]
    public void PortableAuthenticationUnion_RoundTripsWithStableDiscriminator()
    {
        ProviderAuthentication authentication = new OAuthProviderAuthentication
        {
            AccountId = "work",
            Scopes = ["chat", "models.read"],
            AuthorizationProfile = "desktop",
            StoreKey = "keychain"
        };

        var json = JsonSerializer.Serialize(authentication);
        var roundTrip = JsonSerializer.Deserialize<ProviderAuthentication>(json);

        Assert.Contains("\"type\":\"oauth\"", json, StringComparison.Ordinal);
        var oauth = Assert.IsType<OAuthProviderAuthentication>(roundTrip);
        Assert.Equal("work", oauth.AccountId);
        Assert.Equal(["chat", "models.read"], oauth.Scopes);
        Assert.Equal("desktop", oauth.AuthorizationProfile);
        Assert.Equal("keychain", oauth.StoreKey);
    }

    [Fact]
    public void ExplicitRuntimeCredential_IsNotPortable()
    {
        ProviderAuthentication authentication = new ExplicitApiKeyProviderAuthentication
        {
            RuntimeRegistrationName = "runtime:one"
        };

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(authentication));
    }

    [Fact]
    public void AgentConfigSerializer_RejectsRuntimeLiteralRegistrationBeforeWriting()
    {
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Provider = new ProviderReference
                    {
                        Key = "openai",
                        Backend = "platform",
                        Authentication = new ExplicitApiKeyProviderAuthentication
                        {
                            RuntimeRegistrationName = "runtime:opaque"
                        }
                    }
                }
            }
        };

        var error = Assert.Throws<AgentRunConfigurationException>(() => HpdAgentConfigSerializer.Serialize(config));

        Assert.Equal("RuntimeOnlyProviderConfiguration", error.Code);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderReference_KeepsProviderBackendAndAuthenticationAtomic()
    {
        var selection = new ProviderReference
        {
            Key = "openai",
            Backend = "platform",
            Authentication = new ApiKeyProviderAuthentication
            {
                SecretKey = "openai:ApiKey"
            }
        };

        var json = JsonSerializer.Serialize(selection);
        var roundTrip = JsonSerializer.Deserialize<ProviderReference>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal("openai", roundTrip.Key);
        Assert.Equal("platform", roundTrip.Backend);
        Assert.Equal(
            "openai:ApiKey",
            Assert.IsType<ApiKeyProviderAuthentication>(roundTrip.Authentication).SecretKey);
    }
}
