using HPD.Agent.Audio.VoiceActivity;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityPublicApiInventoryTests
{
    [Fact]
    public void Public_voice_activity_contract_inventory_is_closed_and_effect_free()
    {
        var assembly = typeof(VoiceActivityRequest).Assembly;
        var types = assembly.GetExportedTypes()
            .Where(static type => type.Namespace == "HPD.Agent.Audio.VoiceActivity")
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "ActivityDegradationPolicy", "ActivityResponsiveness", "ActivitySourceKind",
            "ActivitySourceRequest", "ActivitySourceRole", "EffectiveActivitySource",
            "ProviderActivityVisibility", "VoiceActivityHealthState", "VoiceActivityJsonContext",
            "VoiceActivityNoiseEnvironment", "VoiceActivityOperationalLimits", "VoiceActivityProfile",
            "VoiceActivityRequest", "VoiceActivitySnapshot", "VoiceActivitySpeechContinuity",
            "VoiceActivityUpdateDisposition", "VoiceActivityUpdateRequest", "VoiceActivityUpdateResult",
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
