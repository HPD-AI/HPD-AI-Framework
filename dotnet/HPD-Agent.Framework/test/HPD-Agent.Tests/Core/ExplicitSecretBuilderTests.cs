using HPD.Agent.Secrets;

namespace HPD.Agent.Tests.Core;

public sealed class ExplicitSecretBuilderTests
{
    [Fact]
    public async Task AddExplicitSecret_HasPriorityOverCustomResolver()
    {
        var builder = new AgentBuilder()
            .WithSecretResolver(new ExplicitSecretResolver(new Dictionary<string, string>
            {
                ["openai:ApiKey"] = "lower-priority"
            }))
            .AddExplicitSecret("openai:ApiKey", "explicit");

        var agent = await builder.BuildAsync();
        var resolved = await builder.SecretResolver!.ResolveAsync("openai:ApiKey");

        Assert.Equal("explicit", resolved?.Value);
        agent.Dispose();
    }

    [Fact]
    public async Task AddExplicitSecret_AfterBuild_IsRejected()
    {
        var builder = new AgentBuilder();
        var agent = await builder.BuildAsync();

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddExplicitSecret("openai:ApiKey", "late"));
        agent.Dispose();
    }
}
