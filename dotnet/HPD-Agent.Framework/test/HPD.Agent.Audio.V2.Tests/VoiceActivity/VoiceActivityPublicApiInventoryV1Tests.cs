using HPD.Agent.Audio.VoiceActivity;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityPublicApiInventoryV1Tests
{
    [Fact]
    public void Public_voice_activity_contract_inventory_is_closed_and_effect_free()
    {
        var assembly = typeof(VoiceActivityRequestV1).Assembly;
        var types = assembly.GetExportedTypes()
            .Where(static type => type.Namespace == "HPD.Agent.Audio.VoiceActivity")
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "ActivityDegradationPolicyV1", "ActivityResponsivenessV1", "ActivitySourceKindV1",
            "ActivitySourceRequestV1", "ActivitySourceRoleV1", "EffectiveActivitySourceV1",
            "ProviderActivityVisibilityV1", "VoiceActivityHealthStateV1", "VoiceActivityJsonContextV1",
            "VoiceActivityNoiseEnvironmentV1", "VoiceActivityOperationalLimitsV1", "VoiceActivityProfileV1",
            "VoiceActivityRequestV1", "VoiceActivitySnapshotV1", "VoiceActivitySpeechContinuityV1",
            "VoiceActivityUpdateDispositionV1", "VoiceActivityUpdateRequestV1", "VoiceActivityUpdateResultV1",
        }, types);

        var forbidden = new[]
        {
            typeof(Stream), typeof(Task), typeof(CancellationToken), typeof(IServiceProvider),
        };
        foreach (var type in assembly.GetExportedTypes().Where(static type =>
                     type.Namespace == "HPD.Agent.Audio.VoiceActivity"))
        {
            Assert.DoesNotContain(type.GetProperties(), property =>
                forbidden.Any(item => item.IsAssignableFrom(property.PropertyType)));
        }
    }
}
