using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.AotSmoke;
using Microsoft.Extensions.DependencyInjection;

var collection = AotProject.Collection;
var value = new AotProject
{
    OrganizationId = "org_aot",
    Name = "AOT",
};

string json = JsonSerializer.Serialize(value, collection.JsonTypeInfo);
var roundTrip = JsonSerializer.Deserialize(json, collection.JsonTypeInfo);

if (roundTrip is null ||
    roundTrip.OrganizationId != "org_aot" ||
    AotProject.Fields.OrganizationId.Id != "organization-id" ||
    AotProject.Fields.OrganizationId.WireName != "organizationId" ||
    !AotProject.Fields.Name.Operators.HasFlag(BaseFieldOperator.Order))
{
    throw new InvalidOperationException(
        "Generated application contracts must survive Native AOT.");
}

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IPolicyEvaluator, AotAllowPolicyEvaluator>();
services.AddHPDBase(hpd => hpd.AddCollection(collection));
using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateOnBuild = true });
if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess())
    throw new InvalidOperationException("InMemory application initialization failed.");
var session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.System,
    SubjectId = "aot"
});
BaseCollectionSession<AotProject> projects = session.Collection(collection);
if (provider.GetRequiredService<HPDBaseInstalledFeatures>().Provider != "inmemory"
    || provider.GetRequiredService<IRecordStore>().Capabilities.StoreKind != BaseStoreKinds.InMemory)
{
    throw new InvalidOperationException(
        "The built-in InMemory provider must be the Native AOT-safe default.");
}

BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
    "aot", "create-project", "request-1",
    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-request"u8)));
BaseBatchBuilder initial = session.Atomic(identity);
initial.Create(collection, new RecordId("project-1"), value);
BaseBatchBuilder retry = session.Atomic(identity);
retry.Create(collection, new RecordId("project-1"), value);
BaseResult<BaseBatchResult> committed = await initial.CommitAsync();
BaseResult<BaseBatchResult> duplicate = await retry.CommitAsync();
if (committed is not BaseSuccess<BaseBatchResult> committedSuccess
    || committedSuccess.Value.RequestDisposition != BaseMutationRequestDisposition.Committed
    || duplicate is not BaseSuccess<BaseBatchResult> duplicateSuccess
    || duplicateSuccess.Value.RequestDisposition != BaseMutationRequestDisposition.Duplicate)
{
    throw new InvalidOperationException("InMemory identified request replay failed.");
}
_ = (await projects.CreateAsync(new RecordId("project-2"), value with { Name = "AOT 2" })).RequireValue();
BasePage<BaseRecord<AotProject>> firstPage = (await projects.Query()
    .OrderBy(AotProject.Fields.Name)
    .Take(1)
    .PageAsync()).RequireValue();
if (firstPage.Page.NextCursor is null)
    throw new InvalidOperationException("InMemory opaque cursor continuation failed.");

namespace HPD.Base.AotSmoke
{
    [BaseCollection("aot.projects", typeof(AotApplicationJsonContext))]
    internal sealed partial record AotProject
    {
        [BaseField("organization-id")]
        public required string OrganizationId { get; init; }

        [BaseField("name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
        public required string Name { get; init; }
    }

    [JsonSerializable(typeof(AotProject))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal sealed partial class AotApplicationJsonContext : JsonSerializerContext;

    internal sealed class AotAllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
            });
        }
    }
}
