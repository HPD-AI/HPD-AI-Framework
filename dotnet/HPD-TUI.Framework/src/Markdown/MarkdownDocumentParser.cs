using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
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

    /// <summary>Gets the largest source admitted to one rich semantic parse.</summary>
    public int MaximumSourceLength { get; init; } = 1_048_576;

    /// <summary>Gets the maximum aggregate count of delimiter-like characters admitted to one parse.</summary>
    public int MaximumDelimiterCharacters { get; init; } = 262_144;
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

/// <summary>Bridges one allowlisted Markdown extension into parsing and terminal rendering.</summary>
public interface ITerminalMarkdownExtension
{
    /// <summary>Gets the stable configuration identifier.</summary>
    string Id { get; }
    /// <summary>Gets the audited streaming invalidation policy.</summary>
    MarkdownExtensionInvalidation Invalidation { get; }
    /// <summary>Gets the versioned identity of terminal renderer registrations and fallback behavior.</summary>
    string RendererPolicyId { get; }
    /// <summary>Configures the parser before the immutable pipeline is built.</summary>
    void ConfigureParser(MarkdownPipelineBuilder builder, IReadOnlyDictionary<string, string> options);
    /// <summary>Registers compatible terminal renderers before dispatch is warmed.</summary>
    void ConfigureTerminal(ObjectRendererCollection renderers, IReadOnlyDictionary<string, string> options);
}

/// <summary>Identifies an HPD-created Markdig pipeline.</summary>
public sealed class MarkdownPipelineDescriptor
{
    internal MarkdownPipelineDescriptor(string stableId, MarkdownPipelineConfiguration configuration, Markdig.MarkdownPipeline pipeline,
        IReadOnlyList<RegisteredTerminalMarkdownExtension> terminalExtensions,
        IReadOnlyList<TerminalRendererRegistration> rendererRegistrations)
    {
        StableId = stableId;
        Configuration = configuration;
        Pipeline = pipeline;
        TerminalExtensions = terminalExtensions;
        RendererRegistrations = rendererRegistrations;
    }

    /// <summary>Gets the stable structural pipeline identity.</summary>
    public string StableId { get; }

    /// <summary>Gets the normalized immutable configuration.</summary>
    public MarkdownPipelineConfiguration Configuration { get; }

    internal Markdig.MarkdownPipeline Pipeline { get; }
    internal IReadOnlyList<RegisteredTerminalMarkdownExtension> TerminalExtensions { get; }
    internal IReadOnlyList<TerminalRendererRegistration> RendererRegistrations { get; }
}

internal sealed record RegisteredTerminalMarkdownExtension(
    ITerminalMarkdownExtension Extension,
    IReadOnlyDictionary<string, string> Options);

/// <summary>Creates the supported immutable Markdown pipeline descriptors.</summary>
public static class MarkdownPipelineFactory
{
    private static readonly Lazy<MarkdownPipelineDescriptor> Default = new(
        static () => Create(new MarkdownPipelineConfiguration()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the shared immutable default HPD terminal Markdown pipeline.</summary>
    public static MarkdownPipelineDescriptor CreateDefault() => Default.Value;

    /// <summary>Creates a descriptor after canonicalizing all caller-owned collections.</summary>
    public static MarkdownPipelineDescriptor Create(MarkdownPipelineConfiguration configuration,
        IReadOnlyList<ITerminalMarkdownExtension>? terminalExtensions = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.MaximumNestingDepth);
        if (configuration.MaximumNestingDepth != 128)
            throw new NotSupportedException("The pinned Markdig runtime exposes a fixed traversal limit; HPD supports its audited value 128.");

        var registry = BuiltInExtensions().Concat(terminalExtensions ?? []).ToDictionary(
            static extension => extension.Id, StringComparer.Ordinal);
        var extensions = (configuration.Extensions ?? DefaultExtensions())
            .Select(extension => new MarkdownExtensionConfiguration(
                extension.Id,
                new ReadOnlyDictionary<string, string>(extension.NormalizedOptions
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
                extension.Invalidation ?? (registry.TryGetValue(extension.Id, out var implementation)
                    ? implementation.Invalidation
                    : MarkdownExtensionInvalidation.DocumentGlobal)))
            .OrderBy(static extension => extension.Id, StringComparer.Ordinal)
            .ToArray();
        var normalized = configuration with { Extensions = Array.AsReadOnly(extensions) };

        var builder = new MarkdownPipelineBuilder();
        var registrations = new List<RegisteredTerminalMarkdownExtension>(extensions.Length);
        foreach (var extension in extensions)
        {
            if (!registry.TryGetValue(extension.Id, out var implementation))
                throw new NotSupportedException($"Markdown extension '{extension.Id}' has no allowlisted terminal implementation.");
            if (extension.Invalidation != implementation.Invalidation)
                throw new InvalidOperationException($"Markdown extension '{extension.Id}' cannot override its audited streaming policy.");
            implementation.ConfigureParser(builder, extension.NormalizedOptions);
            registrations.Add(new(implementation, extension.NormalizedOptions));
        }
        builder.PreciseSourceLocation = normalized.PreciseSourceLocation;
        builder.TrackTrivia = normalized.TrackTrivia;
        var pipeline = builder.Build();
        var identity = string.Join('|', normalized.MaximumNestingDepth, normalized.PreciseSourceLocation,
            normalized.TrackTrivia, normalized.SemanticAnalysisVersion, normalized.RendererPolicyVersion,
            string.Join(';', extensions.Select(e => e.Id + ':' + e.Invalidation + ':' + registry[e.Id].RendererPolicyId + ':' +
                string.Join(',', e.NormalizedOptions.Select(static p => p.Key + '=' + p.Value)))));
        var stableId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var frozenExtensions = registrations.AsReadOnly();
        return new MarkdownPipelineDescriptor(stableId, normalized, pipeline, frozenExtensions,
            TerminalMarkdownRenderer.DescribeObjectRenderers(frozenExtensions));
    }

    private static MarkdownExtensionConfiguration[] DefaultExtensions() =>
    [
        new("autolinks", ReadOnlyDictionary<string, string>.Empty),
        new("emphasis-extras", ReadOnlyDictionary<string, string>.Empty),
        new("pipe-tables", ReadOnlyDictionary<string, string>.Empty),
        new("task-lists", ReadOnlyDictionary<string, string>.Empty)
    ];

    private static ITerminalMarkdownExtension[] BuiltInExtensions() =>
    [
        new BuiltInTerminalMarkdownExtension("autolinks", static builder => builder.UseAutoLinks()),
        new BuiltInTerminalMarkdownExtension("emphasis-extras", static builder => builder.UseEmphasisExtras()),
        new BuiltInTerminalMarkdownExtension("pipe-tables", static builder => builder.UsePipeTables()),
        new BuiltInTerminalMarkdownExtension("task-lists", static builder => builder.UseTaskLists())
    ];

    private sealed class BuiltInTerminalMarkdownExtension(string id, Action<MarkdownPipelineBuilder> configure) : ITerminalMarkdownExtension
    {
        public string Id { get; } = id;
        public MarkdownExtensionInvalidation Invalidation => MarkdownExtensionInvalidation.BlockLocal;
        public string RendererPolicyId => "hpd-terminal-core-v1";
        public void ConfigureParser(MarkdownPipelineBuilder builder, IReadOnlyDictionary<string, string> options) => configure(builder);
        public void ConfigureTerminal(ObjectRendererCollection renderers, IReadOnlyDictionary<string, string> options) { }
    }
}

/// <summary>Default Markdig-backed document parser.</summary>
public sealed class MarkdownDocumentParser : IMarkdownDocumentParser
{
    /// <inheritdoc />
    public MarkdownDocumentSnapshot Parse(string source, MarkdownParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumSourceLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumDelimiterCharacters);
        if (source.Length > options.MaximumSourceLength)
            throw new ArgumentException("Markdown source exceeds the configured semantic-parse limit.", nameof(source));
        var delimiters = 0;
        foreach (var character in source)
            if (character is '`' or '*' or '_' or '[' or ']' or '(' or ')' or '|' or '<' or '>' or '&' &&
                ++delimiters > options.MaximumDelimiterCharacters)
                throw new ArgumentException("Markdown delimiter work exceeds the configured semantic-parse limit.", nameof(source));
        var syntax = Markdig.Markdown.Parse(source, options.Pipeline.Pipeline);
        var blocks = syntax.Select((block, ordinal) => MarkdownTopLevelBlock.From(block, ordinal, source.Length)).ToArray();
        var features = MarkdownSemanticAnalysis.GetFeatures(syntax);
        if (options.Pipeline.Configuration.Extensions?.Any(static extension =>
                extension.Invalidation == MarkdownExtensionInvalidation.DocumentGlobal) == true)
            features |= MarkdownDocumentFeatures.ExtensionGlobalState;
        var nodes = syntax.Descendants().ToArray();
        var maximumDepth = GetMaximumDepth(syntax, 0);
        if (maximumDepth > options.Pipeline.Configuration.MaximumNestingDepth)
            throw new InvalidOperationException("Markdown nesting exceeds the configured parser and terminal-renderer limit.");
        var capabilities = nodes
            .Select(node => DescribeCapability(node.GetType(), options.Pipeline.RendererRegistrations))
            .Append(new MarkdownNodeCapability(
                syntax.GetType().FullName ?? syntax.GetType().Name,
                MarkdownTerminalNodeHandling.OwnedByParentRenderer,
                typeof(TerminalMarkdownRenderer).FullName))
            .Distinct()
            .OrderBy(static capability => capability.RuntimeType, StringComparer.Ordinal)
            .ToArray();
        return new MarkdownDocumentSnapshot(source, blocks, features, Array.AsReadOnly(capabilities), maximumDepth, options.Pipeline, syntax);
    }

    private static MarkdownNodeCapability DescribeCapability(Type nodeType,
        IReadOnlyList<TerminalRendererRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            if (registration.OwnedNodeTypes.Any(owned => owned.IsAssignableFrom(nodeType)))
                return new MarkdownNodeCapability(
                    nodeType.FullName ?? nodeType.Name,
                    MarkdownTerminalNodeHandling.OwnedByParentRenderer,
                    registration.RendererType);
            if (registration.AcceptedNodeType.IsAssignableFrom(nodeType))
                return new MarkdownNodeCapability(
                    nodeType.FullName ?? nodeType.Name,
                    registration.IsFallback ? MarkdownTerminalNodeHandling.SanitizedSourceFallback : MarkdownTerminalNodeHandling.TypedRenderer,
                    registration.RendererType);
        }
        throw new InvalidOperationException($"Frozen terminal registry cannot handle '{nodeType.FullName}'.");
    }

    private static int GetMaximumDepth(MarkdownObject node, int depth)
    {
        var maximum = depth;
        if (node is ContainerBlock containerBlock)
            foreach (var child in containerBlock)
                maximum = Math.Max(maximum, GetMaximumDepth(child, depth + 1));
        if (node is LeafBlock { Inline: { } inline })
            maximum = Math.Max(maximum, GetMaximumDepth(inline, depth + 1));
        if (node is Markdig.Syntax.Inlines.ContainerInline containerInline)
            for (var child = containerInline.FirstChild; child is not null; child = child.NextSibling)
                maximum = Math.Max(maximum, GetMaximumDepth(child, depth + 1));
        return maximum;
    }
}
