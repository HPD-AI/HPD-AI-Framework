using HPD.Agent.Tests.TestToolHarnesses;

namespace HPD.Agent.Tests.Skills;

public sealed class SkillApiTests
{
    [Fact]
    public void Create_RejectsMissingRequiredDefinitionFields()
    {
        var instructions = SkillInstructions.FromText("Analyze carefully.");

        Assert.Throws<ArgumentException>(() => Skill.Create("", "Description.", instructions));
        Assert.Throws<ArgumentException>(() => Skill.Create("analysis", "", instructions));
        Assert.Throws<ArgumentNullException>(() => Skill.Create("analysis", "Description.", null!));
    }

    [Fact]
    public async Task Create_ProducesImmutableStructuredDefinition()
    {
        var skill = Skill.Create(
            name: "data_analysis",
            description: "Analyzes validated data.",
            instructions: SkillInstructions.FromText("Validate before analysis."),
            reinforcement: SkillInstructions.FromText("Do not skip validation."),
            capabilities:
            [
                SkillCapabilities.Function<CombinedCapabilitiesTools>(
                    nameof(CombinedCapabilitiesTools.AnalyzeData))
            ],
            id: "installed:data-analysis@1");

        Assert.Equal("installed:data-analysis@1", skill.Id);
        Assert.Equal(SkillActivationLifetime.MessageTurn, skill.Lifetime);
        var function = Assert.IsType<SkillFunctionReference<CombinedCapabilitiesTools>>(
            Assert.Single(skill.Capabilities));
        Assert.Equal(nameof(CombinedCapabilitiesTools.AnalyzeData), function.MemberName);

        // Static providers intentionally ignore invocation context.
        Assert.Equal("Validate before analysis.", await skill.Instructions(null!, default));
        Assert.Equal("Do not skip validation.", await skill.Reinforcement!(null!, default));
    }

    [Fact]
    public void Create_SnapshotsCapabilities()
    {
        var capabilities = new List<SkillCapability>
        {
            SkillCapabilities.Function<CombinedCapabilitiesTools>(nameof(CombinedCapabilitiesTools.AnalyzeData))
        };

        var skill = Skill.Create(
            "analysis",
            "Analysis guidance.",
            SkillInstructions.FromText("Analyze carefully."),
            capabilities);

        capabilities.Clear();

        Assert.Single(skill.Capabilities);
    }

    [Theory]
    [InlineData(SkillActivationLifetime.ModelIteration)]
    [InlineData(SkillActivationLifetime.Session)]
    public void Create_RejectsUnsupportedLifetime(SkillActivationLifetime lifetime)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Skill.Create(
            "analysis",
            "Analysis guidance.",
            SkillInstructions.FromText("Analyze carefully."),
            lifetime: lifetime));
    }
}
