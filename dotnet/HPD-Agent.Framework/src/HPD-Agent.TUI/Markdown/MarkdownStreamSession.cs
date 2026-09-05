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
    TimeSpan FinalizationDuration,
    long RetainedSourceBytes = 0,
    long ReparsedCharacters = 0,
    int StablePrefixNodes = 0,
    long PeakParseStateBytes = 0);

/// <summary>Owns canonical source and policy-directed incremental parsing for one agent message.</summary>
public sealed class MarkdownStreamSession
{
    private readonly ChunkedMarkdownSource _source = new();
    private readonly IMarkdownDocumentParser _parser;
    private readonly IIncrementalMarkdownParser _incrementalParser;
    private readonly MarkdownParseOptions _parseOptions;
    private readonly MarkdownMessagePresentation _presentation;
    private readonly ImmutableDictionary<string, object?> _additionalProperties;
    private MarkdownDocumentSnapshot _snapshot;
    private MarkdownParseState _parseState;
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
    private readonly int _maximumCanonicalSourceLength;

    public MarkdownStreamSession(
        MarkdownStreamIdentity identity,
        MarkdownMessagePresentation? presentation = null,
        IMarkdownDocumentParser? parser = null,
        MarkdownPipelineDescriptor? pipeline = null,
        IReadOnlyDictionary<string, object?>? additionalProperties = null,
        int maximumCanonicalSourceLength = 4_194_304)
    {
        if (string.IsNullOrWhiteSpace(identity.MessageId)) throw new ArgumentException("A message ID is required.", nameof(identity));
        Identity = identity;
        MessageId = identity.MessageId;
        LineageId = Guid.NewGuid();
        Projection = new(identity, LineageId);
        _presentation = presentation ?? new();
        if (!Enum.IsDefined(_presentation.IncompleteLinePolicy))
            throw new ArgumentOutOfRangeException(nameof(presentation),
                _presentation.IncompleteLinePolicy, "The incomplete-line policy is not defined.");
        if (_presentation.Visibility == AgentMessageVisibility.Hidden)
            throw new ArgumentException("Hidden streams must use lifecycle-only coordination and cannot own source.", nameof(presentation));
        _additionalProperties = additionalProperties?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object?>.Empty;
        _parser = parser ?? new MarkdownDocumentParser();
        _incrementalParser = new ConservativeIncrementalMarkdownParser(_parser);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCanonicalSourceLength);
        _maximumCanonicalSourceLength = maximumCanonicalSourceLength;
        _parseOptions = new() { Pipeline = pipeline ?? MarkdownPipelineFactory.CreateDefault() };
        _parseState = _incrementalParser.ParseInitial(ReadOnlyMemory<char>.Empty, _parseOptions);
        _snapshot = _parseState.Document;
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
        TimeSpan.FromTicks(_finalizationTicks), _parseState.RetainedSourceBytes,
        _parseState.ReparsedCharacters, _parseState.StablePrefixNodes,
        _parseState.PeakParseStateBytes);

    /// <summary>Appends exact source without parsing it.</summary>
    public MarkdownSourceChange Append(string delta)
    {
        EnsureStreaming();
        ArgumentNullException.ThrowIfNull(delta);
        if (delta.Length == 0) return new(false, false, Revision);
        if (delta.Length > _maximumCanonicalSourceLength - _source.Length)
            throw new ArgumentException("The delta would exceed the configured canonical-source limit.", nameof(delta));
        _source.Append(delta);
        _utf16CodeUnitsAppended += delta.Length;
        _deltasAccepted++;
        _pendingDeltas++;
        Revision++;
        var previousParseable = _parseableSourceLength;
        var finalNewline = _source.FindFinalNewline();
        _parseableSourceLength = finalNewline < 0 ? 0 : finalNewline + 1;
        return new(true, _parseableSourceLength > previousParseable, Revision);
    }

    /// <summary>Parses and publishes source according to the configured incomplete-line policy.</summary>
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
        var update = Publish(terminal: true, state);
        _finalizationTicks += Stopwatch.GetElapsedTime(started).Ticks;
        return update with { Diagnostics = Diagnostics };
    }

    private MarkdownStreamUpdate Publish(bool terminal, MarkdownMessageState state)
    {
        _deltasCoalesced += Math.Max(0, _pendingDeltas - 1);
        _pendingDeltas = 0;
        var requestedParsedLength = terminal
            ? _source.Length
            : _presentation.IncompleteLinePolicy == MarkdownIncompleteLinePolicy.StreamRich
                ? Math.Max(_snapshot.SourceLength, _source.Length - GetAmbiguousPrefixHoldback())
                : _parseableSourceLength;
        var parsedLength = requestedParsedLength;
        var previousStable = _stableSourceLength;
        if (_snapshot.SourceLength != requestedParsedLength || terminal && _parseState.StableSourceLength != requestedParsedLength)
        {
            var started = Stopwatch.GetTimestamp();
            var previousFallbacks = _parseState.FallbackCount;
            try
            {
                var appendedLength = requestedParsedLength - _snapshot.SourceLength;
                if (appendedLength < 0)
                    throw new InvalidOperationException("Canonical Markdown source cannot shrink within a stream lineage.");
                var suffix = _source.Slice(_snapshot.SourceLength, appendedLength);
                _parseState = _incrementalParser.Append(_parseState, suffix.AsMemory(), terminal);
                _snapshot = _parseState.Document;
                _parseFallbacks += _parseState.FallbackCount - previousFallbacks;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                _parseFallbacks++;
                parsedLength = _snapshot.SourceLength;
            }
            finally
            {
                _parseCount++;
                _parseTicks += Stopwatch.GetElapsedTime(started).Ticks;
            }
        }

        var parseFallback = parsedLength != requestedParsedLength;
        var global = (_snapshot.Features & (MarkdownDocumentFeatures.ReferenceDefinitions | MarkdownDocumentFeatures.ExtensionGlobalState)) != 0;
        _stableSourceLength = terminal ? _snapshot.SourceLength : global ? 0 : FindStableBoundary(_snapshot, terminal: false);
        if (!terminal && _snapshot.Blocks.LastOrDefault()?.Kind == MarkdownBlockKind.Table)
            _tableHoldbackActivations++;
        if (global && !_documentGlobal) Epoch++;
        _documentGlobal = global;
        Projection.Revision = Revision;
        Projection.Epoch = Epoch;
        var tail = _source.Slice(parsedLength, _source.Length - parsedLength);
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

    private int GetAmbiguousPrefixHoldback()
    {
        const int maximumBlockPrefixLength = 8;
        if (_source.Length > 0)
        {
            var last = _source[_source.Length - 1];
            if (last is '\\' or '&' or '[' or '<') return 1;
            if (last is '*' or '_' or '~' or '`')
            {
                var run = 1;
                while (run < 3 && run < _source.Length && _source[_source.Length - run - 1] == last)
                    run++;
                return run;
            }
        }

        var lineStart = _source.FindFinalNewline() + 1;
        var length = _source.Length - lineStart;
        if (length is <= 0 or > maximumBlockPrefixLength) return 0;

        Span<char> prefix = stackalloc char[maximumBlockPrefixLength];
        for (var index = 0; index < length; index++) prefix[index] = _source[lineStart + index];
        var text = prefix[..length];
        var allHashes = true;
        for (var index = 0; index < text.Length; index++) allHashes &= text[index] == '#';
        if (allHashes && length <= 6) return length;
        if (text.SequenceEqual("-") || text.SequenceEqual("+") || text.SequenceEqual("*") ||
            text.SequenceEqual(">") || text.SequenceEqual("`") || text.SequenceEqual("``") ||
            text.SequenceEqual("```") || text.SequenceEqual("~") || text.SequenceEqual("~~") ||
            text.SequenceEqual("~~~")) return length;
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits])) digits++;
        return digits == text.Length || digits > 0 && digits == text.Length - 1 && text[^1] is '.' or ')'
            ? length
            : 0;
    }

    private int FindStableBoundary(MarkdownDocumentSnapshot snapshot, bool terminal)
    {
        if (terminal) return snapshot.SourceLength;
        if (snapshot.Blocks.Count < 2) return 0;
        var candidateIndex = snapshot.Blocks.Count - 1;
        while (candidateIndex > 0)
        {
            var preceding = snapshot.Blocks[candidateIndex - 1];
            var following = snapshot.Blocks[candidateIndex];
            var blankSeparated = HasBlankLine(preceding.SourceEndExclusive, following.SourceStart);
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

    private bool HasBlankLine(int start, int endExclusive)
    {
        var lineBreaks = 0;
        for (var index = Math.Clamp(start, 0, _source.Length);
             index < Math.Clamp(endExclusive, 0, _source.Length);
             index++)
            if (_source[index] == '\n' && ++lineBreaks >= 2) return true;
        return false;
    }

    private void EnsureStreaming()
    {
        if (State != MarkdownMessageState.Streaming) throw new InvalidOperationException("A terminal Markdown stream cannot be mutated.");
    }
}
