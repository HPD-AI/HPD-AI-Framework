using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace HPD.Base.AspNetCore.Tests.Endpoints;

public sealed class ModuleMutationEndpointTests
{
    [Fact]
    public async Task System_endpoint_executes_and_replays_exact_generated_contract()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("control", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IBaseHttpPrincipalMapper, SystemMapper>();
        builder.Services.AddHPDBase(hpd =>
        {
            hpd.ConfigureSchema(options => options.ApplicationId = "module.application")
                .ConfigureInMemoryStore(options => { options.StoreId = "module-store"; options.Collections = []; })
                .AddAspNetCore()
                .AddPolicyAuthority(new BasePolicyAuthorityDefinition
            {
                Id = "module.policy", Version = 1, OwningModuleId = "module",
                EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
            }, new AllowPolicyEvaluator());
            hpd.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
            {
                Id = "module.increment", Version = 1, OwningModuleId = "module",
                SourceContractId = "module.grants", SourceContractVersion = 1,
            }, new AccessGrant
            {
                Id = "module.increment", ApplicationId = "module.application", ModuleId = "module",
                Audience = HPDBaseEndpointAudience.Application,
                Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
                Action = "module.increment", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
            });
            hpd.AddModuleGenerationCell(ModuleMutationEndpointFixture.Cell())
                .AddModuleMutation(ModuleIncrement.Definition, ModuleIncrement.Identity);
        });
        await using WebApplication app = builder.Build();
        RouteGroupBuilder control = app.MapGroup("/base").RequireAuthorization("control");
        control.MapHPDBaseControlPlaneEndpoints(app, new HPDBaseControlPlaneEndpointSelection
        {
            MapRecords = false, MapRegisteredReads = false, MapAdministration = true,
        }, (endpoint, _) => endpoint.RequireAuthorization("control"));
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        using var firstRequest = Request("one", "{}");
        using var duplicateRequest = Request("one", "{ }");
        using HttpResponseMessage first = await client.SendAsync(firstRequest);
        using HttpResponseMessage duplicate = await client.SendAsync(duplicateRequest);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        string firstBody = await first.Content.ReadAsStringAsync();
        string duplicateBody = await duplicate.Content.ReadAsStringAsync();
        firstBody.Should().Be("{\"disposition\":\"new\",\"outcome\":\"committed\",\"result\":{\"Generation\":\"1\"}}");
        duplicateBody.Should().Be("{\"disposition\":\"duplicate\",\"outcome\":\"duplicate\",\"result\":{\"Generation\":\"1\"}}");

        using HttpResponseMessage explicitNull = await client.SendAsync(Request("null", "{\"OptionalAt\":null}"));
        explicitNull.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using HttpResponseMessage present = await client.SendAsync(Request("two",
            "{\"OptionalNumber\":7,\"OptionalAt\":\"2026-08-27T00:00:00.0000000Z\"}"));
        using HttpResponseMessage reordered = await client.SendAsync(Request("two",
            "{ \"OptionalAt\" : \"2026-08-27T00:00:00.0000000Z\", \"OptionalNumber\" : 7 }"));
        present.StatusCode.Should().Be(HttpStatusCode.OK);
        reordered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await present.Content.ReadAsStringAsync()).Should().Contain("\"Generation\":\"2\"");
        (await reordered.Content.ReadAsStringAsync()).Should().Contain("\"disposition\":\"duplicate\"");

        using HttpResponseMessage changedFingerprint = await client.SendAsync(Request("two",
            "{\"OptionalNumber\":8,\"OptionalAt\":\"2026-08-27T00:00:00.0000000Z\"}"));
        changedFingerprint.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using HttpResponseMessage malformed = await client.PostAsync(
            "/base/module-mutations/v1/module.increment:execute", new StringContent("[]", Encoding.UTF8, "application/json"));
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpRequestMessage Request(string key, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/base/module-mutations/v1/module.increment:execute")
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add(BaseHttpHeaders.IdempotencyKey, key);
        return request;
    }

    private sealed class SystemMapper : IBaseHttpPrincipalMapper
    {
        public ValueTask<PrincipalContext> MapAsync(HttpContext context, HPDBaseEndpointDescriptor endpoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" });
    }
    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }

}

internal static class ModuleMutationEndpointFixture
{
    internal static BaseModuleGenerationCellDefinition Cell() => new()
    {
        Id = "module.generation", Version = 1, OwningModuleId = "module", Scope = BaseModuleGenerationScope.Application,
        MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
    };

    internal static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8, MaximumGenerationCaptures = 8, MaximumRecordMutations = 8,
        MaximumGenerationReads = 8, MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8, MaximumGuardNodes = 8, MaximumGuardDepth = 8,
        MaximumPreconditions = 8, MaximumRequestGuardEvaluations = 16, MaximumStaticSetMembers = 16, MaximumStaticSetComparisons = 120, MaximumDisabledCaptures = 8, MaximumRemovedFields = 8,
        MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32, MaximumReadIntervals = 16, MaximumSubjectValidations = 8,
        MaximumAuthorityReads = 16, MaximumRelationChecks = 8, MaximumUniqueConstraintChecks = 8, MaximumRequestBytes = 4096,
        MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 4096, MaximumWrittenBytes = 4096,
        MaximumFactBytes = 4096, MaximumJournalBytes = 4096, MaximumReceiptBytes = 4096, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines { AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5), CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5) },
    };
}

[BaseRegisteredModuleMutation("module.increment", typeof(ModuleMutationJsonContext), typeof(ModuleIncrementRequest), typeof(ModuleIncrementResult), Version = 1, OwningModuleId = "module", GrantId = "module.increment")]
public static partial class ModuleIncrement
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
            Id = "module.increment", Version = 1, OwningModuleId = "module", GrantId = "module.increment",
            Audience = BaseModuleMutationAudience.System, RequestTypeId = "module.increment.request", ResultTypeId = "module.increment.result",
            SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = ["module.generation"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [new BaseModuleGenerationCapture { Id = "generation", CellId = "module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }],
                Guards = [], Preconditions = [], Body = new BaseModuleMutationBlock { Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }] },
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.ResultObject<ModuleIncrementResult>("result",
                        BaseModuleMutationTemplateBuilder.Property(
                            ResultProperties.Generation,
                            BaseModuleMutationTemplateBuilder.ResultingGeneration("result-generation", "generation")))),
            },
            Limits = ModuleMutationEndpointFixture.Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    });
}

public sealed record ModuleIncrementRequest
{
    [BaseField("module.request.optional-at", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public DateTimeOffset? OptionalAt { get; init; }
    [BaseField("module.request.optional-number", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)]
    public int? OptionalNumber { get; init; }
}
public sealed record ModuleIncrementResult { [BaseField("module.result.generation")] public required BaseModuleGeneration Generation { get; init; } }
[JsonSerializable(typeof(ModuleIncrementRequest))]
[JsonSerializable(typeof(ModuleIncrementResult))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class ModuleMutationJsonContext : JsonSerializerContext;
