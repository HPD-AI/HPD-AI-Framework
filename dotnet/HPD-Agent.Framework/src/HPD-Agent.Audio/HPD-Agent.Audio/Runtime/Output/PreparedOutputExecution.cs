using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal sealed class PreparedOutputExecution
{
    private readonly LiveAudioOutputGeneration _generation;

    internal PreparedOutputExecution(LiveAudioOutputGeneration generation, OutputOriginEvidence origin)
    {
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        var providerPlan = origin.Provider.Plan ??
            throw new ArgumentException("Output origin requires an effective provider plan.", nameof(origin));
        if (!SameAuthority(generation.Authority, origin.Decision.Authority) ||
            !SameAuthority(generation.Authority, providerPlan.Authority))
            throw new ArgumentException("Output origin must be effective under the prepared output authority.", nameof(origin));
    }

    internal OutputOriginEvidence Origin { get; }
    internal ExpectedAuthorityVectorV1 Authority => _generation.Authority;
    internal OutputGenerationId OutputGeneration => _generation.OutputGeneration;

    internal LiveAudioOutputActivationResult Activate(
        OperationId operationId,
        long maximumUnits,
        Hash256 contentFingerprint) =>
        _generation.Activate(new OutputOffer(
            operationId,
            OutputGeneration,
            maximumUnits,
            contentFingerprint,
            Origin));

    private static bool SameAuthority(ExpectedAuthorityVectorV1 left, ExpectedAuthorityVectorV1 right) =>
        left.Session == right.Session && left.Axes.SequenceEqual(right.Axes);
}
