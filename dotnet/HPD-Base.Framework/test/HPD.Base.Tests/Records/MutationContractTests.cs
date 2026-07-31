using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Records;

public sealed class MutationContractTests
{
    [Fact]
    public void PatchRequestCarriesFieldMapMergePayload()
    {
        using var document = JsonDocument.Parse("""{"title":"updated"}""");
        var patch = new RecordPatchRequest
        {
            Patch = new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = new Dictionary<string, JsonElement>
                {
                    ["title"] = document.RootElement.GetProperty("title").Clone()
                }
            }
        };

        Assert.Equal(RecordPayloadKind.FieldMap, patch.Patch.Kind);
        Assert.True(patch.Patch.Fields!.ContainsKey("title"));
    }

    [Fact]
    public void ReplaceRequestCarriesFullPayload()
    {
        using var document = JsonDocument.Parse("""{"title":"full"}""");
        var replace = new RecordReplaceRequest
        {
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = document.RootElement.Clone()
            }
        };

        Assert.Equal(RecordPayloadKind.Json, replace.Payload.Kind);
        Assert.Equal("full", replace.Payload.Json.GetProperty("title").GetString());
    }
}
