namespace HPD.ML.Abstractions;

public enum RefinementLevel
{
    /// <summary>Column names and approximate types; dimensions may be symbolic.</summary>
    Approximate,

    /// <summary>All types fully resolved, dimensions concrete.</summary>
    Exact,

    /// <summary>Exact, plus opaque external annotations (ONNX shapes, etc.).</summary>
    OpaqueAnnotated
}
