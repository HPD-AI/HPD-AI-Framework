using System.Text;
using System.Text.Json;
using HPD.Graph.Abstractions.Checkpointing;
using HPD.Graph.Core.Storage;

namespace HPD.Graph.Core.Checkpointing;

/// <summary>Encodes and decodes the closed durable Graph checkpoint format.</summary>
public static class GraphCheckpointCodec
{
    /// <summary>Serializes one checkpoint to its canonical UTF-8 JSON representation.</summary>
    public static string Serialize(GraphCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            JsonCheckpointStore.ToJsonCheckpoint(checkpoint),
            StorageJsonSerializerContext.Default.JsonGraphCheckpoint);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Deserializes one canonical checkpoint representation.</summary>
    public static GraphCheckpoint Deserialize(string canonicalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        JsonGraphCheckpoint value = JsonSerializer.Deserialize(
            canonicalJson,
            StorageJsonSerializerContext.Default.JsonGraphCheckpoint)
            ?? throw new JsonException("The graph checkpoint payload was empty.");
        return JsonCheckpointStore.FromJsonCheckpoint(value);
    }
}
