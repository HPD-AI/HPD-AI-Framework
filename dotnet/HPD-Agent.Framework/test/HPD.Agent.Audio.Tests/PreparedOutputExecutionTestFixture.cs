using HPD.Agent.Authority;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;

namespace HPD.Agent.Audio.Tests;

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
        HPD_Agent_AudioGeneratedAgentEventModule.Fragment
    ]);

    internal static PreparedOutputExecution Create()
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
        var decision = new TurnDecisionFinalized(
            OperationId.Create(), new JournalPositionV1(session, 1), authority, 1);
        var plan = new ProviderParticipantPlan(
            ParticipantId.Create(), ProviderId.Create(), provider, route, authority,
            Hash256.Compute([1]), 8);
        var origin = new OutputOriginEvidence(decision,
            new ProviderParticipantSnapshot(1, ProviderParticipantPhase.Effective, plan, 0, null));
        return new PreparedOutputExecution(new LiveAudioOutputGeneration(authority), origin);
    }
}
