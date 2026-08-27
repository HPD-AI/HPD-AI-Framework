using HPD.Agent;
using Microsoft.Extensions.AI;
using System.Text;

var contentStore = new InMemoryContentStore();
var skillStore = new ContentStoreSkillStore(contentStore, ContentStoreScopes.Skills);
await skillStore.InstallAsync(new SkillPackage
{
    Manifest = new SkillPackageManifest
    {
        Id = "runtime-aot",
        Name = "runtime_aot_guidance",
        Description = "Provides runtime-installed guidance used to verify Native AOT persistence.",
        Version = "1"
    },
    Instructions = new MemoryStream(Encoding.UTF8.GetBytes("Use the installed Native AOT guidance."))
});
var reconstructedStore = new ContentStoreSkillStore(contentStore, ContentStoreScopes.Skills);

var agent = await new AgentBuilder(new AgentConfig { Name = "skill-aot-smoke" })
    .WithSkillStore(reconstructedStore)
    .WithToolHarness<SkillAotSmokeHarness>(options => options.AddSkillsFromStore())
    .BuildAsync();

var functions = agent.DefaultOptions?.Tools?.OfType<AIFunction>().ToArray() ?? [];
var exitCode = !functions.Any(function => function.Name == "aot_guidance") ? 1
    : !functions.Any(function => function.Name == nameof(SkillAotSmokeHarness.Inspect)) ? 2
    : !functions.Any(function => function.Name == "aot_guide") ? 3
    : !functions.Any(function => function.Name == "runtime_aot_guidance") ? 4
    : functions.Any(function => function.AdditionalProperties?.ContainsKey(
        HPDCapabilityMetadata.AdditionalPropertiesKey) != true) ? 5
    : 0;
await agent.DisposeAsync();
return exitCode;

public sealed partial class SkillAotSmokeHarness
{
    [AIFunction]
    public string Inspect() => "inspected";

    [Skill]
    public Skill Guidance() => Skill.Create(
        name: "aot_guidance",
        description: "Provides guidance used to verify the Native AOT skill path.",
        instructions: SkillInstructions.FromText("Inspect first, then consult the guide."),
        capabilities:
        [
            SkillCapabilities.Function<SkillAotSmokeHarness>(nameof(Inspect)),
            SkillCapabilities.Resource(
                "aot_guide",
                "Reads the Native AOT smoke-test guide.",
                "Native AOT skill resources are projected as parameterless functions.")
        ]);
}
