using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

namespace HPD.TUI.Markdown;

/// <summary>Parses canonical Markdown source into immutable HPD snapshot metadata.</summary>
public interface IMarkdownDocumentParser
{
    /// <summary>Parses <paramref name="source"/> using the specified immutable options.</summary>
    MarkdownDocumentSnapshot Parse(string source, MarkdownParseOptions options);
}

/// <summary>Specifies the HPD-owned pipeline used for one parse.</summary>
public sealed record MarkdownParseOptions
{
    /// <summary>Gets the pipeline descriptor.</summary>
    public required MarkdownPipelineDescriptor Pipeline { get; init; }

    /// <summary>Gets whether streaming boundary metadata is analyzed.</summary>
    public bool AnalyzeStreamingBoundaries { get; init; } = true;
}

/// <summary>Describes immutable parser and renderer-affecting configuration.</summary>
public sealed record MarkdownPipelineConfiguration(
    int MaximumNestingDepth = 128,
    bool PreciseSourceLocation = true,
    bool TrackTrivia = false,
    IReadOnlyList<MarkdownExtensionConfiguration>? Extensions = null,
    long SemanticAnalysisVersion = 1,
    long RendererPolicyVersion = 1);

/// <summary>Describes one enabled Markdown extension and its normalized options.</summary>
public sealed record MarkdownExtensionConfiguration(
    string Id,
    IReadOnlyDictionary<string, string> NormalizedOptions,
    MarkdownExtensionInvalidation? Invalidation = null);

/// <summary>Declares whether an extension can change semantics outside its local block.</summary>
public enum MarkdownExtensionInvalidation { BlockLocal, DocumentGlobal }

/// <summary>Identifies an HPD-created Markdig pipeline.</summary>
public sealed class MarkdownPipelineDescriptor
{
    internal MarkdownPipelineDescriptor(string stableId, MarkdownPipelineConfiguration configuration, Markdig.MarkdownPipeline pipeline)
    {
        StableId = stableId;
        Configuration = configuration;
        Pipeline = pipeline;
    }

    /// <summary>Gets the stable structural pipeline identity.</summary>
    public string StableId { get; }

    /// <summary>Gets the normalized immutable configuration.</summary>
    public MarkdownPipelineConfiguration Configuration { get; }

    internal Markdig.MarkdownPipeline Pipeline { get; }
}

/// <summary>Creates the supported immutable Markdown pipeline descriptors.</summary>
public static class MarkdownPipelineFactory
{
    /// <summary>Creates the default HPD terminal Markdown pipeline.</summary>
    public static MarkdownPipelineDescriptor CreateDefault() => Create(new MarkdownPipelineConfiguration());

    /// <summary>Creates a descriptor after canonicalizing all caller-owned collections.</summary>
    public static MarkdownPipelineDescriptor Create(MarkdownPipelineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.MaximumNestingDepth);
        if (configuration.MaximumNestingDepth != 128)
            throw new NotSupportedException("The referenced Markdig build exposes its fixed nesting limit; HPD currently supports the audited value 128.");

        var extensions = (configuration.Extensions ?? DefaultExtensions())
            .Select(static extension => new MarkdownExtensionConfiguration(
                extension.Id,
                new ReadOnlyDictionary<string, string>(extension.NormalizedOptions
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
                extension.Invalidation ?? (IsBuiltIn(extension.Id)
                    ? MarkdownExtensionInvalidation.BlockLocal
                    : MarkdownExtensionInvalidation.DocumentGlobal)))
            .OrderBy(static extension => extension.Id, StringComparer.Ordinal)
            .ToArray();
        var normalized = configuration with { Extensions = Array.AsReadOnly(extensions) };

        var builder = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UseTaskLists()
            .UsePipeTables();
        builder.PreciseSourceLocation = normalized.PreciseSourceLocation;
        builder.TrackTrivia = normalized.TrackTrivia;
        var pipeline = builder.Build();
        var identity = string.Join('|', normalized.MaximumNestingDepth, normalized.PreciseSourceLocation,
            normalized.TrackTrivia, normalized.SemanticAnalysisVersion, normalized.RendererPolicyVersion,
            string.Join(';', extensions.Select(static e => e.Id + ':' + e.Invalidation + ':' + string.Join(',', e.NormalizedOptions.Select(static p => p.Key + '=' + p.Value)))));
        var stableId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new MarkdownPipelineDescriptor(stableId, normalized, pipeline);
    }

    private static MarkdownExtensionConfiguration[] DefaultExtensions() =>
    [
        new("autolinks", ReadOnlyDictionary<string, string>.Empty),
        new("emphasis-extras", ReadOnlyDictionary<string, string>.Empty),
        new("pipe-tables", ReadOnlyDictionary<string, string>.Empty),
        new("task-lists", ReadOnlyDictionary<string, string>.Empty)
    ];

    private static bool IsBuiltIn(string id) => id is "autolinks" or "emphasis-extras" or "pipe-tables" or "task-lists";
}

/// <summary>Default Markdig-backed document parser.</summary>
public sealed class MarkdownDocumentParser : IMarkdownDocumentParser
{
    /// <inheritdoc />
    public MarkdownDocumentSnapshot Parse(string source, MarkdownParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var syntax = Markdig.Markdown.Parse(source, options.Pipeline.Pipeline);
        var blocks = syntax.Select((block, ordinal) => MarkdownTopLevelBlock.From(block, ordinal, source.Length)).ToArray();
        var features = MarkdownSemanticAnalysis.GetFeatures(syntax);
        if (options.Pipeline.Configuration.Extensions?.Any(static extension =>
                extension.Invalidation == MarkdownExtensionInvalidation.DocumentGlobal) == true)
            features |= MarkdownDocumentFeatures.ExtensionGlobalState;
        return new MarkdownDocumentSnapshot(source, blocks, features, options.Pipeline, syntax);
    }
}
