namespace HPD.ML.Abstractions;

/// <summary>A pure function DataHandle -> DataHandle with a schema signature.</summary>
public interface ITransform
{
    /// <summary>Compute output schema without touching data.</summary>
    ISchema GetOutputSchema(ISchema inputSchema);

    /// <summary>Apply this transform, producing a new (typically lazy) DataHandle.</summary>
    IDataHandle Apply(IDataHandle input);

    TransformProperties Properties { get; }
}
