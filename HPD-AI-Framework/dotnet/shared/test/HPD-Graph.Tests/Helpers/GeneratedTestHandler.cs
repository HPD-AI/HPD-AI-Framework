using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Test handler using source generator attributes.
/// The SocketBridgeGenerator should generate the IGraphNodeHandler implementation.
/// NOTE: Must be partial class and declare IGraphNodeHandler<TContext>.
/// The generator will provide the explicit interface implementation.
/// </summary>
[GraphNodeHandler(NodeName = "generated_test")]
public partial class GeneratedTestHandler : IGraphNodeHandler<GraphContext>
{
    /// <summary>
    /// Clean method that accepts inputs via attributes.
    /// Source generator should extract these from HandlerInputs automatically.
    /// The 'context' parameter (not annotated) tells the generator what TContext to use.
    /// </summary>
    public async Task<GeneratedTestOutput> ExecuteAsync(
        GraphContext context,
        [InputSocket(Description = "Test input text")] string text,
        [InputSocket(Optional = true, Description = "Optional multiplier")] int? multiplier,
        CancellationToken ct)
    {
        await Task.Delay(10, ct);

        var count = multiplier ?? 1;
        var result = string.Concat(Enumerable.Repeat(text, count));

        return new GeneratedTestOutput
        {
            Result = result,
            Length = result.Length
        };
    }
}

/// <summary>
/// Output type for GeneratedTestHandler.
/// Properties marked with [OutputSocket] will be extracted automatically.
/// </summary>
public class GeneratedTestOutput
{
    [OutputSocket(Description = "Concatenated result")]
    public string Result { get; set; } = "";

    [OutputSocket(Description = "Result length")]
    public int Length { get; set; }
}

[GraphNodeHandler(NodeName = "clean_generated")]
public partial class CleanGeneratedHandler
{
    public Task<GeneratedTestOutput> ExecuteAsync(
        [InputSocket(Description = "Test input text")] string text,
        CancellationToken ct)
    {
        return Task.FromResult(new GeneratedTestOutput
        {
            Result = text.ToUpperInvariant(),
            Length = text.Length
        });
    }
}

[GraphNodeHandler(NodeName = "generated_config")]
public partial class GeneratedConfigHandler : IGraphNodeHandler<GraphContext>
{
    public sealed class Config
    {
        public string Prefix { get; set; } = "";
        public int Count { get; set; }
        public int? OptionalCount { get; set; }
        public Guid RequestId { get; set; }
        public DateTimeOffset Since { get; set; }
        public string[] Tags { get; set; } = [];
        public List<int> Scores { get; set; } = [];
        public GeneratedConfigMode Mode { get; set; }
        public GeneratedComplexConfig Complex { get; set; } = new();
    }

    public Task<GeneratedTestOutput> ExecuteAsync(
        GraphContext context,
        [InputSocket(Description = "Input value")] string text,
        CancellationToken ct)
    {
        var config = GetNodeConfig();
        var result = string.Join(
            ":",
            config.Prefix,
            text,
            config.Count,
            config.OptionalCount,
            config.RequestId,
            config.Since.ToString("O"),
            string.Join(",", config.Tags),
            config.Scores.Sum(),
            config.Mode,
            config.Complex.Name,
            config.Complex.Enabled);

        return Task.FromResult(new GeneratedTestOutput
        {
            Result = result,
            Length = result.Length
        });
    }
}

public enum GeneratedConfigMode
{
    Unknown,
    Fast,
    Careful
}

public sealed class GeneratedComplexConfig
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
}
