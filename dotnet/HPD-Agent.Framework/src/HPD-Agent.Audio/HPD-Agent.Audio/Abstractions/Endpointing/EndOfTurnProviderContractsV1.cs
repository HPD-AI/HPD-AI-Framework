using HPD.Agent.Providers;

namespace HPD.Agent.Audio.ProviderContracts.EndOfTurn;

/// <summary>Creates semantic end-of-turn detectors through the uniform asynchronous provider contract.</summary>
public interface IEndOfTurnDetectorProviderV1 : IProvider, IProviderClientFactory<IEndOfTurnDetectorV1>;

/// <summary>Evaluates whether a stable transcript represents a semantic end of turn.</summary>
public interface IEndOfTurnDetectorV1
{
    /// <summary>Evaluates one immutable transcript snapshot.</summary>
    /// <param name="request">The transcript and optional language metadata.</param>
    /// <param name="cancellationToken">A token that cancels the provider operation.</param>
    /// <returns>A semantic completion assessment that contains no credential state.</returns>
    ValueTask<EndOfTurnDetectionResultV1> DetectAsync(
        EndOfTurnDetectionRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>Contains immutable input for one semantic end-of-turn evaluation.</summary>
public sealed record EndOfTurnDetectionRequestV1
{
    /// <summary>Gets the stable transcript text to evaluate.</summary>
    public required string Transcript { get; init; }
    /// <summary>Gets the optional BCP-47 language tag.</summary>
    public string? Language { get; init; }
}

/// <summary>Contains a provider-neutral semantic end-of-turn assessment.</summary>
public sealed record EndOfTurnDetectionResultV1
{
    /// <summary>Gets whether the transcript is a complete turn candidate.</summary>
    public required bool IsEndOfTurn { get; init; }
    /// <summary>Gets optional normalized confidence in the inclusive range zero through one.</summary>
    public double? Confidence { get; init; }
    /// <summary>Gets a non-secret provider diagnostic category when available.</summary>
    public string? DiagnosticCode { get; init; }
}
