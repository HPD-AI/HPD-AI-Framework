namespace HPD.ML.Backends.Mlx;

public sealed record MlxRuntimeResolution
{
    public required bool IsAvailable { get; init; }
    public string? LibraryPath { get; init; }
    public string? Source { get; init; }
    public string? ReasonUnavailable { get; init; }
    public IReadOnlyList<string> SearchedPaths { get; init; } = Array.Empty<string>();
}

