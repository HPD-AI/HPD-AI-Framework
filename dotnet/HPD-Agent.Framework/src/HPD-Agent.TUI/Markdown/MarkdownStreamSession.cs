using System.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using HPD.TUI.Markdown;

namespace HPD.Agent.TUI.Markdown;

/// <summary>Describes a source append without forcing a parse.</summary>
public readonly record struct MarkdownSourceChange(bool SourceChanged, bool CompletedPhysicalLine, long Revision);

/// <summary>Identifies the layout invalidation caused by one publication.</summary>
public enum MarkdownInvalidationKind { None, MutableTail, StableAppendAndMutableTail, FullMessage, Finalized }

/// <summary>Publishes an immutable message document and its invalidation boundary.</summary>
public sealed record MarkdownStreamUpdate(
    MarkdownMessageDocument Document,
    MarkdownInvalidationKind Invalidation,
    int PreviousStableSourceLength,
    int StableSourceLength,
    MarkdownStreamDiagnosticsSnapshot Diagnostics);

/// <summary>Reports source-safe streaming parser measurements for one message lineage.</summary>
public readonly record struct MarkdownStreamDiagnosticsSnapshot(
    long Utf16CodeUnitsAppended,
    long DeltasAccepted,
    long DeltasCoalesced,
    long ParseCount,
    TimeSpan ParseDuration,
    long ParseFallbacks,
    long PublicationCount,
    long FullMessageInvalidations,
    long TableHoldbackActivations,
    TimeSpan FinalizationDuration);

/// <summary>Owns canonical source and newline-gated parsing for one agent message.</summary>
public sealed class MarkdownStreamSession
{
    private readonly StringBuilder _source = new();
    private readonly IMarkdownDocumentParser _parser;
    private readonly MarkdownParseOptions _parseOptions;
    private readonly MarkdownMessagePresentation _presentation;
    private readonly ImmutableDictionary<string, object?> _additionalProperties;
    private MarkdownDocumentSnapshot _snapshot;
    private int _parseableSourceLength;
    private int _stableSourceLength;
    private string? _failureDetail;
    private bool _documentGlobal;
    private long _utf16CodeUnitsAppended;
    private long _deltasAccepted;
    private long _deltasCoalesced;
    private long _pendingDeltas;
    private long _parseCount;
    private long _parseTicks;
    private long _parseFallbacks;
    private long _publicationCount;
    private long _fullMessageInvalidations;
    private long _tableHoldbackActivations;
    private long _finalizationTicks;

    public MarkdownStreamSession(
        MarkdownStreamIdentity identity,
        MarkdownMessagePresentation? presentation = null,
        IMarkdownDocumentParser? parser = null,
        MarkdownPipelineDescriptor? pipeline = null,
        IReadOnlyDictionary<string, object?>? additionalProperties = null)
    {
        if (string.IsNullOrWhiteSpace(identity.MessageId)) throw new ArgumentException("A message ID is required.", nameof(identity));
        Identity = identity;
        MessageId = identity.MessageId;
        LineageId = Guid.NewGuid();
        Projection = new(identity, LineageId);
        _presentation = presentation ?? new();
        if (_presentation.Visibility == AgentMessageVisibility.Hidden)
            throw new ArgumentException("Hidden streams must use lifecycle-only coordination and cannot own source.", nameof(presentation));
        _additionalProperties = additionalProperties?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object?>.Empty;
        _parser = parser ?? new MarkdownDocumentParser();
        _parseOptions = new() { Pipeline = pipeline ?? MarkdownPipelineFactory.CreateDefault() };
        _snapshot = _parser.Parse(string.Empty, _parseOptions);
    }

    /// <summary>Gets the stream kind and external message identity.</summary>
    public MarkdownStreamIdentity Identity { get; }
    /// <summary>Gets the external message identity.</summary>
    public string MessageId { get; }
    /// <summary>Gets the unique identity of this accepted Start lineage.</summary>
    public Guid LineageId { get; }
    /// <summary>Gets the projection retained across document publications.</summary>
    public MarkdownMessageProjection Projection { get; }
    /// <summary>Gets the lifecycle state.</summary>
    public MarkdownMessageState State { get; private set; } = MarkdownMessageState.Streaming;
    /// <summary>Gets the exact-source revision.</summary>
    public long Revision { get; private set; }
    /// <summary>Gets the document-global invalidation epoch.</summary>
    public long Epoch { get; private set; }
    /// <summary>Gets structured measurements that never include model source.</summary>
    public MarkdownStreamDiagnosticsSnapshot Diagnostics => new(
        _utf16CodeUnitsAppended, _deltasAccepted, _deltasCoalesced,
        _parseCount, TimeSpan.FromTicks(_parseTicks), _parseFallbacks,
        _publicationCount, _fullMessageInvalidations, _tableHoldbackActivations,
        TimeSpan.FromTicks(_finalizationTicks));

    /// <summary>Appends exact source without parsing it.</summary>
    public MarkdownSourceChange Append(string delta)
    {
        EnsureStreaming();
        ArgumentNullException.ThrowIfNull(delta);
        if (delta.Length == 0) return new(false, false, Revision);
        _source.Append(delta);
        _utf16CodeUnitsAppended += delta.Length;
        _deltasAccepted++;
        _pendingDeltas++;
        Revision++;
        var previousParseable = _parseableSourceLength;
        var finalNewline = FindFinalNewline(_source);
        _parseableSourceLength = finalNewline < 0 ? 0 : finalNewline + 1;
        return new(true, _parseableSourceLength > previousParseable, Revision);
    }

    /// <summary>Parses the current complete-line prefix and publishes its literal incomplete tail.</summary>
    public MarkdownStreamUpdate Refresh() => Publish(terminal: false, MarkdownMessageState.Streaming);
    /// <summary>Completes the stream and parses every accepted code unit.</summary>
    public MarkdownStreamUpdate Complete() => Transition(MarkdownMessageState.Completed);
    /// <summary>Interrupts the stream while retaining every accepted code unit.</summary>
    public MarkdownStreamUpdate Interrupt() => Transition(MarkdownMessageState.Interrupted);
    /// <summary>Cancels the stream while retaining every accepted code unit.</summary>
    public MarkdownStreamUpdate Cancel() => Transition(MarkdownMessageState.Cancelled);
    /// <summary>Fails the stream while retaining every accepted code unit.</summary>
    public MarkdownStreamUpdate Fail(string? detail = null)
    {
        _failureDetail = detail;
        return Transition(MarkdownMessageState.Failed);
    }

    private MarkdownStreamUpdate Transition(MarkdownMessageState state)
    {
        EnsureStreaming();
        State = state;
        var started = Stopwatch.GetTimestamp();
        try { return Publish(terminal: true, state); }
        finally { _finalizationTicks += Stopwatch.GetElapsedTime(started).Ticks; }
    }

    private MarkdownStreamUpdate Publish(bool terminal, MarkdownMessageState state)
    {
        _deltasCoalesced += Math.Max(0, _pendingDeltas - 1);
        _pendingDeltas = 0;
        var requestedParsedLength = terminal ? _source.Length : _parseableSourceLength;
        var parsedLength = requestedParsedLength;
        var parsedSource = _source.ToString(0, requestedParsedLength);
        var previousStable = _stableSourceLength;
        if (_snapshot.Source != parsedSource)
        {
            var started = Stopwatch.GetTimestamp();
            try { _snapshot = _parser.Parse(parsedSource, _parseOptions); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                _parseFallbacks++;
                parsedLength = _snapshot.Source.Length;
            }
            finally
            {
                _parseCount++;
                _parseTicks += Stopwatch.GetElapsedTime(started).Ticks;
            }
        }

        var parseFallback = parsedLength != requestedParsedLength;
        var global = (_snapshot.Features & (MarkdownDocumentFeatures.ReferenceDefinitions | MarkdownDocumentFeatures.ExtensionGlobalState)) != 0;
        _stableSourceLength = terminal ? _snapshot.Source.Length : global ? 0 : FindStableBoundary(_snapshot, terminal: false);
        if (!terminal && _snapshot.Blocks.LastOrDefault()?.Kind == MarkdownBlockKind.Table)
            _tableHoldbackActivations++;
        if (global && !_documentGlobal) Epoch++;
        _documentGlobal = global;
        Projection.Revision = Revision;
        Projection.Epoch = Epoch;
        var tail = _source.ToString(parsedLength, _source.Length - parsedLength);
        var document = new MarkdownMessageDocument
        {
            Identity = Identity,
            LineageId = LineageId,
            MessageId = MessageId,
            Parsed = _snapshot,
            UnparsedTail = tail,
            StableSourceLength = _stableSourceLength,
            State = state,
            FailureDetail = _failureDetail,
            Presentation = _presentation,
            Revision = Revision,
            Epoch = Epoch,
            AdditionalProperties = _additionalProperties
        };
        var invalidation = terminal ? MarkdownInvalidationKind.Finalized
            : parseFallback ? MarkdownInvalidationKind.FullMessage
            : global ? MarkdownInvalidationKind.FullMessage
            : _stableSourceLength > previousStable ? MarkdownInvalidationKind.StableAppendAndMutableTail
            : MarkdownInvalidationKind.MutableTail;
        _publicationCount++;
        if (invalidation == MarkdownInvalidationKind.FullMessage) _fullMessageInvalidations++;
        return new(document, invalidation, previousStable, _stableSourceLength, Diagnostics);
    }

    private static int FindStableBoundary(MarkdownDocumentSnapshot snapshot, bool terminal)
    {
        if (terminal) return snapshot.Source.Length;
        if (snapshot.Blocks.Count < 2) return 0;
        var candidateIndex = snapshot.Blocks.Count - 1;
        while (candidateIndex > 0)
        {
            var preceding = snapshot.Blocks[candidateIndex - 1];
            var following = snapshot.Blocks[candidateIndex];
            var blankSeparated = HasBlankLine(snapshot.Source, preceding.SourceEndExclusive, following.SourceStart);
            var proven = preceding.Kind switch
            {
                MarkdownBlockKind.ThematicBreak => true,
                MarkdownBlockKind.Paragraph or MarkdownBlockKind.Heading => blankSeparated,
                MarkdownBlockKind.Quote => blankSeparated && following.Kind != MarkdownBlockKind.Quote,
                MarkdownBlockKind.List => blankSeparated && following.Kind != MarkdownBlockKind.List,
                MarkdownBlockKind.Table => following.Kind != MarkdownBlockKind.Table,
                MarkdownBlockKind.Code => blankSeparated,
                // HTML subtypes and extension/unknown blocks remain mutable by default.
                _ => false
            };
            if (proven) return following.SourceStart;
            candidateIndex--;
        }
        return 0;
    }

    private static bool HasBlankLine(string source, int start, int endExclusive)
    {
        var lineBreaks = 0;
        for (var index = Math.Clamp(start, 0, source.Length);
             index < Math.Clamp(endExclusive, 0, source.Length);
             index++)
            if (source[index] == '\n' && ++lineBreaks >= 2) return true;
        return false;
    }

    private static int FindFinalNewline(StringBuilder source)
    {
        for (var index = source.Length - 1; index >= 0; index--) if (source[index] == '\n') return index;
        return -1;
    }

    private void EnsureStreaming()
    {
        if (State != MarkdownMessageState.Streaming) throw new InvalidOperationException("A terminal Markdown stream cannot be mutated.");
    }
}
