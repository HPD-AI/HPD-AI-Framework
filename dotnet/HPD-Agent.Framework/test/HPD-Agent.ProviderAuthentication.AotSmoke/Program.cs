using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;

ProviderAuthentication[] portable =
[
    new ApiKeyProviderAuthentication { SecretKey = "openai:ApiKey" },
    new OAuthProviderAuthentication
    {
        AccountId = "work",
        StoreKey = "protected",
        Scopes = ["chat", "models.read"]
    },
    new ExternalIdentityProviderAuthentication { CredentialName = "azure-workload" },
    new AnonymousProviderAuthentication()
];

foreach (var authentication in portable)
{
    var selection = new ProviderReference
    {
        Key = "provider",
        Backend = "platform",
        Authentication = authentication
    };
    var json = JsonSerializer.Serialize(selection, HPDJsonContext.Default.ProviderReference);
    var roundTrip = JsonSerializer.Deserialize(json, HPDJsonContext.Default.ProviderReference);
    if (roundTrip?.Key != "provider" ||
        roundTrip.Backend != "platform" ||
        roundTrip.Authentication?.GetType() != authentication.GetType())
        return 1;
}

var profileConfig = new AgentConfig
{
    ProviderProfiles =
    {
        new AgentProviderBackendProfile
        {
            ProviderKey = "provider",
            BackendKey = "platform",
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Provider = new ProviderReference
                    {
                        Key = "provider",
                        Backend = "platform",
                        Authentication = portable[0]
                    }
                }
            }
        }
    }
};
var profileJson = JsonSerializer.Serialize(profileConfig, HPDJsonContext.Default.AgentConfig);
var profileRoundTrip = JsonSerializer.Deserialize(profileJson, HPDJsonContext.Default.AgentConfig);
if (profileRoundTrip?.ProviderProfiles.Count != 1 ||
    profileRoundTrip.ProviderProfiles[0].ProviderKey != "provider")
    return 2;

try
{
    _ = JsonSerializer.Serialize<ProviderAuthentication>(
        new ExplicitApiKeyProviderAuthentication { RuntimeRegistrationName = "runtime:one" },
        HPDJsonContext.Default.ProviderAuthentication);
    return 3;
}
catch (NotSupportedException)
{
    return 0;
}
