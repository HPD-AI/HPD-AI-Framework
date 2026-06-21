namespace HPD.ML.Serialization.Zip;

using HPD.ML.Abstractions;

/// <summary>
/// Maps ILearnedParameters types to their serialization writers.
/// Domain packages may register their own writers; HPD.ML-owned parameter
/// types are registered by the serializer.
/// </summary>
public sealed class ParameterWriterRegistry
{
    private readonly Dictionary<string, IParameterWriter> _writers = new();

    public void Register<TParams>(IParameterWriter<TParams> writer)
        where TParams : ILearnedParameters
    {
        _writers[writer.TypeName] = writer;
    }

    public IParameterWriter? GetWriter(ILearnedParameters parameters)
    {
        var typeName = parameters.GetType().Name;
        return _writers.GetValueOrDefault(typeName);
    }

    public IParameterWriter? GetWriterByTypeName(string typeName)
        => _writers.GetValueOrDefault(typeName);
}
