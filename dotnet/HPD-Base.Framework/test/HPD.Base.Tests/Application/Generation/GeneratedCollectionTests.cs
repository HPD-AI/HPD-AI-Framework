using FluentAssertions;
using HPD.Base;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Tests.Application.Generation;

public sealed class GeneratedCollectionTests
{
    private static GeneratedApplicationJsonContext Metadata() =>
        new(BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase));
    [Fact]
    public void GeneratorProducesTypedCollectionFieldsSchemaAndJsonMetadata()
    {
        FinalizeGenerated();
        GeneratedProject.Collection.Id.Should().Be("projects");

        GeneratedProject.Fields.OrganizationId.Id.Should().Be("organization-id");
        GeneratedProject.Fields.OrganizationId.WireName.Should().Be("organizationId");
        GeneratedProject.Fields.Name.Operators.Should().HaveFlag(BaseFieldOperator.Order);
        GeneratedProject.Fields.OptionalNote.Nullable.Should().BeTrue();

        GeneratedProject.Collection.Definition.SchemaMode.Should().Be(SchemaMode.Strict);
        GeneratedProject.Collection.Definition.UnknownFields.Should().Be(UnknownFieldPolicy.Reject);
        GeneratedProject.Collection.Definition.Fields.Should().HaveCount(3);
        GeneratedProject.Collection.Definition.Indexes.Should().ContainSingle();
        GeneratedProject.Collection.Definition.Indexes![0].Parts.Should().ContainSingle();
        GeneratedProject.Collection.Definition.Indexes[0].Parts![0].FieldId
            .Should().Be("organization-id");
        GeneratedProject.Collection.Definition.Fields!.Single(field => field.Id == "organization-id").ApplicationName.Should().Be("OrganizationId");
        GeneratedProject.Collection.Definition.Fields.Single(field => field.Id == "organization-id").WireName.Should().Be("organizationId");
        GeneratedProject.Collection.Definition.SerializerContractChecksum.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ManualBuilderProducesValidatedImmutableCanonicalContract()
    {
        var metadata = Metadata().GeneratedProject;
        BaseCollection<GeneratedProject> collection =
            HPD.Base.BaseCollection.Define(
                "manual.projects",
                metadata,
                schema =>
                {
                    schema.String("organization-id", "OrganizationId", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "organizationId")).Required();
                    schema.String("name", "Name", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "name")).Required();
                    schema.String("optional-note", "OptionalNote", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "optionalNote")).Optional();
                    schema.Index("organization-name", "organization-id", "name")
                        .Required()
                        .Unique();
                });

        collection.Definition.Fields![0].Id.Should().Be("organization-id");
        collection.Definition.Fields[0].Nullable.Should().BeFalse();
        collection.Definition.Indexes![0].Enforcement.Should().Be(EnforcementOwner.Store);
        collection.Definition.Indexes[0].Unique.Should().BeTrue();

        CollectionDefinition snapshot = collection.Definition;
        snapshot.Fields![0] = snapshot.Fields[0] with { ApplicationName = "mutated" };
        collection.Definition.Fields![0].ApplicationName.Should().Be("OrganizationId");
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
        FinalizeGenerated();
        var metadata = Metadata().GeneratedProject;
        BaseCollection<GeneratedProject> manual =
            HPD.Base.BaseCollection.Define(
                "projects",
                metadata,
                schema =>
                {
                    schema.String("organization-id", "OrganizationId", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "organizationId")).Required();
                    schema.String("name", "Name", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "name")).Required();
                    schema.String("optional-note", "OptionalNote", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "optionalNote")).Optional();
                    schema.Index("organization", "organization-id").Advisory();
                });

        manual.Definition.Should().BeEquivalentTo(GeneratedProject.Collection.Definition);
    }

    private static void FinalizeGenerated()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));
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
