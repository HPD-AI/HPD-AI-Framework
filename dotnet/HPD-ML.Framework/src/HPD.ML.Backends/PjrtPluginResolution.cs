namespace HPD.ML.Backends.Pjrt;

public sealed record PjrtPluginResolverOptions
{
    public string? ExplicitPath { get; init; }
    public string? SearchRoot { get; init; }
    public string Backend { get; init; } = "cpu";
    public PjrtClientCreateOptions? ClientOptions { get; init; }
}

public sealed record PjrtPluginResolution
{
    public required bool IsAvailable { get; init; }
    public string? LibraryPath { get; init; }
    public string? Source { get; init; }
    public string? ReasonUnavailable { get; init; }
}
