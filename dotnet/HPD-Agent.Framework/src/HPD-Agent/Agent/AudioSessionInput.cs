using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public sealed record AudioSessionInputEvent : AgentInputEvent
{
    public required AudioSessionCommand Command { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AudioSessionCommand.Start), "start")]
[JsonDerivedType(typeof(AudioSessionCommand.Update), "update")]
[JsonDerivedType(typeof(AudioSessionCommand.CommitInputTurn), "commitInputTurn")]
[JsonDerivedType(typeof(AudioSessionCommand.SetInputEnabled), "setInputEnabled")]
[JsonDerivedType(typeof(AudioSessionCommand.SetOutputEnabled), "setOutputEnabled")]
[JsonDerivedType(typeof(AudioSessionCommand.InterruptOutput), "interruptOutput")]
[JsonDerivedType(typeof(AudioSessionCommand.Stop), "stop")]
public abstract record AudioSessionCommand
{
    private AudioSessionCommand() { }

    public sealed record Start(AudioSessionStartBindings? Bindings = null) : AudioSessionCommand;

    public sealed record Update(
        string AudioSessionId,
        long ExpectedRevision,
        AudioSessionConfigPatch Patch) : AudioSessionCommand;

    public sealed record CommitInputTurn(
        string AudioSessionId,
        string CandidateId,
        long ExpectedRevision) : AudioSessionCommand;

    public sealed record SetInputEnabled(string AudioSessionId, bool Enabled) : AudioSessionCommand;

    public sealed record SetOutputEnabled(string AudioSessionId, bool Enabled) : AudioSessionCommand;

    /// <summary>Requests interruption of the exact active output generation for this session.</summary>
    public sealed record InterruptOutput(
        string AudioSessionId,
        long ExpectedRevision,
        string OperationId) : AudioSessionCommand;

    public sealed record Stop(
        string AudioSessionId,
        AudioSessionStopReason Reason = AudioSessionStopReason.UserRequested) : AudioSessionCommand;
}

public sealed record AudioSessionStartBindings
{
    public IReadOnlyList<AudioSessionStartBinding> Bindings { get; init; } = [];
}

public sealed record AudioSessionStartBinding
{
    public required string ComponentInstance { get; init; }
    public required string Schema { get; init; }
    public required uint Version { get; init; }
    public required JsonElement Value { get; init; }
}

/// <summary>Schema-bound immutable participant updates for a retained Audio session.</summary>
public sealed record AudioSessionConfigPatch
{
    public IReadOnlyList<AudioSessionStartBinding> Bindings { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<AudioSessionStopReason>))]
public enum AudioSessionStopReason
{
    UserRequested = 0,
    TransportEnded = 1,
    PolicyRevoked = 2,
    HostShutdown = 3
}

public abstract record AudioSessionInputResult
{
    private AudioSessionInputResult() { }

    public sealed record Started(string AudioSessionId, long Revision) : AudioSessionInputResult;

    public sealed record UpdateEvaluated(
        string AudioSessionId,
        long PreviousRevision,
        long CurrentRevision,
        AudioSessionUpdateDisposition Disposition) : AudioSessionInputResult;

    public sealed record InputTurnCommitted(
        string AudioSessionId,
        string CandidateId,
        long Revision) : AudioSessionInputResult
    {
        private ChatMessage? _admittedMessage;

        internal ChatMessage? AdmittedMessage
        {
            init => _admittedMessage = value;
        }

        internal string? DurableSemanticOperationId { get; init; }

        internal ChatMessage? TryTakeAdmittedMessage()
            => Interlocked.Exchange(ref _admittedMessage, null);
    }

    public sealed record InputTurnDiscarded(
        string AudioSessionId,
        string CandidateId,
        long Revision,
        string SafeCode) : AudioSessionInputResult;

    public sealed record InputStateChanged(
        string AudioSessionId,
        long Revision,
        bool Enabled) : AudioSessionInputResult;

    public sealed record OutputStateChanged(
        string AudioSessionId,
        long Revision,
        bool Enabled) : AudioSessionInputResult;

    public sealed record OutputInterrupted(
        string AudioSessionId,
        long Revision,
        string OperationId) : AudioSessionInputResult;

    public sealed record OutputAlreadyIdle(
        string AudioSessionId,
        long Revision,
        string OperationId) : AudioSessionInputResult;

    public sealed record OutputInterruptionUnknown(
        string AudioSessionId,
        long Revision,
        string OperationId,
        string SafeCode) : AudioSessionInputResult;

    public sealed record Stopped(string AudioSessionId, long Revision) : AudioSessionInputResult;

    public sealed record Rejected(
        AudioSessionInputDisposition Disposition,
        string SafeCode,
        long? CurrentRevision = null) : AudioSessionInputResult;

    public sealed record OutcomeUnknown(
        string OperationId,
        string? AudioSessionId = null,
        long? CurrentRevision = null) : AudioSessionInputResult;
}

[JsonConverter(typeof(JsonStringEnumConverter<AudioSessionInputDisposition>))]
public enum AudioSessionInputDisposition
{
    RevisionConflict = 0,
    CapabilityNotInstalled = 1,
    SessionNotFound = 2,
    ScopeMismatch = 3,
    CandidateNotFound = 4,
    Unauthorized = 5,
    Unsupported = 6,
    Refused = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<AudioSessionUpdateDisposition>))]
public enum AudioSessionUpdateDisposition
{
    Unchanged = 0,
    Applied = 1
}

internal interface IAudioSessionInputRuntime
{
    ValueTask<AudioSessionInputResult> ExecuteAsync(
        AudioSessionInputEvent input,
        AgentClientSet? clientSet,
        CancellationToken cancellationToken);

    ValueTask<AudioSemanticAdmissionResult> AcceptSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken);
    ValueTask<AudioSemanticAdmissionResult> AcknowledgeSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken);
    ValueTask<AudioSemanticAdmissionResult> WithdrawSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken);
}

public abstract record AudioSemanticAdmissionResult
{
    private AudioSemanticAdmissionResult() { }
    public sealed record Accepted(string OperationId) : AudioSemanticAdmissionResult;
    public sealed record AlreadyAccepted(string OperationId) : AudioSemanticAdmissionResult;
    public sealed record Withdrawn(string OperationId) : AudioSemanticAdmissionResult;
    public sealed record Conflict(string SafeCode) : AudioSemanticAdmissionResult;
    public sealed record OutcomeUnknown(string SafeCode) : AudioSemanticAdmissionResult;
}
