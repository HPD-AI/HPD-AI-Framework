using FluentAssertions;

namespace HPD.Agent.ModelsDev.Tests;

public sealed class ModelsDevModelIdTests
{
    [Fact]
    public void TryParse_accepts_provider_model_reference()
    {
        ModelsDevModelId.TryParse("openai/gpt-4o", out var id).Should().BeTrue();
        id.Provider.Should().Be("openai");
        id.Model.Should().Be("gpt-4o");
        id.ToString().Should().Be("openai/gpt-4o");
        id.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TryParse_splits_on_first_slash()
    {
        ModelsDevModelId.TryParse("openrouter/deepseek/deepseek-chat", out var id).Should().BeTrue();
        id.Provider.Should().Be("openrouter");
        id.Model.Should().Be("deepseek/deepseek-chat");
    }

    [Theory]
    [InlineData("")]
    [InlineData("openai")]
    [InlineData("/gpt-4o")]
    [InlineData("openai/")]
    public void TryParse_rejects_invalid_reference(string value)
    {
        ModelsDevModelId.TryParse(value, out var id).Should().BeFalse();
        id.IsZero.Should().BeTrue();
        id.IsValid.Should().BeFalse();
    }
}
