using FluentAssertions;
using HPD.Base.Application.Collections;
using HPD.Base.Application.Generation;
using HPD.Base.Schema;
using HPD.Base.Application.Schema;
using System.Text.Json.Serialization;
using Xunit;

namespace HPD.Base.Application.Tests.Generation;

public sealed class GeneratedCollectionTests
{
    [Fact]
    public void GeneratorProducesTypedCollectionFieldsSchemaAndJsonMetadata()
    {
        GeneratedProject.Collection.Id.Should().Be("projects");
        GeneratedProject.Collection.JsonTypeInfo.Type.Should().Be(typeof(GeneratedProject));

        GeneratedProject.Fields.OrganizationId.Path.Should().Be("organizationId");
        GeneratedProject.Fields.Name.Operators.Should().HaveFlag(BaseFieldOperator.Order);
        GeneratedProject.Fields.OptionalNote.Nullable.Should().BeTrue();

        GeneratedProject.Collection.Definition.SchemaMode.Should().Be(SchemaMode.Strict);
        GeneratedProject.Collection.Definition.UnknownFields.Should().Be(UnknownFieldPolicy.Reject);
        GeneratedProject.Collection.Definition.Fields.Should().HaveCount(3);
        GeneratedProject.Collection.Definition.Indexes.Should().ContainSingle();
        GeneratedProject.Collection.Definition.Indexes![0].Parts.Should().ContainSingle();
        GeneratedProject.Collection.Definition.Indexes[0].Parts![0].FieldPath
            .Should().Be("organizationId");
    }

    [Fact]
    public void ManualBuilderProducesValidatedImmutableCanonicalContract()
    {
        BaseCollection<GeneratedProject> collection =
            HPD.Base.Application.Schema.BaseCollection.Define(
                "manual.projects",
                GeneratedApplicationJsonContext.Default.GeneratedProject,
                schema =>
                {
                    schema.String("organizationId").Required();
                    schema.String("name").Required();
                    schema.String("optionalNote").Optional();
                    schema.Index("organization-name", "organizationId", "name")
                        .Required()
                        .Unique();
                });

        collection.Field<string>("organizationId").Nullable.Should().BeFalse();
        collection.Definition.Indexes![0].Enforcement.Should().Be(EnforcementOwner.Store);
        collection.Definition.Indexes[0].Unique.Should().BeTrue();

        CollectionDefinition snapshot = collection.Definition;
        snapshot.Fields![0] = snapshot.Fields[0] with { Name = "mutated" };
        collection.Definition.Fields![0].Name.Should().Be("organizationId");
    }

    [Fact]
    public void ManualBuilderRejectsUnknownIndexFields()
    {
        Action build = () => HPD.Base.Application.Schema.BaseCollection.Define(
            "manual.projects",
            GeneratedApplicationJsonContext.Default.GeneratedProject,
            schema => schema.Index("missing", "unknown").Required());

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown field*");
    }
}

[BaseCollection("projects", typeof(GeneratedApplicationJsonContext))]
[BaseIndex("organization", nameof(OrganizationId))]
internal sealed partial record GeneratedProject
{
    public required string OrganizationId { get; init; }

    [BaseField(Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string Name { get; init; }

    public string? OptionalNote { get; init; }
}

[JsonSerializable(typeof(GeneratedProject))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GeneratedApplicationJsonContext : JsonSerializerContext;
