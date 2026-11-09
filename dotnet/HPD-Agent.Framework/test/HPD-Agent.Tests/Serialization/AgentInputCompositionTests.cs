using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Serialization;

public sealed class AgentInputCompositionTests
{
    [Fact]
    public void PublicFlag_ControlsPublicSubmission_NotCodecMembership()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var composition = AgentInputComposition.Create(
        [
            new AgentInputModuleFragment
            {
                ModuleId = "tests.agent.inputs",
                Inputs =
                [
                    Descriptor<PublicProbeInputEvent>("PUBLIC_PROBE_INPUT", isPublic: true),
                    Descriptor<InternalProbeInputEvent>("INTERNAL_PROBE_INPUT", isPublic: false)
                ]
            }
        ]);
        var codec = new AgentInputCodec(ProviderComposition.Create([]), composition);

        var publicJson = codec.Serialize(new PublicProbeInputEvent("public"));
        var internalJson = codec.Serialize(new InternalProbeInputEvent("internal"));

        Assert.IsType<PublicProbeInputEvent>(codec.Deserialize(publicJson));
        Assert.IsType<InternalProbeInputEvent>(codec.Deserialize(internalJson));
        Assert.IsType<PublicProbeInputEvent>(codec.DeserializePublic(publicJson));
        Assert.Throws<JsonException>(() => codec.DeserializePublic(internalJson));
        Assert.True(composition.IsPublic(typeof(PublicProbeInputEvent)));
        Assert.False(composition.IsPublic(typeof(InternalProbeInputEvent)));
        Assert.Throws<JsonException>(() => codec.Deserialize(
            publicJson.Replace("\"version\":\"1.0\"", "\"version\":\"2.0\"", StringComparison.Ordinal)));

        AgentInputDescriptor Descriptor<T>(string discriminator, bool isPublic)
            where T : AgentInputEvent => new()
        {
            Discriminator = discriminator,
            InputType = typeof(T),
            JsonTypeInfo = options.GetTypeInfo(typeof(T)),
            ModuleId = "tests.agent.inputs",
            IsPublic = isPublic
        };
    }

    [Fact]
    public void DuplicateDiscriminator_IsRejected()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        var error = Assert.Throws<ArgumentException>(() => AgentInputComposition.Create(
        [
            new AgentInputModuleFragment
            {
                ModuleId = "tests.agent.inputs.one",
                Inputs =
                [
                    Descriptor<PublicProbeInputEvent>("tests.agent.inputs.one")
                ]
            },
            new AgentInputModuleFragment
            {
                ModuleId = "tests.agent.inputs.two",
                Inputs =
                [
                    Descriptor<InternalProbeInputEvent>("tests.agent.inputs.two")
                ]
            }
        ]));

        Assert.Contains("Duplicate input discriminator", error.Message);
        AgentInputDescriptor Descriptor<T>(string moduleId)
            where T : AgentInputEvent => new()
        {
            Discriminator = "SHARED_INPUT",
            InputType = typeof(T),
            JsonTypeInfo = options.GetTypeInfo(typeof(T)),
            ModuleId = moduleId
        };
    }

    private sealed record PublicProbeInputEvent(string Value) : AgentInputEvent;

    private sealed record InternalProbeInputEvent(string Value) : AgentInputEvent;
}
