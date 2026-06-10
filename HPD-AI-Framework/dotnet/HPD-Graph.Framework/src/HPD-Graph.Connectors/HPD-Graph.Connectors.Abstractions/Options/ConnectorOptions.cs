using System.Text.Json;

namespace HPD.Graph.Connectors.Abstractions.Options;

public interface IConnectorOptionProvider
{
    string OptionProviderName { get; }

    Task<IReadOnlyList<ConnectorOption>> GetOptionsAsync(
        ConnectorOptionRequest request,
        CancellationToken ct = default);
}

public interface IConnectorOptionProvider<TRequest>
{
    ValueTask<ConnectorOptionPage> GetOptionsAsync(
        TRequest request,
        CancellationToken ct = default);
}

public sealed record ConnectorOptionRequest
{
    public string? ConnectionId { get; init; }
    public JsonElement? CurrentConfig { get; init; }
    public string? Search { get; init; }
    public string? Cursor { get; init; }
    public int? Limit { get; init; }
}

public sealed record ConnectorOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed record ConnectorOptionPage
{
    public IReadOnlyList<ConnectorOption> Options { get; init; } = [];
    public string? NextCursor { get; init; }
}
