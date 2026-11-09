using HPD.Agent.Audio;

namespace HPD.Agent.Audio.Tests;

public sealed class AudioPublicApiInventoryTests
{
    [Fact]
    public void Exported_type_surface_matches_reviewed_baseline()
    {
        var assembly = typeof(AgentBuilderAudioRuntimeExtensions).Assembly;
        var actual = assembly.GetExportedTypes()
            .Select(static type => type.FullName)
            .Where(static name => name is not null)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = File.ReadAllLines(
                Path.Combine(AppContext.BaseDirectory, "PublicApi", "AudioPublicApi.txt"))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Runtime_infrastructure_and_fakes_are_not_exported()
    {
        var exported = typeof(AgentBuilderAudioRuntimeExtensions).Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(
            exported,
            static name => name.StartsWith("HPD.Agent.Audio.Runtime.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            exported,
            static name => name.Contains(".Fake", StringComparison.Ordinal));
        Assert.DoesNotContain(
            exported,
            static name => name.Contains(".InMemory", StringComparison.Ordinal));
    }

    [Fact]
    public void Exported_type_names_do_not_carry_contract_version_suffixes()
    {
        var exported = typeof(AgentBuilderAudioRuntimeExtensions).Assembly
            .GetExportedTypes()
            .Select(static type => type.Name);

        Assert.DoesNotContain(
            exported,
            static name => name.Contains("V1", StringComparison.Ordinal) ||
                name.Contains("V2", StringComparison.Ordinal));
    }
}
