namespace HPD.ML.Serialization.Zip;

using System.Text.Json;
using HPD.ML.Abstractions;

/// <summary>
/// Serializes transform properties to JSON for inspectability.
/// </summary>
internal static class SchemaWriter
{
    public static void Write(ITransform transform, Stream destination, JsonSerializerOptions options)
    {
        var schemaInfo = new SchemaInfo
        {
            IsStateful = transform.Properties.IsStateful,
            RequiresOrdering = transform.Properties.RequiresOrdering,
            PreservesRowCount = transform.Properties.PreservesRowCount
        };

        JsonSerializer.Serialize(destination, schemaInfo, options);
    }
}

internal sealed class SchemaInfo
{
    public bool IsStateful { get; init; }
    public bool RequiresOrdering { get; init; }
    public bool PreservesRowCount { get; init; }
}
