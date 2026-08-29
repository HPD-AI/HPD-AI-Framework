using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio;

/// <summary>Starts one retained live-Audio backend session after command admission.</summary>
public interface IManagedAudioSessionBackendV1
{
    ValueTask<IManagedAudioSessionV1> StartAsync(
        ManagedAudioSessionStartRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>Consumes retained decoded PCM and yields finalized semantic candidates.</summary>
public interface IManagedAudioTranscriptSourceV1
{
    IAsyncEnumerable<ManagedAudioTranscriptCandidateV1> RunAsync(
        IAudioSource source,
        CancellationToken cancellationToken = default);
}

/// <summary>The effect-owning backend session borrowed by the S1 command authority.</summary>
public interface IManagedAudioSessionV1 : IAsyncDisposable
{
    string AudioSessionId { get; }

    IAsyncEnumerable<ManagedAudioTranscriptCandidateV1> ReadTranscriptCandidatesAsync(
        CancellationToken cancellationToken = default);

    ValueTask SetInputEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    ValueTask SetOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    ValueTask<ManagedAudioOutputInterruptionV1> InterruptOutputAsync(
        string operationId,
        CancellationToken cancellationToken = default);
    ValueTask StopAsync(AudioSessionStopReason reason, CancellationToken cancellationToken = default);
}

public sealed record ManagedAudioSessionStartRequestV1
{
    public required string AgentId { get; init; }
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public required AudioSessionStartBindings Bindings { get; init; }
    public AgentClientSet? ClientSet { get; init; }
}

public sealed record ManagedAudioTranscriptCandidateV1
{
    public required string CandidateId { get; init; }
    public required string Text { get; init; }
    public bool CommitAutomatically { get; init; } = true;
}

public enum ManagedAudioOutputInterruptionV1
{
    Interrupted = 0,
    AlreadyIdle = 1,
    OutcomeUnknown = 2
}

/// <summary>
/// Retained session-command authority. It owns scope, revision, candidate and
/// semantic-handoff state; the backend owns transport/provider effects.
/// </summary>
public sealed class ManagedAudioSessionAuthorityV1 :
    IAudioSessionControlAuthorityV1,
    IAudioSemanticTurnAuthorityV1,
    IAsyncDisposable
{
    private readonly IManagedAudioSessionBackendV1 _backend;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private Func<AgentInputEvent, CancellationToken, ValueTask>? _submitInput;
    private int _disposed;

    public ManagedAudioSessionAuthorityV1(IManagedAudioSessionBackendV1 backend) =>
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    internal void AttachInputDispatcher(Func<AgentInputEvent, CancellationToken, ValueTask> submitInput) =>
        _submitInput = submitInput ?? throw new ArgumentNullException(nameof(submitInput));

    public ValueTask<AudioSessionInputResult> ExecuteAsync(
        AudioSessionInputEvent input,
        AgentClientSet? clientSet,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(input);
        return input.Command switch
        {
            AudioSessionCommand.Start start => StartAsync(input, start, clientSet, cancellationToken),
            AudioSessionCommand.Update update => ExistingAsync(input, update.AudioSessionId, cancellationToken),
            AudioSessionCommand.CommitInputTurn commit => ExistingAsync(input, commit.AudioSessionId, cancellationToken),
            AudioSessionCommand.SetInputEnabled setInput => ExistingAsync(input, setInput.AudioSessionId, cancellationToken),
            AudioSessionCommand.SetOutputEnabled setOutput => ExistingAsync(input, setOutput.AudioSessionId, cancellationToken),
            AudioSessionCommand.InterruptOutput interrupt => ExistingAsync(input, interrupt.AudioSessionId, cancellationToken),
            AudioSessionCommand.Stop stop => ExistingAsync(input, stop.AudioSessionId, cancellationToken),
            _ => ValueTask.FromResult<AudioSessionInputResult>(Rejected(
                AudioSessionInputDisposition.Unsupported, "audio-command-unsupported"))
        };
    }

    private async ValueTask<AudioSessionInputResult> StartAsync(
        AudioSessionInputEvent input,
        AudioSessionCommand.Start start,
        AgentClientSet? clientSet,
        CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var scope = Scope.From(input);
            IManagedAudioSessionV1 backendSession;
            try
            {
                backendSession = await _backend.StartAsync(new ManagedAudioSessionStartRequestV1
                {
                    AgentId = scope.AgentId,
                    SessionId = scope.SessionId,
                    ThreadId = scope.ThreadId,
                    Bindings = start.Bindings ?? new AudioSessionStartBindings(),
                    ClientSet = clientSet
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return new AudioSessionInputResult.OutcomeUnknown(
                    input.ClientInputId ?? $"audio-start:{Guid.NewGuid():N}");
            }

            if (string.IsNullOrWhiteSpace(backendSession.AudioSessionId))
            {
                await backendSession.DisposeAsync().ConfigureAwait(false);
                return Rejected(AudioSessionInputDisposition.Refused, "audio-session-id-missing");
            }

            var state = new SessionState(scope, backendSession);
            if (!_sessions.TryAdd(backendSession.AudioSessionId, state))
            {
                await backendSession.DisposeAsync().ConfigureAwait(false);
                return Rejected(AudioSessionInputDisposition.Refused, "audio-session-id-conflict");
            }

            state.Pump = PumpCandidatesAsync(state);
            return new AudioSessionInputResult.Started(backendSession.AudioSessionId, state.Revision);
        }
        finally { _startGate.Release(); }
    }

    private async ValueTask<AudioSessionInputResult> ExistingAsync(
        AudioSessionInputEvent input,
        string audioSessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(audioSessionId) || !_sessions.TryGetValue(audioSessionId, out var state))
            return Rejected(AudioSessionInputDisposition.SessionNotFound, "audio-session-not-found");
        if (state.Scope != Scope.From(input))
            return Rejected(AudioSessionInputDisposition.ScopeMismatch, "audio-session-scope-mismatch", state.Revision);

        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return input.Command switch
            {
                AudioSessionCommand.Update update => Update(state, update),
                AudioSessionCommand.CommitInputTurn commit => Commit(state, commit),
                AudioSessionCommand.SetInputEnabled setInput =>
                    await SetInputAsync(state, setInput.Enabled, cancellationToken).ConfigureAwait(false),
                AudioSessionCommand.SetOutputEnabled setOutput =>
                    await SetOutputAsync(state, setOutput.Enabled, cancellationToken).ConfigureAwait(false),
                AudioSessionCommand.InterruptOutput interrupt =>
                    await InterruptAsync(state, interrupt, cancellationToken).ConfigureAwait(false),
                AudioSessionCommand.Stop stop =>
                    await StopAsync(state, stop, cancellationToken).ConfigureAwait(false),
                _ => Rejected(AudioSessionInputDisposition.Unsupported, "audio-command-unsupported", state.Revision)
            };
        }
        finally { state.Gate.Release(); }
    }

    private static AudioSessionInputResult Update(SessionState state, AudioSessionCommand.Update update)
    {
        if (update.ExpectedRevision != state.Revision)
            return Rejected(AudioSessionInputDisposition.RevisionConflict, "audio-revision-conflict", state.Revision);
        if (update.Patch.Bindings.Count != 0)
            return Rejected(AudioSessionInputDisposition.Unsupported, "audio-binding-update-requires-replacement", state.Revision);
        return new AudioSessionInputResult.UpdateEvaluated(
            state.Backend.AudioSessionId, state.Revision, state.Revision, AudioSessionUpdateDisposition.Unchanged);
    }

    private static AudioSessionInputResult Commit(SessionState state, AudioSessionCommand.CommitInputTurn commit)
    {
        if (commit.ExpectedRevision != state.Revision)
            return Rejected(AudioSessionInputDisposition.RevisionConflict, "audio-revision-conflict", state.Revision);
        if (!state.Candidates.TryGetValue(commit.CandidateId, out var candidate) || candidate.Stage != CandidateStage.Pending)
            return Rejected(AudioSessionInputDisposition.CandidateNotFound, "audio-candidate-not-found", state.Revision);

        candidate.Stage = CandidateStage.Committed;
        var revision = ++state.Revision;
        return new AudioSessionInputResult.InputTurnCommitted(state.Backend.AudioSessionId, candidate.Id, revision)
        {
            AdmittedMessage = new ChatMessage(ChatRole.User, candidate.Text),
            DurableSemanticOperationId = candidate.OperationId
        };
    }

    private static async ValueTask<AudioSessionInputResult> SetInputAsync(
        SessionState state, bool enabled, CancellationToken cancellationToken)
    {
        await state.Backend.SetInputEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
        state.InputEnabled = enabled;
        return new AudioSessionInputResult.InputStateChanged(state.Backend.AudioSessionId, ++state.Revision, enabled);
    }

    private static async ValueTask<AudioSessionInputResult> SetOutputAsync(
        SessionState state, bool enabled, CancellationToken cancellationToken)
    {
        await state.Backend.SetOutputEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
        state.OutputEnabled = enabled;
        return new AudioSessionInputResult.OutputStateChanged(state.Backend.AudioSessionId, ++state.Revision, enabled);
    }

    private static async ValueTask<AudioSessionInputResult> InterruptAsync(
        SessionState state, AudioSessionCommand.InterruptOutput command, CancellationToken cancellationToken)
    {
        if (command.ExpectedRevision != state.Revision)
            return Rejected(AudioSessionInputDisposition.RevisionConflict, "audio-revision-conflict", state.Revision);
        var result = await state.Backend.InterruptOutputAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            ManagedAudioOutputInterruptionV1.Interrupted =>
                new AudioSessionInputResult.OutputInterrupted(state.Backend.AudioSessionId, ++state.Revision, command.OperationId),
            ManagedAudioOutputInterruptionV1.AlreadyIdle =>
                new AudioSessionInputResult.OutputAlreadyIdle(state.Backend.AudioSessionId, state.Revision, command.OperationId),
            _ => new AudioSessionInputResult.OutputInterruptionUnknown(
                state.Backend.AudioSessionId, state.Revision, command.OperationId, "audio-output-interruption-unknown")
        };
    }

    private async ValueTask<AudioSessionInputResult> StopAsync(
        SessionState state, AudioSessionCommand.Stop command, CancellationToken cancellationToken)
    {
        if (state.Stopped)
            return new AudioSessionInputResult.Stopped(state.Backend.AudioSessionId, state.Revision);
        await state.Backend.StopAsync(command.Reason, cancellationToken).ConfigureAwait(false);
        state.Stopped = true;
        state.PumpStop.Cancel();
        _sessions.TryRemove(state.Backend.AudioSessionId, out _);
        await state.Backend.DisposeAsync().ConfigureAwait(false);
        return new AudioSessionInputResult.Stopped(state.Backend.AudioSessionId, ++state.Revision);
    }

    private async Task PumpCandidatesAsync(SessionState state)
    {
        try
        {
            await foreach (var value in state.Backend.ReadTranscriptCandidatesAsync(state.PumpStop.Token)
                .ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(value.CandidateId) || string.IsNullOrWhiteSpace(value.Text))
                    continue;
                var candidate = new Candidate(value.CandidateId, value.Text,
                    $"audio-semantic:{state.Backend.AudioSessionId}:{value.CandidateId}");
                if (!state.Candidates.TryAdd(candidate.Id, candidate) || !value.CommitAutomatically)
                    continue;
                var submit = _submitInput;
                if (submit is null) continue;
                await submit(new AudioSessionInputEvent
                {
                    AgentId = state.Scope.AgentId,
                    SessionId = state.Scope.SessionId,
                    ThreadId = state.Scope.ThreadId,
                    ClientInputId = $"audio-auto-commit:{state.Backend.AudioSessionId}:{candidate.Id}",
                    Command = new AudioSessionCommand.CommitInputTurn(
                        state.Backend.AudioSessionId, candidate.Id, state.Revision)
                }, state.PumpStop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (state.PumpStop.IsCancellationRequested) { }
        catch { state.PumpFaulted = true; }
    }

    public ValueTask<AudioSemanticAdmissionResult> AcceptSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken = default) =>
        SemanticAsync(audioSessionId, candidateId, CandidateStage.Committed, CandidateStage.Accepted, cancellationToken);

    public async ValueTask<AudioSemanticAdmissionResult> AcknowledgeSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken = default)
    {
        var result = await SemanticAsync(
            audioSessionId, candidateId, CandidateStage.Accepted, CandidateStage.Acknowledged, cancellationToken)
            .ConfigureAwait(false);
        if (result is AudioSemanticAdmissionResult.Accepted &&
            _sessions.TryGetValue(audioSessionId, out var state))
            state.Candidates.TryRemove(candidateId, out _);
        return result;
    }

    public async ValueTask<AudioSemanticAdmissionResult> WithdrawSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(audioSessionId, out var state))
            return new AudioSemanticAdmissionResult.Conflict("audio-session-not-found");
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!state.Candidates.TryGetValue(candidateId, out var candidate) ||
                candidate.Stage is CandidateStage.Acknowledged or CandidateStage.Withdrawn)
                return new AudioSemanticAdmissionResult.Conflict("audio-candidate-not-withdrawable");
            candidate.Stage = CandidateStage.Withdrawn;
            return new AudioSemanticAdmissionResult.Withdrawn(candidate.OperationId);
        }
        finally { state.Gate.Release(); }
    }

    private async ValueTask<AudioSemanticAdmissionResult> SemanticAsync(
        string audioSessionId, string candidateId, CandidateStage expected, CandidateStage next,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(audioSessionId, out var state))
            return new AudioSemanticAdmissionResult.Conflict("audio-session-not-found");
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!state.Candidates.TryGetValue(candidateId, out var candidate))
                return new AudioSemanticAdmissionResult.Conflict("audio-candidate-not-found");
            if (candidate.Stage == next)
                return new AudioSemanticAdmissionResult.AlreadyAccepted(candidate.OperationId);
            if (candidate.Stage != expected)
                return new AudioSemanticAdmissionResult.Conflict("audio-semantic-stage-conflict");
            candidate.Stage = next;
            return new AudioSemanticAdmissionResult.Accepted(candidate.OperationId);
        }
        finally { state.Gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var state in _sessions.Values)
        {
            state.PumpStop.Cancel();
            try { await state.Backend.StopAsync(AudioSessionStopReason.HostShutdown).ConfigureAwait(false); }
            catch { }
            await state.Backend.DisposeAsync().ConfigureAwait(false);
            state.Gate.Dispose();
            state.PumpStop.Dispose();
        }
        _sessions.Clear();
        _startGate.Dispose();
    }

    private static AudioSessionInputResult.Rejected Rejected(
        AudioSessionInputDisposition disposition, string code, long? revision = null) => new(disposition, code, revision);

    private readonly record struct Scope(string AgentId, string SessionId, string ThreadId)
    {
        internal static Scope From(AudioSessionInputEvent input) => new(
            input.AgentId ?? string.Empty,
            input.SessionId ?? string.Empty,
            input.ThreadId ?? "main");
    }

    private sealed class SessionState(Scope scope, IManagedAudioSessionV1 backend)
    {
        internal Scope Scope { get; } = scope;
        internal IManagedAudioSessionV1 Backend { get; } = backend;
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal CancellationTokenSource PumpStop { get; } = new();
        internal ConcurrentDictionary<string, Candidate> Candidates { get; } = new(StringComparer.Ordinal);
        internal Task? Pump { get; set; }
        internal long Revision { get; set; } = 1;
        internal bool InputEnabled { get; set; } = true;
        internal bool OutputEnabled { get; set; } = true;
        internal bool Stopped { get; set; }
        internal bool PumpFaulted { get; set; }
    }

    private sealed class Candidate(string id, string text, string operationId)
    {
        internal string Id { get; } = id;
        internal string Text { get; } = text;
        internal string OperationId { get; } = operationId;
        internal CandidateStage Stage { get; set; }
    }

    private enum CandidateStage { Pending, Committed, Accepted, Acknowledged, Withdrawn }
}
