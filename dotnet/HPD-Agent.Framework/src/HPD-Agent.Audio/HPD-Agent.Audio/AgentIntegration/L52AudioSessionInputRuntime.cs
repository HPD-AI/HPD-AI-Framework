namespace HPD.Agent.Audio;

/// <summary>
/// Owns the package boundary between Agent input dispatch and the current L52A
/// Audio authority. It deliberately retains no parallel session state.
/// </summary>
internal sealed class L52AudioSessionInputRuntime(
    IAudioSessionControlAuthorityV1 authority) : IAudioSessionInputRuntime
{
    private readonly IAudioSessionControlAuthorityV1 _authority =
        authority ?? throw new ArgumentNullException(nameof(authority));

    public ValueTask<AudioSessionInputResult> ExecuteAsync(
        AudioSessionInputEvent input,
        AgentClientSet? clientSet,
        CancellationToken cancellationToken) =>
        _authority.ExecuteAsync(input, clientSet, cancellationToken);

    public ValueTask<AudioSemanticAdmissionResult> AcceptSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken) =>
        Semantic().AcceptSemanticAsync(audioSessionId, candidateId, cancellationToken);

    public ValueTask<AudioSemanticAdmissionResult> AcknowledgeSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken) =>
        Semantic().AcknowledgeSemanticAsync(audioSessionId, candidateId, cancellationToken);

    public ValueTask<AudioSemanticAdmissionResult> WithdrawSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken) =>
        Semantic().WithdrawSemanticAsync(audioSessionId, candidateId, cancellationToken);

    private IAudioSemanticTurnAuthorityV1 Semantic() =>
        _authority as IAudioSemanticTurnAuthorityV1
        ?? throw new InvalidOperationException(
            "The installed Audio-session authority does not support semantic turn admission.");
}

/// <summary>
/// Current-authority SPI for retained Audio-session commands. Implementations
/// own lifecycle truth, revisions, effects, reconciliation, and typed results.
/// </summary>
public interface IAudioSessionControlAuthorityV1
{
    ValueTask<AudioSessionInputResult> ExecuteAsync(
        AudioSessionInputEvent input,
        AgentClientSet? clientSet,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional S4 semantic admission surface implemented by a session authority.</summary>
public interface IAudioSemanticTurnAuthorityV1
{
    ValueTask<AudioSemanticAdmissionResult> AcceptSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken = default);
    ValueTask<AudioSemanticAdmissionResult> AcknowledgeSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken = default);
    ValueTask<AudioSemanticAdmissionResult> WithdrawSemanticAsync(
        string audioSessionId, string candidateId, CancellationToken cancellationToken = default);
}
