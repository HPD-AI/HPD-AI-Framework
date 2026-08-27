using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace HPD.Agent;

public enum ProviderOperationKind
{
    ChatModelResponse,
    RealtimeModelResponse,
    SpeechToText,
    TextToSpeech,
    RealtimeInputTranscription,
    ImageGeneration,
    Embeddings,
    HostedFileOperation,
    VoiceActivityDetection,
    EndOfTurnDetection
}

public enum ProviderOperationOutcome
{
    Succeeded,
    Failed,
    Cancelled,
    Unknown
}

public enum UsageUpdateSemantics
{
    Delta,
    CumulativeSnapshot,
    FinalOnly
}

public sealed class ProviderUsageAccumulator(UsageUpdateSemantics semantics)
{
    private UsageDetails? _usage;
    private int _observations;

    public UsageDetails? Usage => _usage;

    public void Observe(UsageDetails usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        _observations++;
        switch (semantics)
        {
            case UsageUpdateSemantics.Delta:
                _usage ??= new UsageDetails();
                _usage.Add(usage);
                break;
            case UsageUpdateSemantics.CumulativeSnapshot:
                _usage = Clone(usage);
                break;
            case UsageUpdateSemantics.FinalOnly:
                if (_observations > 1)
                    throw new InvalidOperationException("A final-only provider emitted more than one usage update.");
                _usage = Clone(usage);
                break;
            default:
                throw new InvalidOperationException($"Undeclared usage update semantics '{semantics}'.");
        }
    }

    private static UsageDetails Clone(UsageDetails usage)
    {
        var clone = new UsageDetails();
        clone.Add(usage);
        return clone;
    }
}

public sealed record ProviderUsageMeasurement(
    string SourceEventId,
    string MessageTurnId,
    long ThreadSequenceNumber,
    string OperationId,
    string? LogicalOperationId,
    int Attempt,
    ProviderOperationKind OperationKind,
    ProviderClientFamily Family,
    ProviderOperationOutcome Outcome,
    UsageDetails? Usage,
    string? ProviderKey,
    string? ModelId,
    string? ResponseId);

public sealed record MessageTurnUsageSummary(IReadOnlyList<ProviderUsageMeasurement> Operations)
{
    public static MessageTurnUsageSummary Empty { get; } = new([]);

    public UsageDetails? AggregateCompatibleUsage(ProviderClientFamily? family = null)
    {
        UsageDetails? aggregate = null;
        foreach (var operation in Operations)
        {
            if (operation.Usage is null || (family.HasValue && operation.Family != family.Value))
            {
                continue;
            }

            if (aggregate is null)
            {
                aggregate = new UsageDetails();
            }
            aggregate.Add(operation.Usage);
        }

        return aggregate;
    }
}

public sealed class MessageTurnUsageCollector
{
    private readonly object _gate = new();
    private readonly string _messageTurnId;
    private readonly List<(long Ordinal, ProviderUsageMeasurement Measurement)> _measurements = [];
    private readonly HashSet<string> _sourceEventIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _operationIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nonDurableTerminalOperationIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingAttempt> _pendingAttempts = new(StringComparer.Ordinal);
    private long _nextOrdinal;
    private MessageTurnUsageSummary? _frozen;
    private bool _closing;
    private Func<AgentEvent, CancellationToken, ValueTask<AgentEvent>>? _committer;

    public MessageTurnUsageCollector(string messageTurnId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTurnId);
        _messageTurnId = messageTurnId;
    }

    public string MessageTurnId => _messageTurnId;

    public bool TryAcceptCommitted(ProviderUsageMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (!string.Equals(measurement.MessageTurnId, _messageTurnId, StringComparison.Ordinal))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(measurement.SourceEventId) ||
            string.IsNullOrWhiteSpace(measurement.OperationId))
        {
            throw new InvalidOperationException("Usage measurements require a committed source identity and a valid sequence.");
        }

        lock (_gate)
        {
            if (_frozen is not null)
                throw new InvalidOperationException($"Message-turn accounting for '{_messageTurnId}' is closed.");
            if (_closing && !_pendingAttempts.ContainsKey(measurement.OperationId))
            {
                throw new InvalidOperationException($"Unregistered provider operation '{measurement.OperationId}' arrived while accounting was closing.");
            }

            // A terminal without a canonical journal position releases the barrier so CloseAsync
            // can fail deterministically. It is never converted into a successful empty summary.
            if (measurement.ThreadSequenceNumber <= 0)
            {
                if (_nonDurableTerminalOperationIds.Add(measurement.OperationId) &&
                    _pendingAttempts.Remove(measurement.OperationId, out var nonDurablePending))
                {
                    nonDurablePending.Completion.TrySetResult();
                }
                return false;
            }
            if (_sourceEventIds.Contains(measurement.SourceEventId) || _operationIds.Contains(measurement.OperationId))
            {
                return false;
            }
            _sourceEventIds.Add(measurement.SourceEventId);
            _operationIds.Add(measurement.OperationId);
            _measurements.Add((_nextOrdinal++, measurement));
            if (_pendingAttempts.Remove(measurement.OperationId, out var pending))
            {
                pending.Completion.TrySetResult();
            }
            return true;
        }
    }

    public ProviderUsageAttemptTicket RegisterAttempt(ProviderOperationAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(attempt.OperationId);
        lock (_gate)
        {
            if (_closing || _frozen is not null)
                throw new InvalidOperationException($"Message-turn accounting for '{_messageTurnId}' is closing.");
            if (_operationIds.Contains(attempt.OperationId) || _pendingAttempts.ContainsKey(attempt.OperationId))
                throw new InvalidOperationException($"Provider operation '{attempt.OperationId}' is already registered.");
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingAttempts.Add(attempt.OperationId, new(attempt, completion));
            return new ProviderUsageAttemptTicket(this, attempt.OperationId);
        }
    }

    internal void ConfigureCommitter(Func<AgentEvent, CancellationToken, ValueTask<AgentEvent>> committer)
    {
        ArgumentNullException.ThrowIfNull(committer);
        lock (_gate)
        {
            if (_committer is not null)
                throw new InvalidOperationException("The accounting committer is already configured.");
            _committer = committer;
        }
    }

    public ValueTask<AgentEvent> CommitTerminalAsync(AgentEvent terminalEvent, CancellationToken cancellationToken = default)
    {
        Func<AgentEvent, CancellationToken, ValueTask<AgentEvent>> committer;
        lock (_gate)
            committer = _committer ?? throw new InvalidOperationException("No durable accounting committer is configured.");
        return committer(terminalEvent, cancellationToken);
    }

    public IReadOnlyList<ProviderOperationAttempt> GetPendingAttempts()
    {
        lock (_gate)
            return _pendingAttempts.Values.Select(static pending => pending.Attempt).ToArray();
    }

    public async ValueTask<MessageTurnUsageSummary> CloseAsync(CancellationToken cancellationToken = default)
    {
        Task[] pending;
        lock (_gate)
        {
            _closing = true;
            pending = _pendingAttempts.Values.Select(static pendingAttempt => pendingAttempt.Completion.Task).ToArray();
        }
        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_nonDurableTerminalOperationIds.Count > 0)
                throw new InvalidOperationException(
                    $"Message-turn accounting for '{_messageTurnId}' cannot close because " +
                    $"{_nonDurableTerminalOperationIds.Count} provider terminal event(s) were not durably committed.");
        }
        return Freeze();
    }

    internal void WithdrawBeforeDispatch(string operationId)
    {
        lock (_gate)
        {
            if (_pendingAttempts.Remove(operationId, out var pending))
                pending.Completion.TrySetResult();
        }
    }

    public MessageTurnUsageSummary Freeze()
    {
        lock (_gate)
        {
            return _frozen ??= new MessageTurnUsageSummary(_measurements
                .OrderBy(static entry => entry.Measurement.ThreadSequenceNumber)
                .ThenBy(static entry => entry.Ordinal)
                .Select(static entry => entry.Measurement)
                .ToArray());
        }
    }

    public static MessageTurnUsageCollector Replay(
        string messageTurnId,
        IEnumerable<ProviderUsageMeasurement> committedMeasurements)
    {
        var collector = new MessageTurnUsageCollector(messageTurnId);
        foreach (var measurement in committedMeasurements)
        {
            if (!collector.TryAcceptCommitted(measurement))
            {
                throw new InvalidOperationException($"Duplicate or foreign usage source '{measurement.SourceEventId}' during replay.");
            }
        }
        return collector;
    }

    private sealed record PendingAttempt(ProviderOperationAttempt Attempt, TaskCompletionSource Completion);
}

public sealed record ProviderOperationAttempt(
    string OperationId,
    string? LogicalOperationId,
    int Attempt,
    ProviderOperationKind OperationKind,
    ProviderClientFamily Family,
    string? ProviderKey,
    string? ModelId);

public sealed class ProviderUsageAttemptTicket
{
    private MessageTurnUsageCollector? _owner;
    internal ProviderUsageAttemptTicket(MessageTurnUsageCollector owner, string operationId)
    {
        _owner = owner;
        OperationId = operationId;
    }

    public string OperationId { get; }

    public void WithdrawBeforeDispatch()
        => Interlocked.Exchange(ref _owner, null)?.WithdrawBeforeDispatch(OperationId);
}

public static class ProviderOperationAccountingScope
{
    private static readonly AsyncLocal<MessageTurnUsageCollector?> Ambient = new();

    public static MessageTurnUsageCollector? Current => Ambient.Value;

    internal static IDisposable Push(MessageTurnUsageCollector collector)
    {
        var previous = Ambient.Value;
        Ambient.Value = collector;
        return new Scope(previous);
    }

    private sealed class Scope(MessageTurnUsageCollector? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

internal sealed class ProviderOperationAccountingBridge
{
    public MessageTurnUsageCollector? Collector { get; set; }
    private readonly ConcurrentQueue<AgentEvent> _terminalEvents = new();

    public void EnqueueTerminal(AgentEvent terminalEvent) => _terminalEvents.Enqueue(terminalEvent);

    public IReadOnlyList<AgentEvent> DrainTerminals()
    {
        var events = new List<AgentEvent>();
        while (_terminalEvents.TryDequeue(out var terminalEvent))
            events.Add(terminalEvent);
        return events;
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ProviderReportedMonetaryObservation), "provider_reported_monetary")]
public abstract record ProviderValuationObservation;

public sealed record ProviderReportedMonetaryObservation(
    decimal Amount,
    string Currency,
    string ProviderKey,
    string? ResponseId,
    string NativeField) : ProviderValuationObservation;

public enum ProviderUsageValuationStatus
{
    Complete,
    Partial,
    Unavailable,
    InvalidUsage
}

public enum ProviderUsageValuationAuthorityKind
{
    CatalogEstimate,
    ProviderReported,
    ContractRate,
    InvoiceReconciled
}

public enum ProviderUsageValuationDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ProviderReportedValuationProvenance), "provider_reported")]
[JsonDerivedType(typeof(ContractValuationProvenance), "contract")]
[JsonDerivedType(typeof(InvoiceValuationProvenance), "invoice")]
[JsonDerivedType(typeof(AuthorityAttemptValuationProvenance), "authority_attempt")]
public abstract record ProviderUsageValuationProvenance
{
    [JsonIgnore]
    public abstract ProviderUsageValuationAuthorityKind? AuthorityKind { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
public abstract record ProviderUsageValuationDetails;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
public abstract record ProviderValuationComponentProvenance;

public sealed record ProviderReportedValuationProvenance(
    string ProviderKey,
    string? ResponseId,
    string NativeField) : ProviderUsageValuationProvenance
{
    public override ProviderUsageValuationAuthorityKind? AuthorityKind => ProviderUsageValuationAuthorityKind.ProviderReported;
}

public sealed record ContractValuationProvenance(
    string ContractId,
    string RateRevision) : ProviderUsageValuationProvenance
{
    public override ProviderUsageValuationAuthorityKind? AuthorityKind => ProviderUsageValuationAuthorityKind.ContractRate;
}

public sealed record InvoiceValuationProvenance(
    string InvoiceId,
    string LineItemId) : ProviderUsageValuationProvenance
{
    public override ProviderUsageValuationAuthorityKind? AuthorityKind => ProviderUsageValuationAuthorityKind.InvoiceReconciled;
}

public sealed record AuthorityAttemptValuationProvenance(
    string AuthorityId,
    string AuthorityRevision,
    string ReasonCode) : ProviderUsageValuationProvenance
{
    public override ProviderUsageValuationAuthorityKind? AuthorityKind => null;
}

public sealed record ProviderUsageValuationComponent(
    string Category,
    decimal Quantity,
    string Unit,
    decimal? RateAmount,
    string? RateCurrency,
    decimal? RateQuantity,
    string? RateUnit,
    decimal Amount,
    ProviderValuationComponentProvenance? RateSelection);

public sealed record ProviderUsageUnpricedQuantity(
    string Category,
    decimal Quantity,
    string Unit,
    string Reason);

public sealed record ProviderUsageValuationDiagnostic(
    string Code,
    string Message,
    ProviderUsageValuationDiagnosticSeverity Severity);

public sealed record ProviderUsageValuation
{
    [JsonConstructor]
    public ProviderUsageValuation(
        string SourceEventId,
        string AuthorityId,
        ProviderUsageValuationAuthorityKind AuthorityKind,
        ProviderUsageValuationStatus Status,
        decimal? Amount,
        string? Currency,
        IReadOnlyList<ProviderUsageValuationComponent> Components,
        IReadOnlyList<ProviderUsageUnpricedQuantity> UnpricedUsage,
        ProviderUsageValuationProvenance Provenance,
        ProviderUsageValuationDetails? Details,
        IReadOnlyList<ProviderUsageValuationDiagnostic> Diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuthorityId);
        ArgumentNullException.ThrowIfNull(Components);
        ArgumentNullException.ThrowIfNull(UnpricedUsage);
        ArgumentNullException.ThrowIfNull(Provenance);
        ArgumentNullException.ThrowIfNull(Diagnostics);
        if (Status is ProviderUsageValuationStatus.Complete or ProviderUsageValuationStatus.Partial)
        {
            if (!Amount.HasValue || string.IsNullOrWhiteSpace(Currency) || Provenance.AuthorityKind != AuthorityKind)
                throw new ArgumentException("Available valuations require amount, currency, and authority-matching provenance.");
            if (Components.Sum(static component => component.Amount) != Amount.Value)
                throw new ArgumentException("The valuation amount must equal the exact component sum.");
        }
        else
        {
            if (Amount.HasValue || Provenance is not AuthorityAttemptValuationProvenance)
                throw new ArgumentException("Unavailable and invalid valuations require authority-attempt provenance and no amount.");
        }

        this.SourceEventId = SourceEventId;
        this.AuthorityId = AuthorityId;
        this.AuthorityKind = AuthorityKind;
        this.Status = Status;
        this.Amount = Amount;
        this.Currency = Currency;
        this.Components = Components;
        this.UnpricedUsage = UnpricedUsage;
        this.Provenance = Provenance;
        this.Details = Details;
        this.Diagnostics = Diagnostics;
    }

    public string SourceEventId { get; }
    public string AuthorityId { get; }
    public ProviderUsageValuationAuthorityKind AuthorityKind { get; }
    public ProviderUsageValuationStatus Status { get; }
    public decimal? Amount { get; }
    public string? Currency { get; }
    public IReadOnlyList<ProviderUsageValuationComponent> Components { get; }
    public IReadOnlyList<ProviderUsageUnpricedQuantity> UnpricedUsage { get; }
    public ProviderUsageValuationProvenance Provenance { get; }
    public ProviderUsageValuationDetails? Details { get; }
    public IReadOnlyList<ProviderUsageValuationDiagnostic> Diagnostics { get; }

    public static ProviderUsageValuation Unavailable(
        string sourceEventId,
        string authorityId,
        ProviderUsageValuationAuthorityKind authorityKind,
        string authorityRevision,
        string reasonCode,
        string message) => new(
            sourceEventId,
            authorityId,
            authorityKind,
            ProviderUsageValuationStatus.Unavailable,
            null,
            null,
            [],
            [],
            new AuthorityAttemptValuationProvenance(authorityId, authorityRevision, reasonCode),
            null,
            [new(reasonCode, message, ProviderUsageValuationDiagnosticSeverity.Warning)]);
}

public sealed record ProviderUsageValuationInput(
    ProviderUsageMeasurement Measurement,
    IReadOnlyList<ProviderValuationObservation> Observations);

public interface IProviderUsageValuationAuthority
{
    string AuthorityId { get; }

    ValueTask<ProviderUsageValuation> ValueAsync(
        ProviderUsageValuationInput input,
        CancellationToken cancellationToken = default);
}

public sealed class ProviderReportedUsageValuationAuthority : IProviderUsageValuationAuthority
{
    public string AuthorityId => "provider-reported";

    public ValueTask<ProviderUsageValuation> ValueAsync(
        ProviderUsageValuationInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        var matching = input.Observations
            .OfType<ProviderReportedMonetaryObservation>()
            .Where(observation =>
                string.Equals(observation.ProviderKey, input.Measurement.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                (input.Measurement.ResponseId is null ||
                 string.Equals(observation.ResponseId, input.Measurement.ResponseId, StringComparison.Ordinal)))
            .ToArray();
        if (matching.Length != 1)
        {
            return ValueTask.FromResult(ProviderUsageValuation.Unavailable(
                input.Measurement.SourceEventId,
                AuthorityId,
                ProviderUsageValuationAuthorityKind.ProviderReported,
                "provider-reported-v1",
                matching.Length == 0 ? "observation_missing" : "observation_ambiguous",
                matching.Length == 0
                    ? "No correlated provider-reported monetary observation is available."
                    : "Multiple correlated provider-reported monetary observations are available."));
        }

        var observation = matching[0];
        if (observation.Amount < 0 || string.IsNullOrWhiteSpace(observation.Currency))
        {
            return ValueTask.FromResult(new ProviderUsageValuation(
                input.Measurement.SourceEventId,
                AuthorityId,
                ProviderUsageValuationAuthorityKind.ProviderReported,
                ProviderUsageValuationStatus.InvalidUsage,
                null,
                null,
                [],
                [],
                new AuthorityAttemptValuationProvenance(AuthorityId, "provider-reported-v1", "invalid_observation"),
                null,
                [new("invalid_observation", "The correlated monetary observation is invalid.", ProviderUsageValuationDiagnosticSeverity.Error)]));
        }

        var component = new ProviderUsageValuationComponent(
            "provider_reported_amount", 1m, "operation",
            observation.Amount, observation.Currency, 1m, "operation",
            observation.Amount, null);
        return ValueTask.FromResult(new ProviderUsageValuation(
            input.Measurement.SourceEventId,
            AuthorityId,
            ProviderUsageValuationAuthorityKind.ProviderReported,
            ProviderUsageValuationStatus.Complete,
            observation.Amount,
            observation.Currency,
            [component],
            [],
            new ProviderReportedValuationProvenance(
                observation.ProviderKey, observation.ResponseId, observation.NativeField),
            null,
            []));
    }
}

public sealed record MessageTurnValuationProjection(
    IReadOnlyList<ProviderUsageValuation> SelectedValuations,
    IReadOnlyDictionary<string, decimal> KnownAmountsByCurrency);

public static class ProviderUsageValuationProjector
{
    public static MessageTurnValuationProjection ProjectPreferred(
        MessageTurnUsageSummary usage,
        IEnumerable<ProviderUsageValuation> valuations,
        IReadOnlyList<string>? authorityPreference = null)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(valuations);
        var sourceIds = usage.Operations.Select(static operation => operation.SourceEventId)
            .ToHashSet(StringComparer.Ordinal);
        var preference = authorityPreference?.Select((id, index) => (id, index))
            .ToDictionary(static value => value.id, static value => value.index, StringComparer.Ordinal)
            ?? new Dictionary<string, int>(StringComparer.Ordinal);
        var selected = valuations
            .Where(value => sourceIds.Contains(value.SourceEventId))
            .GroupBy(static value => value.SourceEventId, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(value => preference.TryGetValue(value.AuthorityId, out var index) ? index : int.MaxValue)
                .ThenBy(static value => DefaultAuthorityRank(value.AuthorityKind))
                .ThenBy(static value => value.Amount.HasValue ? 0 : 1)
                .First())
            .ToArray();
        var totals = selected
            .Where(static value => value.Amount.HasValue && !string.IsNullOrWhiteSpace(value.Currency))
            .GroupBy(static value => value.Currency!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Sum(value => value.Amount!.Value),
                StringComparer.OrdinalIgnoreCase);
        return new(selected, totals);
    }

    private static int DefaultAuthorityRank(ProviderUsageValuationAuthorityKind kind) => kind switch
    {
        ProviderUsageValuationAuthorityKind.InvoiceReconciled => 0,
        ProviderUsageValuationAuthorityKind.ContractRate => 1,
        ProviderUsageValuationAuthorityKind.ProviderReported => 2,
        ProviderUsageValuationAuthorityKind.CatalogEstimate => 3,
        _ => int.MaxValue
    };
}

public static class AgentUsageCountKeys
{
    public const string CacheWriteInputTokens = "hpd.cache_write_input_tokens";
    public const string CacheWriteInputTokens5Minute = "hpd.cache_write_input_tokens.5m";
    public const string CacheWriteInputTokens1Hour = "hpd.cache_write_input_tokens.1h";
    public const string AcceptedPredictionTokens = "hpd.accepted_prediction_tokens";
    public const string RejectedPredictionTokens = "hpd.rejected_prediction_tokens";
}
