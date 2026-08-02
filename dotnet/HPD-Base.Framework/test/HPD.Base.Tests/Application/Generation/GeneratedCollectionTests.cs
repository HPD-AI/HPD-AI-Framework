using FluentAssertions;
using HPD.Base;
using System.Text.Json.Serialization;
using Xunit;

namespace HPD.Base.Tests.Application.Generation;

public sealed class GeneratedCollectionTests
{
    [Fact]
    public void GeneratorProducesTypedCollectionFieldsSchemaAndJsonMetadata()
    {
        GeneratedProject.Collection.Id.Should().Be("projects");
        GeneratedProject.Collection.JsonTypeInfo.Type.Should().Be(typeof(GeneratedProject));

        GeneratedProject.Fields.OrganizationId.Id.Should().Be("organization-id");
        GeneratedProject.Fields.OrganizationId.StoredName.Should().Be("organizationId");
        GeneratedProject.Fields.Name.Operators.Should().HaveFlag(BaseFieldOperator.Order);
        GeneratedProject.Fields.OptionalNote.Nullable.Should().BeTrue();

        GeneratedProject.Collection.Definition.SchemaMode.Should().Be(SchemaMode.Strict);
        GeneratedProject.Collection.Definition.UnknownFields.Should().Be(UnknownFieldPolicy.Reject);
        GeneratedProject.Collection.Definition.Fields.Should().HaveCount(3);
        GeneratedProject.Collection.Definition.Indexes.Should().ContainSingle();
        GeneratedProject.Collection.Definition.Indexes![0].Parts.Should().ContainSingle();
        GeneratedProject.Collection.Definition.Indexes[0].Parts![0].FieldId
            .Should().Be("organization-id");
    }

    [Fact]
    public void ManualBuilderProducesValidatedImmutableCanonicalContract()
    {
        BaseCollection<GeneratedProject> collection =
            HPD.Base.BaseCollection.Define(
                "manual.projects",
                GeneratedApplicationJsonContext.Default.GeneratedProject,
                schema =>
                {
                    schema.String("organization-id", "organizationId").Required();
                    schema.String("name", "name").Required();
                    schema.String("optional-note", "optionalNote").Optional();
                    schema.Index("organization-name", "organization-id", "name")
                        .Required()
                        .Unique();
                });

        collection.Definition.Fields![0].Id.Should().Be("organization-id");
        collection.Definition.Fields[0].Nullable.Should().BeFalse();
        collection.Definition.Indexes![0].Enforcement.Should().Be(EnforcementOwner.Store);
        collection.Definition.Indexes[0].Unique.Should().BeTrue();

        CollectionDefinition snapshot = collection.Definition;
        snapshot.Fields![0] = snapshot.Fields[0] with { Name = "mutated" };
        collection.Definition.Fields![0].Name.Should().Be("organizationId");
    }

    [Fact]
    public void ManualBuilderRejectsUnknownIndexFields()
    {
        Action build = () => HPD.Base.BaseCollection.Define(
            "manual.projects",
            GeneratedApplicationJsonContext.Default.GeneratedProject,
            schema => schema.Index("missing", "unknown").Required());

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown field*");
    }

    [Fact]
    public void GeneratedAndManualContractsLowerToEquivalentCanonicalSchemas()
    {
        BaseCollection<GeneratedProject> manual =
            HPD.Base.BaseCollection.Define(
                "projects",
                GeneratedApplicationJsonContext.Default.GeneratedProject,
                schema =>
                {
                    schema.String("organization-id", "organizationId").Required();
                    schema.String("name", "name").Required();
                    schema.String("optional-note", "optionalNote").Optional();
                    schema.Index("organization", "organization-id").Advisory();
                });

        manual.Definition.Should().BeEquivalentTo(GeneratedProject.Collection.Definition);
    }
}

[BaseCollection("projects", typeof(GeneratedApplicationJsonContext))]
[BaseIndex("organization", nameof(OrganizationId), Required = false)]
internal sealed partial record GeneratedProject
{
    [BaseField("organization-id")]
    public required string OrganizationId { get; init; }

    [BaseField("name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string Name { get; init; }

    [BaseField("optional-note")]
    public string? OptionalNote { get; init; }
}

[JsonSerializable(typeof(GeneratedProject))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GeneratedApplicationJsonContext : JsonSerializerContext;
