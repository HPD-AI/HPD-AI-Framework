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
        GeneratedProject.Collection.Definition.Indexes[0].Parts[0].FieldOrdinal.Should().Be(2);
        GeneratedProject.Collection.Definition.Fields!.Single(field => field.Id == "organization-id").ApplicationName.Should().Be("OrganizationId");
        GeneratedProject.Collection.Definition.Fields.Single(field => field.Id == "organization-id").WireName.Should().Be("organizationId");
        GeneratedProject.Collection.Definition.SerializerContractChecksum.Should().MatchRegex("^[0-9a-f]{64}$");
        FieldDefinition name = GeneratedProject.Collection.Definition.Fields.Single(field => field.Id == "name");
        name.Presence.Should().Be(BaseFieldPresence.Required);
        name.Nullability.Should().Be(BaseFieldNullability.NonNullable);
        name.ScalarConstraints!.MinimumUtf8Bytes.Should().Be(1);
        name.ScalarConstraints.MaximumUtf8Bytes.Should().Be(256);
        name.ScalarConstraints.StringNormalization.Should().Be(BaseStringNormalizationRequirement.RequireNfc);
        name.ScalarConstraintChecksum!.Value.IsValid.Should().BeTrue();
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
                    schema.String("name", "Name", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "name")).Required()
                        .Constraints(value => value.Utf8Bytes(1, 256).RequireNfc());
                    schema.String("optional-note", "OptionalNote", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "optionalNote")).Optional();
                    schema.Index("organization-name", 1, index => index
                        .Part(BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "organizationId"))
                        .Part(BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "name"))
                        .Unique());
                });

        collection.Definition.Fields![0].Id.Should().Be("organization-id");
        collection.Definition.Fields[0].Nullability.Should().Be(BaseFieldNullability.NonNullable);
        collection.Definition.Indexes![0].StoreRequired.Should().BeTrue();
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
            schema => schema.Index("missing", 1, _ => { }));

        build.Should().Throw<InvalidOperationException>()
            .WithMessage(BaseSchemaErrorCodes.ContractInvalid);
    }

    [Fact]
    public void ManualBuilderSealsIndependentPresenceConstraintsAndExactIndexAuthority()
    {
        var metadata = Metadata().GeneratedProject;
        var organization = BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "organizationId");
        var optionalNote = BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "optionalNote");
        BaseCollection<GeneratedProject> collection = HPD.Base.BaseCollection.Define("l54.projects", metadata, schema =>
        {
            schema.String("organization-id", "OrganizationId", organization).Required().Constraints(value => value.Utf8Bytes(1, 64).RequireNfc());
            schema.String("name", "Name", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "name")).Required();
            schema.String("optional-note", "OptionalNote", optionalNote).OptionalNonNullable().Constraints(value => value.Utf8Bytes(0, 256));
            schema.Index("l54.idx.organization", 1, index => index
                .Part(organization)
                .Part(optionalNote, BaseIndexSortDirection.Descending, nullOrder: BaseIndexNullOrder.ValueThenNullThenMissing)
                .Unique()
                .Predicate(predicate =>
                {
                    BaseIndexPredicateId defined = predicate.IsDefined("defined", optionalNote);
                    BaseIndexPredicateId nonNull = predicate.IsNotNull("non-null", optionalNote);
                    predicate.Root(predicate.And("root", defined, nonNull));
                }));
        });

        FieldDefinition constrained = collection.Definition.Fields!.Single(value => value.Id == "optional-note");
        constrained.Presence.Should().Be(BaseFieldPresence.Optional);
        constrained.Nullability.Should().Be(BaseFieldNullability.NonNullable);
        constrained.ScalarConstraintChecksum.Should().NotBeNull();
        BaseLogicalIndexDefinition index = collection.Definition.Indexes!.Single();
        index.Unique.Should().BeTrue();
        index.StoreRequired.Should().BeTrue();
        index.Parts.Should().HaveCount(2);
        index.MembershipPredicate.Nodes.Should().HaveCount(3);
        index.Checksum.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ManualBuilderRejectsDisconnectedPredicateNodes()
    {
        var metadata = Metadata().GeneratedProject;
        var organization = BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "organizationId");
        Action build = () => HPD.Base.BaseCollection.Define("l54.invalid", metadata, schema =>
        {
            schema.String("organization-id", "OrganizationId", organization).Required();
            schema.String("name", "Name", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "name")).Required();
            schema.String("optional-note", "OptionalNote", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "optionalNote")).Optional();
            schema.Index("l54.idx.invalid", 1, index => index.Part(organization).Predicate(predicate =>
            {
                BaseIndexPredicateId root = predicate.True("root");
                predicate.False("unreachable");
                predicate.Root(root);
            }));
        });

        build.Should().Throw<InvalidOperationException>().WithMessage(BaseSchemaErrorCodes.ContractInvalid);
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
                    schema.String("name", "Name", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "name")).Required()
                        .Constraints(value => value.Utf8Bytes(1, 256).RequireNfc());
                    schema.String("optional-note", "OptionalNote", BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "optionalNote")).Optional();
                    BaseJsonProperty<GeneratedProject, string> organization = BaseJsonProperty<GeneratedProject, string>.Bind(metadata, "organizationId");
                    schema.Index("organization", 1, index => index.Part(organization).Predicate(predicate =>
                    {
                        BaseIndexPredicateId defined = predicate.IsDefined("defined", organization);
                        BaseIndexPredicateId equal = predicate.Equal("equal", organization, "acme");
                        BaseIndexPredicateId notNull = predicate.IsNotNull("not-null", organization);
                        predicate.Root(predicate.And("root", defined, equal, notNull));
                    }));
                });

        manual.Definition.Should().BeEquivalentTo(GeneratedProject.Collection.Definition);
    }

    [Fact]
    public void GeneratedAndManualEnumCodecsBindTheExactDeclaredLiteralSet()
    {
        var metadata = Metadata().GeneratedEnumRecord;
        BaseCollection<GeneratedEnumRecord> manual = HPD.Base.BaseCollection.Define(
            "generated-enums",
            metadata,
            schema => schema.Enum("state", "State", BaseJsonProperty<GeneratedEnumRecord, GeneratedState>.Bind(metadata, "state"))
                .Required()
                .Constraints(value => value.EnumLiterals("Active", "Disabled")));

        FieldDefinition generated = GeneratedEnumRecord.Collection.Definition.Fields!.Single();
        FieldDefinition authored = manual.Definition.Fields!.Single();
        generated.ScalarCodec.Should().BeEquivalentTo(authored.ScalarCodec);
        generated.ScalarConstraintChecksum.Should().Be(authored.ScalarConstraintChecksum);
        string json = JsonSerializer.Serialize(new GeneratedEnumRecord { State = GeneratedState.Active }, metadata);
        json.Should().Be("{\"state\":\"Active\"}");
        Action numericWire = () => JsonSerializer.Deserialize("{\"state\":0}", metadata);
        numericWire.Should().Throw<JsonException>();

        BaseScalarCodecAuthority other = BaseGeneratedSchemaRegistration.ScalarCodec(
            BaseScalarKind.ClosedEnum,
            BaseGeneratedSchemaRegistration.EnumQualifier("Active", "Disabled", "Pending"));
        other.Id.Should().NotBe(generated.ScalarCodec!.Id);
        other.CodecChecksum.Should().NotBe(generated.ScalarCodec.CodecChecksum);
    }

    [Fact]
    public void RenamedClosedEnumUsesOnlyItsExactWireVocabularyAtTheGeneratedBoundary()
    {
        var metadata = Metadata().GeneratedWireEnumRecord;
        _ = GeneratedWireEnumRecord.Collection.Definition;

        JsonSerializer.Serialize(new GeneratedWireEnumRecord { State = GeneratedWireState.Active }, metadata)
            .Should().Be("{\"state\":\"active-wire\"}");
        JsonSerializer.Deserialize("{\"state\":\"active-wire\"}", metadata)!.State.Should().Be(GeneratedWireState.Active);
        ((Action)(() => JsonSerializer.Deserialize("{\"state\":0}", metadata))).Should().Throw<JsonException>();
        ((Action)(() => JsonSerializer.Deserialize("{\"state\":\"ACTIVE-WIRE\"}", metadata))).Should().Throw<JsonException>();
        ((Action)(() => JsonSerializer.Deserialize("{\"state\":\"Active\"}", metadata))).Should().Throw<JsonException>();
        ((Action)(() => JsonSerializer.Deserialize("{\"state\":\"unknown\"}", metadata))).Should().Throw<JsonException>();
        ((Action)(() => JsonSerializer.Serialize(new GeneratedWireEnumRecord { State = (GeneratedWireState)99 }, metadata))).Should().Throw<JsonException>();
    }

    [Fact]
    public void OptionalNonNullableNullSerializesAsAGenuinelyMissingField()
    {
        string json = JsonSerializer.Serialize(new GeneratedOptionalNonNullableRecord(),
            GeneratedOptionalNonNullableJsonContext.Default.GeneratedOptionalNonNullableRecord);

        json.Should().Be("{}");
    }

    [Fact]
    public void ManualBuilderCoversEveryPreviouslyMissingScalarAndBinaryEquality()
    {
        var metadata = Metadata().ManualScalarRecord;
        var binary = BaseJsonProperty<ManualScalarRecord, BaseBinary>.Bind(metadata, "binary");
        BaseCollection<ManualScalarRecord> collection = HPD.Base.BaseCollection.Define("manual.scalars", metadata, schema =>
        {
            schema.UInt32("uint32", "UInt32", BaseJsonProperty<ManualScalarRecord, uint>.Bind(metadata, "uint32")).Required();
            schema.UInt64("uint64", "UInt64", BaseJsonProperty<ManualScalarRecord, ulong>.Bind(metadata, "uint64")).Required();
            schema.Guid("guid", "Guid", BaseJsonProperty<ManualScalarRecord, Guid>.Bind(metadata, "guid")).Required();
            schema.Binary("binary", "Binary", binary).Required().Constraints(value => value.BinaryBytes(32));
            schema.CanonicalJson("json", "Json", BaseJsonProperty<ManualScalarRecord, BaseCanonicalJson>.Bind(metadata, "json")).Required()
                .Constraints(value => value.CanonicalJson(256, BaseJsonShape.Object, 8, 16, 16, 64, 128, 128));
            schema.Array("items", "Items", BaseJsonProperty<ManualScalarRecord, string[]>.Bind(metadata, "items")).Required()
                .Constraints(value => value.CollectionItems(0, 8));
            schema.Index("manual.binary", 1, index => index.Part(binary).Unique().Predicate(predicate =>
                predicate.Root(predicate.Equal("root", binary, BaseBinary.From([0, 1, 255])))));
        });

        collection.Definition.Fields!.Select(static field => field.ScalarKind).Should().Contain(
            [BaseScalarKind.UInt32, BaseScalarKind.UInt64, BaseScalarKind.Guid, BaseScalarKind.Binary, BaseScalarKind.CanonicalJson, BaseScalarKind.FrozenArray]);
        collection.Definition.Indexes.Should().ContainSingle().Which.MembershipPredicate.Nodes.Should().ContainSingle()
            .Which.Literal!.Kind.Should().Be(BaseScalarKind.Binary);
    }

    [Fact]
    public void FrozenArrayCannotBecomeAnIndexPartWithoutElementCodecAuthority()
    {
        var metadata = Metadata().ManualScalarRecord;
        var items = BaseJsonProperty<ManualScalarRecord, string[]>.Bind(metadata, "items");
        Action build = () => HPD.Base.BaseCollection.Define("manual.array-index", metadata, schema =>
        {
            schema.Array("items", "Items", items).Required().Constraints(value => value.CollectionItems(0, 8));
            schema.Index("manual.array", 1, index => index.Part(items).Unique());
        });

        build.Should().Throw<InvalidOperationException>().WithMessage(BaseSchemaErrorCodes.ContractInvalid);
    }

    [Fact]
    public void ExplicitNeverZeroOrderAndInactiveAlwaysFinalizeThroughRealMetadata()
    {
        var services = new ServiceCollection();
        Action finalize = () => services.AddHPDBase(builder => builder.AddCollection(GeneratedIgnoreContract.Collection));

        finalize.Should().NotThrow();
        GeneratedIgnoreContract.Collection.Definition.Fields.Should().ContainSingle()
            .Which.ApplicationName.Should().Be(nameof(GeneratedIgnoreContract.Active));
    }

    private static void FinalizeGenerated()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));
    }
}

[BaseCollection("projects", typeof(GeneratedApplicationJsonContext))]
[BaseIndex("organization", Unique = false, StoreRequired = false)]
[BaseIndexPart("organization", 0, nameof(OrganizationId))]
[BaseIndexPredicate("organization", "defined", BaseIndexPredicateNodeKind.IsDefined, Field = nameof(OrganizationId))]
[BaseIndexPredicate("organization", "equal", BaseIndexPredicateNodeKind.Equal, Field = nameof(OrganizationId), Literal = "\"acme\"")]
[BaseIndexPredicate("organization", "not-null", BaseIndexPredicateNodeKind.IsNotNull, Field = nameof(OrganizationId))]
[BaseIndexPredicate("organization", "root", BaseIndexPredicateNodeKind.And, Children = new[] { "defined", "equal", "not-null" })]
internal sealed partial record GeneratedProject
{
    [BaseField("organization-id")]
    public required string OrganizationId { get; init; }

    [BaseField("name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256,
        StringNormalization = BaseStringNormalizationRequirement.RequireNfc)]
    public required string Name { get; init; }

    [BaseField("optional-note")]
    public string? OptionalNote { get; init; }
}

[BaseCollection("ignore-contract", typeof(GeneratedApplicationJsonContext))]
internal sealed partial record GeneratedIgnoreContract
{
    [BaseField("ignore.active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyOrder(0)]
    public required string Active { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public string LocalOnly { get; init; } = string.Empty;
}

internal enum GeneratedState
{
    Active,
    Disabled,
}

internal enum GeneratedWireState
{
    [JsonStringEnumMemberName("active-wire")]
    Active,
    [JsonStringEnumMemberName("disabled-wire")]
    Disabled,
}

[BaseCollection("generated-wire-enums", typeof(GeneratedApplicationJsonContext))]
internal sealed partial record GeneratedWireEnumRecord
{
    [BaseField("state", AllowedEnumLiterals = ["active-wire", "disabled-wire"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<GeneratedWireState>))]
    public required GeneratedWireState State { get; init; }
}

[BaseCollection("generated-optional-non-null", typeof(GeneratedOptionalNonNullableJsonContext))]
internal sealed partial record GeneratedOptionalNonNullableRecord
{
    [BaseField("label", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)]
    public string? Label { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GeneratedOptionalNonNullableRecord))]
internal sealed partial class GeneratedOptionalNonNullableJsonContext : JsonSerializerContext;

[BaseCollection("generated-enums", typeof(GeneratedApplicationJsonContext))]
internal sealed partial record GeneratedEnumRecord
{
    [BaseField("state", AllowedEnumLiterals = new[] { "Active", "Disabled" })]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<GeneratedState>))]
    public required GeneratedState State { get; init; }
}

internal sealed record ManualScalarRecord
{
    [JsonPropertyName("uint32")]
    public uint UInt32 { get; init; }
    [JsonPropertyName("uint64")]
    public ulong UInt64 { get; init; }
    public Guid Guid { get; init; }
    public required BaseBinary Binary { get; init; }
    public required BaseCanonicalJson Json { get; init; }
    public required string[] Items { get; init; }
}

[JsonSerializable(typeof(GeneratedProject))]
[JsonSerializable(typeof(GeneratedIgnoreContract))]
[JsonSerializable(typeof(GeneratedEnumRecord))]
[JsonSerializable(typeof(GeneratedWireEnumRecord))]
[JsonSerializable(typeof(ManualScalarRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GeneratedApplicationJsonContext : JsonSerializerContext;
