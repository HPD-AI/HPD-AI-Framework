using HPD.Agent.ToolHarness.WebSearch;

namespace HPD.Agent.Tests.Tools;

public class WebSearchHarnessRegistrationTests
{
    [Fact]
    public void WithTavilyWebSearch_WithExplicitApiKey_RegistersConfiguredHarness()
    {
        var exception = Record.Exception(() => new AgentBuilder()
            .WithTavilyWebSearch(tavily => tavily.WithApiKey("tvly-test")));

        Assert.Null(exception);
    }

    [Fact]
    public void WithTavilyWebSearch_WithoutApiKey_FailsOnMissingApiKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new AgentBuilder()
            .WithTavilyWebSearch());

        Assert.Contains("Tavily API key is required", exception.Message);
    }
}
