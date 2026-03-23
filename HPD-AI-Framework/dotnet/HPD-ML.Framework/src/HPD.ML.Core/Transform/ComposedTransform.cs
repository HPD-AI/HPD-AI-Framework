namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Sequential composition of transforms. Produced by TransformComposer.Compose().
/// Schema propagation runs eagerly; data transformation runs lazily.
/// </summary>
public sealed class ComposedTransform : ITransform
{
    private readonly ITransform[] _transforms;

    /// <summary>The child transforms in pipeline order.</summary>
    public IReadOnlyList<ITransform> Transforms => _transforms;

    public ComposedTransform(ITransform[] transforms)
    {
        _transforms = transforms;
        Properties = new TransformProperties
        {
            IsStateful = transforms.Any(t => t.Properties.IsStateful),
            RequiresOrdering = transforms.Any(t => t.Properties.RequiresOrdering),
            PreservesRowCount = transforms.All(t => t.Properties.PreservesRowCount),
            DevicePreference = transforms
                .Select(t => t.Properties.DevicePreference)
                .LastOrDefault(d => d is not null),
        };
    }

    public TransformProperties Properties { get; }

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var schema = inputSchema;
        foreach (var t in _transforms)
            schema = t.GetOutputSchema(schema);
        return schema;
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var data = input;
        foreach (var t in _transforms)
            data = t.Apply(data);
        return data;
    }
}
