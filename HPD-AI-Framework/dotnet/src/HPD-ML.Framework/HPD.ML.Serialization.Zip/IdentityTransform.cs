namespace HPD.ML.Serialization.Zip;

using HPD.ML.Abstractions;

/// <summary>
/// Passthrough transform used when topology is not saved/loaded.
/// </summary>
internal sealed class IdentityTransform : ITransform
{
    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;

    public IDataHandle Apply(IDataHandle input) => input;
}
