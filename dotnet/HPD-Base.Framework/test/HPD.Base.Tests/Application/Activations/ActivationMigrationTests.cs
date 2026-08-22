using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace HPD.Base.Tests.Application.Activations;

public sealed class ActivationMigrationTests
{
    [Fact]
    public void Closed_projection_renames_properties_adds_constants_and_is_deterministic()
    {
        BaseActivationMigrationDefinition definition = new()
        {
            Id = "example.migration", Version = 1, OwningModuleId = "example",
            Source = Key("example.source", 1), Target = Key("example.target", 2),
            GrantId = "example.activation.migrate",
            Properties =
            [
                new BaseActivationMigrationProperty
                {
                    TargetPropertyPath = ["target.name"], SourcePropertyPath = ["source.name"],
                },
                new BaseActivationMigrationProperty
                {
                    TargetPropertyPath = ["target.enabled"], CanonicalConstant = "true"u8.ToArray().ToImmutableArray(),
                },
            ],
            Checksum = [],
        };
        var registration = new BaseActivationMigrationRegistration<SourceInput, TargetInput>
        {
            Definition = definition, SourceTypeInfo = MigrationJsonContext.Default.SourceInput,
            TargetTypeInfo = MigrationJsonContext.Default.TargetInput,
            SourceBindings = [BaseModuleDtoPropertyBinding.Create<SourceInput, string>("source.name", "name")],
            TargetBindings =
            [
                BaseModuleDtoPropertyBinding.Create<TargetInput, string>("target.name", "displayName"),
                BaseModuleDtoPropertyBinding.Create<TargetInput, bool>("target.enabled", "enabled"),
            ],
        };
        var installed = new BaseInstalledActivationMigration<SourceInput, TargetInput>(registration);
        ImmutableArray<byte> projected = installed.Project(
            JsonSerializer.SerializeToUtf8Bytes(new SourceInput { Name = "Ada" }, MigrationJsonContext.Default.SourceInput));
        TargetInput value = JsonSerializer.Deserialize(projected.AsSpan(), MigrationJsonContext.Default.TargetInput)!;

        value.Should().Be(new TargetInput { DisplayName = "Ada", Enabled = true });
        installed.Definition.Checksum.Should().HaveCount(32);
        new BaseInstalledActivationMigration<SourceInput, TargetInput>(registration).Definition.Checksum
            .Should().Equal(installed.Definition.Checksum);
    }

    [Fact]
    public void Missing_target_leaf_fails_graph_construction()
    {
        Action action = () => new BaseInstalledActivationMigration<SourceInput, TargetInput>(new()
        {
            Definition = new BaseActivationMigrationDefinition
            {
                Id = "example.migration", Version = 1, OwningModuleId = "example",
                Source = Key("example.source", 1), Target = Key("example.target", 2),
                GrantId = "example.activation.migrate",
                Properties = [new BaseActivationMigrationProperty
                {
                    TargetPropertyPath = ["target.name"], SourcePropertyPath = ["source.name"],
                }], Checksum = [],
            },
            SourceTypeInfo = MigrationJsonContext.Default.SourceInput,
            TargetTypeInfo = MigrationJsonContext.Default.TargetInput,
            SourceBindings = [BaseModuleDtoPropertyBinding.Create<SourceInput, string>("source.name", "name")],
            TargetBindings =
            [
                BaseModuleDtoPropertyBinding.Create<TargetInput, string>("target.name", "displayName"),
                BaseModuleDtoPropertyBinding.Create<TargetInput, bool>("target.enabled", "enabled"),
            ],
        });

        action.Should().Throw<InvalidOperationException>().WithMessage("base.activation.migrationInvalid");
    }

    private static BaseActivationDefinitionKey Key(string id, int version) => new()
    {
        Id = id, Version = version, Checksum = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)).ToImmutableArray(),
    };
}

internal sealed record SourceInput { public required string Name { get; init; } }
internal sealed record TargetInput { public required string DisplayName { get; init; } public required bool Enabled { get; init; } }

[JsonSerializable(typeof(SourceInput))]
[JsonSerializable(typeof(TargetInput))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class MigrationJsonContext : JsonSerializerContext;
