using System.Buffers;

namespace HPD.ML.Abstractions;

/// <summary>Convenience overloads for Stream-based callers.</summary>
public static class SerializerExtensions
{
    public static void Save(this ISerializer serializer, IModel model, SaveContent content,
        ISerializationFormat format, Stream destination, object? inferenceState = null)
    {
        var writer = new StreamBufferWriter(destination);
        serializer.Save(new SaveRequest(model, content, format, writer, inferenceState));
    }

    public static IModel Load(this ISerializer serializer, ISerializationFormat format, Stream source)
    {
        var sequence = BufferHelpers.ReadToSequence(source);
        return serializer.Load(new LoadRequest(format, sequence));
    }
}
