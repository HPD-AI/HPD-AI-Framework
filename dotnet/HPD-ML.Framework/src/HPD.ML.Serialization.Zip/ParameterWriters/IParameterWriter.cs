namespace HPD.ML.Serialization.Zip;

using System.Text.Json;
using HPD.ML.Abstractions;

/// <summary>
/// Type-specific serializer for learned parameters.
/// Each parameter type gets its own writer that knows how to
/// efficiently serialize its data as binary + JSON metadata.
/// </summary>
public interface IParameterWriter
{
    string TypeName { get; }
    void WriteWeights(ILearnedParameters parameters, Stream destination);
    void WriteMetadata(ILearnedParameters parameters, Stream destination, JsonSerializerOptions options);
    ILearnedParameters ReadModel(Stream weights, Stream metadata, JsonSerializerOptions options);
}

/// <summary>Strongly-typed version for registration convenience.</summary>
public interface IParameterWriter<TParams> : IParameterWriter
    where TParams : ILearnedParameters
{
    void WriteWeights(TParams parameters, Stream destination);
    void WriteMetadata(TParams parameters, Stream destination, JsonSerializerOptions options);
    new TParams ReadModel(Stream weights, Stream metadata, JsonSerializerOptions options);

    void IParameterWriter.WriteWeights(ILearnedParameters p, Stream dest)
        => WriteWeights((TParams)p, dest);
    void IParameterWriter.WriteMetadata(ILearnedParameters p, Stream dest, JsonSerializerOptions opts)
        => WriteMetadata((TParams)p, dest, opts);
    ILearnedParameters IParameterWriter.ReadModel(Stream w, Stream m, JsonSerializerOptions opts)
        => ReadModel(w, m, opts);
}
