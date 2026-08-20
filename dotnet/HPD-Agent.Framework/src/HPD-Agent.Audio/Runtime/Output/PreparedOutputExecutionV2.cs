using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal sealed class PreparedOutputExecutionV2
{
    private readonly LiveAudioOutputGenerationV2 _generation;

    internal PreparedOutputExecutionV2(LiveAudioOutputGenerationV2 generation, OutputOriginEvidenceV2 origin)
    {
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        var providerPlan = origin.Provider.Plan ??
            throw new ArgumentException("Output origin requires an effective provider plan.", nameof(origin));
        if (!SameAuthority(generation.Authority, origin.Decision.Authority) ||
            !SameAuthority(generation.Authority, providerPlan.Authority))
            throw new ArgumentException("Output origin must be effective under the prepared output authority.", nameof(origin));
    }

    internal OutputOriginEvidenceV2 Origin { get; }
    internal ExpectedAuthorityVectorV1 Authority => _generation.Authority;
    internal OutputGenerationId OutputGeneration => _generation.OutputGeneration;

    internal LiveAudioOutputActivationResultV2 Activate(
        OperationId operationId,
        long maximumUnits,
        Hash256 contentFingerprint) =>
        _generation.Activate(new OutputOfferV2(
            operationId,
            OutputGeneration,
            maximumUnits,
            contentFingerprint,
            Origin));

    private static bool SameAuthority(ExpectedAuthorityVectorV1 left, ExpectedAuthorityVectorV1 right) =>
        left.Session == right.Session && left.Axes.SequenceEqual(right.Axes);
}
