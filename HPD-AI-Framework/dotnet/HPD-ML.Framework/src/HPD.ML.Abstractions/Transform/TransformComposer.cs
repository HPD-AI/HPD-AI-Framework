namespace HPD.ML.Abstractions;

/// <summary>Compose transforms left-to-right.</summary>
public static class TransformComposer
{
    /// <summary>
    /// Variadic composition via params ReadOnlySpan (no array allocation).
    /// </summary>
    public static ITransform Compose(params ReadOnlySpan<ITransform> transforms)
    {
        if (transforms.Length == 0)
            throw new ArgumentException("At least one transform is required.", nameof(transforms));

        if (transforms.Length == 1)
            return transforms[0];

        // Copy to array since ReadOnlySpan can't be captured in closures
        var steps = transforms.ToArray();
        return new ComposedTransform(steps);
    }

    private sealed class ComposedTransform(ITransform[] steps) : ITransform
    {
        public ISchema GetOutputSchema(ISchema inputSchema)
        {
            var schema = inputSchema;
            foreach (var step in steps)
                schema = step.GetOutputSchema(schema);
            return schema;
        }

        public IDataHandle Apply(IDataHandle input)
        {
            var current = input;
            foreach (var step in steps)
                current = step.Apply(current);
            return current;
        }

        public TransformProperties Properties { get; } = new()
        {
            IsStateful = Array.Exists(steps, s => s.Properties.IsStateful),
            RequiresOrdering = Array.Exists(steps, s => s.Properties.RequiresOrdering),
            PreservesRowCount = Array.TrueForAll(steps, s => s.Properties.PreservesRowCount),
        };
    }
}
