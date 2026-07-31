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
        var collection = BaseCollection<ProjectDocument>.Create(
            Definition(),
            TestJsonContext.Default.ProjectDocument,
            fields =>
            {
                fields.Add<string>(
                    "organizationId",
                    operators: BaseFieldOperator.Equal);
                fields.Add<string>(
                    "name",
                    operators: BaseFieldOperator.Equal | BaseFieldOperator.Order);
            });

        collection.Id.Should().Be("projects");
        collection.JsonTypeInfo.Type.Should().Be(typeof(ProjectDocument));
        collection.Field<string>("organizationId").Path.Should().Be("organizationId");
        collection.Field<string>("name").Operators.Should().HaveFlag(BaseFieldOperator.Order);
    }

    [Fact]
    public void CreateRejectsDuplicateFieldPaths()
    {
        var action = () => BaseCollection<ProjectDocument>.Create(
            Definition(),
            TestJsonContext.Default.ProjectDocument,
            fields =>
            {
                fields.Add<string>("name");
                fields.Add<string>("name");
            });

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Field 'name' is already declared.");
    }

    [Fact]
    public void FieldRejectsIncorrectValueType()
    {
        var collection = BaseCollection<ProjectDocument>.Create(
            Definition(),
            TestJsonContext.Default.ProjectDocument,
            fields => fields.Add<string>("name"));

        var action = () => collection.Field<int>("name");

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*field 'name' is not declared as 'System.Int32'.");
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
