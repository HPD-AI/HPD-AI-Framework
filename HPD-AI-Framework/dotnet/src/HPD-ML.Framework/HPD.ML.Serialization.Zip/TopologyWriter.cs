namespace HPD.ML.Serialization.Zip;

using System.Text.Json;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Serializes/deserializes transform pipeline topology.
/// Each transform is stored as a type name + JSON config.
/// </summary>
internal static class TopologyWriter
{
    public static void Write(
        ITransform transform,
        Stream destination,
        List<TransformEntry> entries,
        JsonSerializerOptions options)
    {
        CollectTransforms(transform, entries, options);
        JsonSerializer.Serialize(destination, entries, options);
    }

    public static ITransform Read(
        Stream source,
        List<TransformEntry> entries,
        JsonSerializerOptions options)
    {
        // Topology loading requires a transform registry for full reconstruction.
        // For now, return identity — the caller reconstructs the pipeline
        // using the loaded parameters + original learner.
        return new IdentityTransform();
    }

    private static void CollectTransforms(
        ITransform transform,
        List<TransformEntry> entries,
        JsonSerializerOptions options)
    {
        if (transform is ComposedTransform composed)
        {
            foreach (var child in composed.Transforms)
                CollectTransforms(child, entries, options);
        }
        else
        {
            entries.Add(new TransformEntry
            {
                TypeName = transform.GetType().Name,
                ConfigJson = SerializeTransformConfig(transform, options)
            });
        }
    }

    private static string? SerializeTransformConfig(ITransform transform, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Serialize(transform, transform.GetType(), options);
        }
        catch
        {
            return null;
        }
    }
}
