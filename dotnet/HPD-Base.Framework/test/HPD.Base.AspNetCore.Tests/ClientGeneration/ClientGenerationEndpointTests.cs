using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;

namespace HPD.Base.AspNetCore.Tests.ClientGeneration;

public sealed class ClientGenerationEndpointTests
{
    [Fact]
    public void IdentifiedPatchBytesMatchTheLanguageNeutralFixture()
    {
        using JsonDocument payload = JsonDocument.Parse("{\"stored_title\":\"new\"}");
        var request = new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations = [new BaseRecordBatchItem
            {
                ItemId = "mutation", CollectionId = "documents", Kind = BaseRecordMutationKind.Patch, RecordId = RecordId.Create("d1"),
                Patch = new RecordPatchRequest { Patch = new RecordPayload { Kind = RecordPayloadKind.Json, Json = payload.RootElement.Clone() }, RemovedFieldIds = [] }
            }]
        };
        string json = JsonSerializer.Serialize(request, HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest);
        json.Should().Be("{\"mode\":\"atomic\",\"operations\":[{\"itemId\":\"mutation\",\"collectionId\":\"documents\",\"kind\":\"patch\",\"recordId\":\"d1\",\"patch\":{\"patch\":{\"kind\":\"json\",\"json\":{\"stored_title\":\"new\"}},\"removedFieldIds\":[]}}]}");
    }

    [Fact]
    public async Task ApplicationSnapshotUsesInstalledApplicationAndMaterializedContracts()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("application", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        CollectionDefinition definition = TestBaseApp.Collection() with
        {
            Fields =
            [
                new FieldDefinition { Id = "title", ApplicationName = "storedTitle", WireName = "stored_title", Type = BaseFieldTypes.String },
                new FieldDefinition { Id = "embedding", ApplicationName = "embedding", WireName = "embedding", Type = "vector" }
            ],
            VectorIndexes =
            [
                new VectorIndexDefinition { Id = "semantic", CollectionId = "items", VectorFieldId = "embedding", VectorSpaceId = "test", Dimensions = 3, Function = BaseVectorFunction.CosineSimilarity, FilterFieldIds = [] }
            ]
        };
        BaseCollection<JsonElement> items = BaseCollection<JsonElement>.Create(definition, HPDBaseJsonSerializerContext.Default.JsonElement, static _ => { });
        builder.Services.AddHPDBase(hpd => hpd
            .ConfigureSchema(options => options.ApplicationId = "client-generation-tests")
            .AddAspNetCore()
            .AddCollection(items)
            .AddCollection(ClientCompoundRecord.Collection)
            .AddRead(ClientCompoundRead.Definition)
            .AddRead(ClientBinaryRead.Definition));

        await using WebApplication app = builder.Build();
        app.UseAuthorization();
        app.MapHPDBaseApplicationApi(new HPDBaseApplicationEndpointOptions
        {
            AuthorizationPolicy = "application",
            MapRecords = true,
            MapRegisteredReads = true,
            MapClientGeneration = true
        });
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetTestClient().GetAsync("/base/client-generation");
        response.EnsureSuccessStatusCode();
        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(responseBytes).Should().StartWith("{\"application\":");
        using JsonDocument document = JsonDocument.Parse(responseBytes);
        document.RootElement.GetProperty("application").GetProperty("applicationId").GetString().Should().Be("client-generation-tests");
        JsonElement endpoint = document.RootElement.GetProperty("endpoints").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "base.records.batch");
        endpoint.GetProperty("requestTypeId").GetString().Should().Be(BaseDtoIds.BaseRecordBatchRequest);
        endpoint.GetProperty("responseTypeId").GetString().Should().Be(BaseDtoIds.BaseRecordBatchResult);
        endpoint.GetProperty("maximumRequestBodyBytes").GetInt64().Should().Be(1_048_576);
        endpoint.GetProperty("cache").GetString().Should().Be("none");
        JsonElement types = document.RootElement.GetProperty("schema").GetProperty("types");
        JsonElement vector = types.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "field.items.embedding").GetProperty("node");
        vector.GetProperty("minItems").GetInt32().Should().Be(3);
        vector.GetProperty("maxItems").GetInt32().Should().Be(3);
        JsonElement record = types.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "collection.items.record").GetProperty("node");
        JsonElement title = record.GetProperty("properties").EnumerateArray().Single(item => item.GetProperty("wireName").GetString() == "stored_title");
        title.GetProperty("name").GetString().Should().Be("storedTitle");
        JsonElement compound = document.RootElement.GetProperty("registeredReads").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "client-compound-read");
        compound.GetProperty("fixedCompleteResult").GetBoolean().Should().BeTrue();
        compound.GetProperty("fixedDiscriminators").EnumerateArray().Select(static value => value.GetString())
            .Should().Equal("disabled", "enabled");
        JsonElement discriminator = types.EnumerateArray().Single(item =>
            item.GetProperty("id").GetString()!.EndsWith(".client.compound.kind", StringComparison.Ordinal)).GetProperty("node");
        discriminator.GetProperty("kind").GetString().Should().Be("enum");
        discriminator.GetProperty("values").EnumerateArray().Select(static value => value.GetString())
            .Should().Equal("disabled", "enabled");
        JsonElement binary = types.EnumerateArray().Single(item =>
            item.GetProperty("id").GetString() == "read.client-binary-read.row.client.binary.payload").GetProperty("node");
        binary.GetProperty("kind").GetString().Should().Be("bytes");
        binary.GetProperty("wire").GetString().Should().Be("base64");
        binary.GetProperty("minBytes").GetInt32().Should().Be(4);
        binary.GetProperty("maxBytes").GetInt32().Should().Be(16);
        document.RootElement.GetProperty("digest").GetString().Should().MatchRegex("^sha256:[0-9a-f]{64}$");
    }
}

[BaseCollection("client-compound-records", typeof(ClientCompoundJsonContext))]
internal sealed partial record ClientCompoundRecord
{
    [BaseField("client.compound.enabled")] public required bool Enabled { get; init; }
    [BaseField("client.compound.payload", MinimumBytes = 4, MaximumBytes = 16)] public required BaseBinary Payload { get; init; }
}

[BaseRead("client-compound-read", typeof(ClientCompoundJsonContext), Exposure = BaseReadExposure.Public, RequiredGrantId = "client.compound.read")]
internal sealed partial record ClientCompoundRead
{
    [BaseReadParameter("client.compound.enabled")] public required bool Enabled { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("client.compound.kind")] public required string Kind { get; init; }
        [BaseReadField("client.compound.count")] public required long Count { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ClientCompoundRead, Row> read) => read
        .CountBranch("enabled-branch", Row.Fields.Kind, "enabled", ClientCompoundRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(ClientCompoundRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CountBranch("disabled-branch", Row.Fields.Kind, "disabled", ClientCompoundRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(ClientCompoundRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CompoundLimits(4_096, 16, 2_000, 2, 8);
}

[BaseRead("client-binary-read", typeof(ClientCompoundJsonContext), Exposure = BaseReadExposure.Public, RequiredGrantId = "client.compound.read")]
internal sealed partial record ClientBinaryRead
{
    [BaseReadParameter("client.binary.payload", MinimumBytes = 4, MaximumBytes = 16)]
    public required BaseBinary Payload { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("client.binary.payload", MinimumBytes = 4, MaximumBytes = 16)]
        public required BaseBinary Payload { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ClientBinaryRead, Row> read) => read
        .From(ClientCompoundRecord.Collection, "record", out BaseReadSource<ClientCompoundRecord> record)
        .Where(record.Field(ClientCompoundRecord.Fields.Payload).Equal(read.Parameter(Parameters.Payload)))
        .Project(Row.Fields.Payload, record.Field(ClientCompoundRecord.Fields.Payload));
}

[JsonSerializable(typeof(ClientCompoundRecord))]
[JsonSerializable(typeof(ClientCompoundRead))]
[JsonSerializable(typeof(ClientCompoundRead.Row), TypeInfoPropertyName = "ClientCompoundReadRow")]
[JsonSerializable(typeof(ClientBinaryRead))]
[JsonSerializable(typeof(ClientBinaryRead.Row), TypeInfoPropertyName = "ClientBinaryReadRow")]
internal sealed partial class ClientCompoundJsonContext : JsonSerializerContext;
