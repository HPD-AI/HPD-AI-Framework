using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Tests.Application.Serialization;

public sealed partial class SerializerContractTests
{
    [Theory]
    [InlineData("\"AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA\"")]
    [InlineData("\"{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}\"")]
    [InlineData("\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [InlineData("\" aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"")]
    [InlineData("1")]
    public void Canonical_guid_converter_rejects_every_noncanonical_wire_form(string json)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new BaseCanonicalGuidJsonConverter());

        Action deserialize = () => JsonSerializer.Deserialize<Guid>(json, options);

        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void Canonical_guid_converters_round_trip_value_and_nullable_null()
    {
        Guid value = Guid.ParseExact("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "D");
        var options = new JsonSerializerOptions();
        options.Converters.Add(new BaseCanonicalGuidJsonConverter());
        options.Converters.Add(new BaseCanonicalNullableGuidJsonConverter());

        JsonSerializer.Serialize(value, options).Should().Be("\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"");
        JsonSerializer.Deserialize<Guid>("\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"", options).Should().Be(value);
        JsonSerializer.Deserialize<Guid?>("null", options).Should().BeNull();
        JsonSerializer.Serialize<Guid?>(null, options).Should().Be("null");
    }

    [Fact]
    public void ManualMetadataMustUseTheLockedOptionReceipt()
    {
        Action create = () => Collection(
            "serializer.default",
            DefaultContext.Default.RootA,
            "root-a",
            "Details",
            "Details");

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("base.schema.serializer.optionsMismatch");
    }

    [Fact]
    public void StableFieldIdentityIsBoundToTheExactRootProperty()
    {
        var firstMetadata = new CamelContext(BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase)).TwoFields;
        var secondMetadata = new CamelContext(BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase)).TwoFields;
        BaseCollection<TwoFields> first = TwoFieldCollection(firstMetadata, "left-id", "right-id");
        BaseCollection<TwoFields> swapped = TwoFieldCollection(secondMetadata, "right-id", "left-id");

        first.Definition.SerializerContractChecksum.Should().NotBe(swapped.Definition.SerializerContractChecksum);
    }

    [Fact]
    public void NestedJsonDomIsRejectedBeforeProviderInstallation()
    {
        var metadata = new DomContext(BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase)).DomRoot;
        Action create = () => BaseCollection<DomRoot>.Create(
            Definition("serializer.dom", "dom", "Dom", "dom"), metadata,
            fields => fields.Add<JsonElement>("dom", "Dom", "dom"));

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("base.schema.serializer.metadataInvalid");
    }

    [Fact]
    public void SharedDtoContractsFromDifferentContextsMustBeByteIdentical()
    {
        var contextA = new CamelContext(BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase));
        var contextB = new CamelContext(BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.SnakeCaseLower));
        BaseCollection<RootA> first = Collection("serializer.a", contextA.RootA, "details-a", "Details", "details");
        BaseCollection<RootB> second = Collection("serializer.b", contextB.RootB, "details-b", "Details", "details");
        var services = new ServiceCollection();

        Action install = () => services.AddHPDBase(builder => builder.AddCollection(first).AddCollection(second));

        install.Should().Throw<InvalidOperationException>()
            .WithMessage("base.schema.serializer.contextContractAmbiguous");
    }

    [Fact]
    public void SerializerMetadataIsNotPartOfThePublicCollectionSurface()
    {
        typeof(BaseCollection<>).GetProperty("JsonTypeInfo", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void GeneratedInfrastructureNeverReturnsOrCachesAContextPublicly()
    {
        typeof(BaseSerializerGeneratedContract).GetMethod("GetContext", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull();
        typeof(BaseSerializerContextRegistration).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should().BeEmpty();

    }

    [Fact]
    public void ApplicationOwnerMaterializesOneOperationalContextPerTypeAndPerGraph()
    {
        Action beforeFinalization = () => _ = OwnerRecordA.Collection.JsonTypeInfo;
        beforeFinalization.Should().Throw<InvalidOperationException>()
            .WithMessage("base.schema.serializer.ownerRequired");

        var firstServices = new ServiceCollection();
        firstServices.AddHPDBase(builder => builder.AddCollection(OwnerRecordA.Collection).AddCollection(OwnerRecordB.Collection));
        using ServiceProvider firstProvider = firstServices.BuildServiceProvider();
        BaseSerializerMetadataOwner firstOwner = firstProvider.GetRequiredService<BaseSerializerMetadataOwner>();
        firstOwner.ContextCount.Should().Be(1);
        var firstA = firstOwner.Resolve(OwnerRecordA.Collection);
        var firstB = firstOwner.Resolve(OwnerRecordB.Collection);
        firstA.Options.Should().BeSameAs(firstB.Options);

        var secondServices = new ServiceCollection();
        secondServices.AddHPDBase(builder => builder.AddCollection(OwnerRecordA.Collection).AddCollection(OwnerRecordB.Collection));
        using ServiceProvider secondProvider = secondServices.BuildServiceProvider();
        BaseSerializerMetadataOwner secondOwner = secondProvider.GetRequiredService<BaseSerializerMetadataOwner>();
        var secondA = secondOwner.Resolve(OwnerRecordA.Collection);
        secondOwner.ContextCount.Should().Be(1);
        secondA.Should().NotBeSameAs(firstA);
        firstOwner.Resolve(OwnerRecordA.Collection).Should().BeSameAs(firstA);
    }

    private static BaseCollection<TwoFields> TwoFieldCollection(
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TwoFields> metadata,
        string leftId,
        string rightId) => BaseCollection<TwoFields>.Create(
        new CollectionDefinition
        {
            Id = "serializer.two." + leftId,
            Name = "two",
            Kind = "record",
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
            Fields =
            [
                new FieldDefinition { Id = leftId, ApplicationName = "Left", WireName = "left", Type = "string" },
                new FieldDefinition { Id = rightId, ApplicationName = "Right", WireName = "right", Type = "string" },
            ],
        }, metadata, fields =>
        {
            fields.Add<string>(leftId, "Left", "left");
            fields.Add<string>(rightId, "Right", "right");
        });

    private static BaseCollection<T> Collection<T>(
        string id,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> metadata,
        string fieldId,
        string applicationName,
        string wireName) => BaseCollection<T>.Create(
        Definition(id, fieldId, applicationName, wireName), metadata,
        fields => fields.Add<SharedDetails>(fieldId, applicationName, wireName));

    private static CollectionDefinition Definition(
        string id,
        string fieldId,
        string applicationName,
        string wireName) => new()
        {
            Id = id,
            Name = id,
            Kind = "record",
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
            Fields = [new FieldDefinition { Id = fieldId, ApplicationName = applicationName, WireName = wireName, Type = "object" }],
        };

    internal sealed record SharedDetails(string URLValue);
    internal sealed record RootA(SharedDetails Details);
    internal sealed record RootB(SharedDetails Details);
    internal sealed record TwoFields(string Left, string Right);
    internal sealed record DomRoot(JsonElement Dom);

    [JsonSerializable(typeof(RootA))]
    [JsonSerializable(typeof(RootB))]
    [JsonSerializable(typeof(SharedDetails))]
    [JsonSerializable(typeof(TwoFields))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal sealed partial class CamelContext : JsonSerializerContext;

    [JsonSerializable(typeof(RootA))]
    internal sealed partial class DefaultContext : JsonSerializerContext;

    [JsonSerializable(typeof(DomRoot))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal sealed partial class DomContext : JsonSerializerContext;
}

[BaseCollection("serializer-owner-a", typeof(OwnerJsonContext))]
internal sealed partial record OwnerRecordA
{
    [BaseField("owner-a.value")] public required string Value { get; init; }
}

[BaseCollection("serializer-owner-b", typeof(OwnerJsonContext))]
internal sealed partial record OwnerRecordB
{
    [BaseField("owner-b.value")] public required string Value { get; init; }
}

[JsonSerializable(typeof(OwnerRecordA))]
[JsonSerializable(typeof(OwnerRecordB))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class OwnerJsonContext : JsonSerializerContext;
