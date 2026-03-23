using System.Text;
using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Formats retrieved search results into a context string for injection into an LLM prompt.
/// Supports Markdown, Plain, and Xml output formats.
/// Pure formatting — no external calls.
/// Default retry: 2 attempts, Constant, 200ms.
/// Default propagation: StopPipeline.
/// </summary>
[GraphNodeHandler(NodeName = "FormatContext")]
public sealed partial class FormatContextHandler : IGraphNodeHandler<MragPipelineContext>
{
    public static MragRetryPolicy DefaultRetryPolicy { get; } = new()
    {
        MaxAttempts = 2,
        InitialDelay = TimeSpan.FromMilliseconds(200),
        Strategy = MragBackoffStrategy.Constant
    };

    public static MragErrorPropagation DefaultErrorPropagation { get; } = MragErrorPropagation.StopPipeline;

    public sealed class Config
    {
        public MragFormat Format { get; set; } = MragFormat.Markdown;
    }

    public Task<FormatContextOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The search results to format into a context block.")] MragSearchResultDto[] Results,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        var formatted = Format(Results, config.Format);
        return Task.FromResult(new FormatContextOutput { Context = formatted });
    }

    private static string Format(MragSearchResultDto[] results, MragFormat format)
    {
        if (results.Length == 0)
            return string.Empty;

        return format switch
        {
            MragFormat.Markdown => FormatMarkdown(results),
            MragFormat.Plain    => FormatPlain(results),
            MragFormat.Xml      => FormatXml(results),
            _                   => FormatMarkdown(results)
        };
    }

    private static string FormatMarkdown(MragSearchResultDto[] results)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < results.Length; i++)
        {
            var r = results[i];
            if (i > 0) sb.AppendLine();
            sb.AppendLine($"### Source {i + 1} (id: {r.DocumentId}, score: {r.Score:F4})");
            if (!string.IsNullOrEmpty(r.Context))
                sb.AppendLine($"> {r.Context}");
            sb.AppendLine();
            sb.Append(r.Content);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatPlain(MragSearchResultDto[] results)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < results.Length; i++)
        {
            var r = results[i];
            if (i > 0) sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {r.DocumentId} (score: {r.Score:F4})");
            if (!string.IsNullOrEmpty(r.Context))
                sb.AppendLine($"Context: {r.Context}");
            sb.Append(r.Content);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatXml(MragSearchResultDto[] results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<context>");
        for (var i = 0; i < results.Length; i++)
        {
            var r = results[i];
            sb.AppendLine($"  <source index=\"{i + 1}\" documentId=\"{EscapeXml(r.DocumentId)}\" score=\"{r.Score:F4}\">");
            if (!string.IsNullOrEmpty(r.Context))
                sb.AppendLine($"    <header>{EscapeXml(r.Context)}</header>");
            sb.AppendLine($"    <content>{EscapeXml(r.Content)}</content>");
            sb.AppendLine("  </source>");
        }
        sb.Append("</context>");
        return sb.ToString();
    }

    private static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    public sealed class FormatContextOutput
    {
        [OutputSocket(Description = "Formatted context string ready for LLM prompt injection.")]
        public required string Context { get; init; }
    }
}
