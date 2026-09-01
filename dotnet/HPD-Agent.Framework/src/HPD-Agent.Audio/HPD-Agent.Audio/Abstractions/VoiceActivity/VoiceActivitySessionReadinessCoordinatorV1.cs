using HPD.Agent.Authority;
using HPD.Agent.Runtime;

namespace HPD.Agent.Audio.VoiceActivity;

internal abstract record VoiceActivitySessionReadinessResultV1
{
    private VoiceActivitySessionReadinessResultV1() { }
    internal sealed record Ready(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, bool AlreadyCommitted)
        : VoiceActivitySessionReadinessResultV1;
    internal sealed record ParticipantStartFailed(RuntimeParticipantResultV1 Result)
        : VoiceActivitySessionReadinessResultV1;
    internal sealed record AdmissionFailed(SessionLifecycleAdmissionResultV1 Admission, RuntimeParticipantResultV1 Cleanup)
        : VoiceActivitySessionReadinessResultV1;
    internal sealed record Retryable(SessionLifecycleAdmissionResultV1 Admission)
        : VoiceActivitySessionReadinessResultV1;
}

/// <summary>
/// Publishes S1 readiness only after the neutral runtime coordinator has started every admitted participant.
/// </summary>
/// <remarks>
/// This coordinator does not create readiness authority. It submits the existing canonical PublishReady command
/// to the sole S1 lifecycle admission path and preserves started participants while that admission is ambiguous.
/// </remarks>
internal sealed class VoiceActivitySessionReadinessCoordinatorV1
{
    private readonly RuntimeParticipantCoordinatorV1 _participants;
    private readonly IAuthorityJournalV1 _journal;
    private readonly SessionLifecycleCommandV1 _command;
    private readonly CorrelationEnvelopeV1 _correlation;
    private readonly UtcInstant _observedAt;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _participantsStarted;
    private bool _closed;

    internal VoiceActivitySessionReadinessCoordinatorV1(
        RuntimeParticipantCoordinatorV1 participants,
        IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session,
        ExpectedAuthorityVectorV1 authority,
        OperationId operationId,
        JournalPositionV1 expectedLifecycleFact,
        CorrelationEnvelopeV1 correlation,
        UtcInstant observedAt)
    {
        _participants = participants ?? throw new ArgumentNullException(nameof(participants));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        if (!session.IsValid || authority is null || authority.Session != session || !operationId.IsValid ||
            !expectedLifecycleFact.IsValid || !correlation.IsValid)
            throw new ArgumentException("Readiness requires one exact session, authority, operation, predecessor and correlation.");
        var body = new SessionLifecycleCommandBodyV1.PublishReady(
            operationId, expectedLifecycleFact, SessionAvailabilityWireV1.Available);
        _command = new SessionLifecycleCommandV1(session, authority, SessionLifecycleBodyCodecsV1.Encode(body));
        _correlation = correlation;
        _observedAt = observedAt;
    }

    internal async ValueTask<VoiceActivitySessionReadinessResultV1> StartAndPublishAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_closed) throw new InvalidOperationException("Readiness admission is closed.");
            if (!_participantsStarted)
            {
                var started = await _participants.StartAsync(cancellationToken).ConfigureAwait(false);
                if (!started.IsSuccess)
                {
                    _closed = true;
                    return new VoiceActivitySessionReadinessResultV1.ParticipantStartFailed(started);
                }
                _participantsStarted = true;
            }

            var admission = await SessionLifecycleAdmissionCoordinatorV1.AdmitAsync(
                _journal, _command, _correlation, _observedAt, cancellationToken).ConfigureAwait(false);
            switch (admission)
            {
                case SessionLifecycleAdmissionResultV1.Committed committed when IsReady(committed.Result):
                    _closed = true;
                    return new VoiceActivitySessionReadinessResultV1.Ready(
                        committed.Command, committed.Result, AlreadyCommitted: false);
                case SessionLifecycleAdmissionResultV1.AlreadyCommitted existing when IsReady(existing.Result):
                    _closed = true;
                    return new VoiceActivitySessionReadinessResultV1.Ready(
                        existing.Command, existing.Result, AlreadyCommitted: true);
                case SessionLifecycleAdmissionResultV1.OutcomeUnknown:
                case SessionLifecycleAdmissionResultV1.RetryRequired:
                    return new VoiceActivitySessionReadinessResultV1.Retryable(admission);
                default:
                    _closed = true;
                    var cleanup = await _participants.TerminateAsync(
                        RuntimeTerminationCauseV1.StartFailed, CancellationToken.None).ConfigureAwait(false);
                    return new VoiceActivitySessionReadinessResultV1.AdmissionFailed(admission, cleanup);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsReady(AuthorityFactEnvelopeV1 envelope) =>
        SessionLifecyclePayloadV1Codec.TryDecodeFact(envelope.PayloadMemory, out var outer) && outer is not null &&
        SessionLifecycleBodyCodecsV1.TryDecodeFact(outer.BodyBytes.ToArray(), out var fact) && fact is not null &&
        fact.Outcome is SessionLifecycleOutcomeV1.Applied or SessionLifecycleOutcomeV1.Idempotent &&
        fact.SafeCode is null && fact.Snapshot.State == SessionLifecycleStateWireV1.Active &&
        fact.Snapshot.Availability == SessionAvailabilityWireV1.Available &&
        fact.Snapshot.Readiness == SessionReadinessWireV1.Succeeded;
}
