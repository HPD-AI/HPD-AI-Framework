using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base;
using Xunit;

namespace HPD.Base.Tests.Application.Collections;

public sealed partial class BaseCollectionTests
{
    [Fact]
    public void CreateProducesImmutableTypedContract()
    {
        BaseField<ProjectDocument, string>? organization = null;
        BaseField<ProjectDocument, string>? name = null;
        var metadata = new TestJsonContext(BaseSerializerGeneratedContract.CreateOptions(System.Text.Json.JsonNamingPolicy.CamelCase)).ProjectDocument;
        var collection = BaseCollection<ProjectDocument>.Create(
            Definition(),
            metadata,
            fields =>
            {
                organization = fields.Add<string>(
                    "organization-id",
                    "OrganizationId",
                    "organizationId",
                    operators: BaseFieldOperator.Equal);
                name = fields.Add<string>(
                    "name",
                    "Name",
                    "name",
                    operators: BaseFieldOperator.Equal | BaseFieldOperator.Order);
            });

        collection.Id.Should().Be("projects");
        collection.JsonTypeInfo.Type.Should().Be(typeof(ProjectDocument));
        organization!.Id.Should().Be("organization-id");
        organization.WireName.Should().Be("organizationId");
        name!.Operators.Should().HaveFlag(BaseFieldOperator.Order);
    }

    [Fact]
    public void CreateRejectsDuplicateFieldPaths()
    {
        var action = () => BaseCollection<ProjectDocument>.Create(
            Definition(),
            TestJsonContext.Default.ProjectDocument,
            fields =>
            {
                fields.Add<string>("name", "name", "name") ;
                fields.Add<string>("name", "otherName", "otherName") ;
            });

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Field 'name' is already declared.");
    }

    [Fact]
    public void CreateRejectsDuplicateStoredNames()
    {
        var action = () => BaseCollection<ProjectDocument>.Create(
            Definition(), TestJsonContext.Default.ProjectDocument, fields =>
            {
                fields.Add<string>("first-name", "name", "name") ;
                fields.Add<string>("second-name", "name", "name") ;
            });

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*application name 'name' is already declared*");
    }

    private static CollectionDefinition Definition() =>
        new()
        {
            Id = "projects",
            Name = "projects",
            Kind = "record",
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
        };

    private sealed record ProjectDocument(
        string OrganizationId,
        string Name);

    [JsonSerializable(typeof(ProjectDocument))]
    private sealed partial class TestJsonContext : JsonSerializerContext;
}
