using HPD.Agent.Authority;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;

namespace HPD.Agent.Audio.V2.Tests;

internal static class PreparedOutputExecutionTestFixture
{
    internal static ProviderClientExecutionIdentity ChatClientIdentity => new()
    {
        ProviderKey = "test",
        BackendKey = "local",
        Family = ProviderClientFamily.Chat,
        ModelName = "test-model",
        OperationAdapterKey = "test/chat",
        UsageSemanticsKey = "test",
        SafeConfigurationFingerprint = "test-fixture"
    };

    internal static AgentEventComposition EventComposition => AgentEventComposition.Create([
        CoreAgentEventModule.Fragment,
        GeneratedAgentEventModule_HPD_Agent_Audio_1448db00.Fragment
    ]);

    internal static PreparedOutputExecutionV2 Create()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var turn = TurnGenerationId.Create();
        var provider = ProviderGenerationId.Create();
        var route = RouteGenerationId.Create();
        var output = OutputGenerationId.Create();
        var authority = ExpectedAuthorityVectorV1.Create(session,
        [
            new AuthorityAxisValueV1.Turn(turn),
            new AuthorityAxisValueV1.Provider(provider),
            new AuthorityAxisValueV1.Route(route),
            new AuthorityAxisValueV1.Output(output)
        ]);
        var decision = new TurnDecisionFinalizedV1(
            OperationId.Create(), new JournalPositionV1(session, 1), authority, 1);
        var plan = new ProviderParticipantPlanV1(
            ParticipantId.Create(), ProviderId.Create(), provider, route, authority,
            Hash256.Compute([1]), 8);
        var origin = new OutputOriginEvidenceV2(decision,
            new ProviderParticipantSnapshotV1(1, ProviderParticipantPhaseV1.Effective, plan, 0, null));
        return new PreparedOutputExecutionV2(new LiveAudioOutputGenerationV2(authority), origin);
    }
}
