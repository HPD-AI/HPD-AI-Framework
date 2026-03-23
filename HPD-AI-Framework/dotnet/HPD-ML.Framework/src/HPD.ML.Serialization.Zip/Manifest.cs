namespace HPD.ML.Serialization.Zip;

using HPD.ML.Abstractions;

/// <summary>
/// Manifest stored at the root of the ZIP archive.
/// </summary>
public sealed class Manifest
{
    public string FormatId { get; init; } = "hpd-ml-zip-v1";
    public int SchemaVersion { get; init; } = 1;
    public SaveContent Content { get; init; }
    public DateTime SavedAtUtc { get; init; }

    /// <summary>Type discriminator for the learned parameters.</summary>
    public string? ParameterType { get; init; }

    /// <summary>Transform type discriminators in pipeline order.</summary>
    public List<TransformEntry>? Pipeline { get; init; }

    /// <summary>Whether inference state is included.</summary>
    public bool HasInferenceState { get; init; }
}

public sealed class TransformEntry
{
    public string TypeName { get; init; } = "";
    public string? ConfigJson { get; init; }
}
