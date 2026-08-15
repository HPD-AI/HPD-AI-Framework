using HPD.Base;
using HPD.Payments.Runtime.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

var services = new ServiceCollection();
services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
services.AddHPDBase(builder =>
{
    builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition
    {
        Id = "hpd.payments.base.policy", Version = 1, OwningModuleId = "hpd.payments",
        EvaluatorContractId = "hpd.payments.base.policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
    }, new AllowPolicy());
    builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
    {
        Id = "hpd.payments.owner-ledger.advance", Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.base.grants", SourceContractVersion = 1,
    }, new AccessGrant
    {
        Id = "hpd.payments.owner-ledger.advance",
        ApplicationId = "hpd.base.application", ModuleId = "hpd.payments", Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-worker", TenantId = "tenant-one" },
        Action = "hpd.payments.owner-ledger.advance", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime, TenantId = "tenant-one" },
    });
    AddSourceGrant(builder, "hpd.payments.ledger-head.source", PaymentsLedgerHead.Collection.Id);
    AddSourceGrant(builder, "hpd.payments.owner-state.source", PaymentsOwnerState.Collection.Id);
    builder.AddPaymentsModuleMutations();
});

static void AddSourceGrant(HPDBaseBuilder builder, string grantId, string collectionId) =>
    builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
    {
        Id = grantId, Version = 1, OwningModuleId = "hpd.payments",
        SourceContractId = "hpd.payments.base.grants", SourceContractVersion = 1,
    }, new AccessGrant
    {
        Id = grantId, ApplicationId = "hpd.base.application", ModuleId = "hpd.payments",
        Audience = HPDBaseEndpointAudience.ControlPlane,
        Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "payments-worker", TenantId = "tenant-one" },
        Action = collectionId,
        Scope = new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = collectionId, TenantId = "tenant-one" },
    });
using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
OperationResult<BaseApplicationReadiness> initialized = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
if (!initialized.IsSuccess()) throw new InvalidOperationException(initialized.Error?.Code);

BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "payments-worker",
    CurrentTenantId = "tenant-one",
}, options => options.Audience = HPDBaseEndpointAudience.ControlPlane);
var request = new AdvanceOwnerGenerationRequest { OwnerId = "owner-one", OperationId = "payment-one" };
BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
    "hpd.payments", "owner-ledger", "payment-one", BaseMutationRequestFingerprint.Create(new byte[32]));
BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> first =
    await PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, request, identity);
if (first is not BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>
    || first.RequireValue().Result.OwnerGeneration.ToCanonicalString() != "1"
    || first.RequireValue().Result.LedgerGeneration.ToCanonicalString() != "1")
    throw new InvalidOperationException(first is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> failure ? failure.Error.Code : "First Payments execution failed.");

BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> duplicate =
    await PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, request, identity);
if (duplicate is not BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>
    || duplicate.RequireValue().Disposition != BaseMutationRequestDisposition.Duplicate)
    throw new InvalidOperationException(duplicate is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> failure ? failure.Error.Code : "Payments replay failed.");

var secondRequest = request with
{
    OperationId = "payment-two",
    ExpectedOwnerGeneration = first.RequireValue().Result.OwnerGeneration,
    ExpectedLedgerGeneration = first.RequireValue().Result.LedgerGeneration,
};
BaseMutationRequestIdentity secondIdentity = BaseMutationRequestIdentity.Create(
    "hpd.payments", "owner-ledger", "payment-two", BaseMutationRequestFingerprint.Create(new byte[32]));
BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> second =
    await PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, secondRequest, secondIdentity);
if (second is not BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>
    || second.RequireValue().Result.OwnerGeneration.ToCanonicalString() != "2"
    || second.RequireValue().Result.LedgerGeneration.ToCanonicalString() != "2")
    throw new InvalidOperationException(second is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>> failure ? failure.Error.Code : "Guarded Payments execution failed.");

AdvanceOwnerGenerationResult generationTwo = second.RequireValue().Result;
var contenderA = secondRequest with
{
    OperationId = "payment-three-a",
    ExpectedOwnerGeneration = generationTwo.OwnerGeneration,
    ExpectedLedgerGeneration = generationTwo.LedgerGeneration,
};
var contenderB = contenderA with { OperationId = "payment-three-b" };
Task<BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>> contenderATask =
    PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, contenderA, BaseMutationRequestIdentity.Create(
        "hpd.payments", "owner-ledger", "payment-three-a", BaseMutationRequestFingerprint.Create(new byte[32]))).AsTask();
Task<BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>> contenderBTask =
    PaymentsOwnerLedgerMutationClient.ExecuteAsync(session, contenderB, BaseMutationRequestIdentity.Create(
        "hpd.payments", "owner-ledger", "payment-three-b", BaseMutationRequestFingerprint.Create(new byte[32]))).AsTask();
await Task.WhenAll(contenderATask, contenderBTask);
BaseResult<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>[] contenders =
    [await contenderATask, await contenderBTask];
if (contenders.Count(static result => result is BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>) != 1
    || contenders.Count(static result => result is BaseFailure<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>) != 1)
    throw new InvalidOperationException("Concurrent generation guards did not produce exactly one winner.");
AdvanceOwnerGenerationResult winner = contenders
    .OfType<BaseSuccess<BaseModuleMutationExecutionResult<AdvanceOwnerGenerationResult>>>()
    .Single().Value.Result;
if (winner.OwnerGeneration.ToCanonicalString() != "3" || winner.LedgerGeneration.ToCanonicalString() != "3")
    throw new InvalidOperationException("Concurrent generation winner did not publish generation three.");

Console.WriteLine("Payments L50 integration passed: two private records, two guarded cells, replay, contention, and opaque generations.");

sealed class AllowPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
}
