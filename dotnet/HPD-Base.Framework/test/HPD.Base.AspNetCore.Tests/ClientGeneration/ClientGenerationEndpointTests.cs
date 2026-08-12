using System.Text.Json;
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
                ItemId = "mutation", CollectionId = "documents", Kind = BaseRecordMutationKind.Patch, RecordId = new RecordId("d1"),
                Patch = new RecordPatchRequest { Patch = new RecordPayload { Kind = RecordPayloadKind.Json, Json = payload.RootElement.Clone() } }
            }]
        };
        string json = JsonSerializer.Serialize(request, HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest);
        json.Should().Be("{\"mode\":\"atomic\",\"operations\":[{\"itemId\":\"mutation\",\"collectionId\":\"documents\",\"kind\":\"patch\",\"recordId\":\"d1\",\"patch\":{\"patch\":{\"kind\":\"json\",\"json\":{\"stored_title\":\"new\"}}}}]}");
    }

    [Fact]
    public async Task ApplicationSnapshotUsesInstalledApplicationAndMaterializedContracts()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("application", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        BaseCollection<JsonElement> items = BaseCollection<JsonElement>.Create(TestBaseApp.Collection(), HPDBaseJsonSerializerContext.Default.JsonElement, static _ => { });
        builder.Services.AddHPDBase(hpd => hpd
            .ConfigureSchema(options => options.ApplicationId = "client-generation-tests")
            .AddAspNetCore()
            .AddCollection(items));

        await using WebApplication app = builder.Build();
        app.UseAuthorization();
        app.MapHPDBaseApplicationApi(new HPDBaseApplicationEndpointOptions
        {
            AuthorizationPolicy = "application",
            MapRecords = true,
            MapRegisteredReads = false,
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
        document.RootElement.GetProperty("digest").GetString().Should().MatchRegex("^sha256:[0-9a-f]{64}$");
    }
}
