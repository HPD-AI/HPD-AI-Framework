using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Testing;

internal static class BaseLogicalIndexCertificationHost
{
    internal const string ApplicationId = "base.cert.logicalIndex";
    internal const string GrantId = "base.cert.logicalIndex.mutate";
    internal const string ProfileId = "base.cert.logicalIndex.patch.v1";

    internal static ServiceProvider Create(
        HPDBaseStoreProvider provider,
        bool constrainToTenantA = false,
        IPolicyEvaluator? certificationEvaluator = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            HPDBaseBuilder configured = builder.ConfigureSchema(options =>
                {
                    options.ApplicationId = ApplicationId;
                    options.PlanProtectionKey = Enumerable.Repeat((byte)0x80, 32).ToArray();
                })
                .ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
                {
                    HostMaxima = Limits(),
                    MaximumReceiptIdentityBytes = 512,
                    MaximumEvidenceTokenBytes = 512,
                    MaximumRouteNameBytes = 96,
                    MaximumRequestBodyBytes = 1_048_576,
                });
            BasePolicyAuthorityDefinition authority = new()
                {
                    Id = constrainToTenantA
                        ? "base.cert.logicalIndex.tenantA"
                        : "base.cert.logicalIndex.allow",
                    Version = 1,
                    OwningModuleId = ApplicationId,
                    EvaluatorContractId = constrainToTenantA
                        ? "base.cert.logicalIndex.tenantA.v1"
                        : "base.cert.logicalIndex.allow.v1",
                    EvaluatorContractVersion = 1,
                    CompositionOrder = 0,
                };
            if (certificationEvaluator is not null)
                configured.AddPolicyAuthority(authority, certificationEvaluator);
            else if (constrainToTenantA)
                configured.AddPolicyAuthority<TenantA>(authority);
            else
                configured.AddPolicyAuthority<AllowAll>(authority);
            configured
                .AddCollection(BaseLogicalIndexCertificationItem.Collection)
                .AddSelectionOperationProfile(Profile())
                .UseStore(provider);
            builder.PolicyAuthority.AddStaticGrant(new BaseGrantAuthorityDefinition
            {
                Id = GrantId,
                Version = 1,
                OwningModuleId = ApplicationId,
                SourceContractId = "base.cert.logicalIndex.grants.v1",
                SourceContractVersion = 1,
            }, new AccessGrant
            {
                Id = GrantId,
                Subject = new AccessSubject { Kind = AccessSubjectKind.User },
                Action = "selectionMutation",
                Scope = new ResourceScope
                {
                    Kind = ResourceScopeKind.Collection,
                    CollectionId = BaseLogicalIndexCertificationItem.Collection.Id,
                },
            });
        });
        return services.BuildServiceProvider();
    }

    internal static async ValueTask<BaseCollectionSession<BaseLogicalIndexCertificationItem>>
        InitializeAsync(ServiceProvider provider, CancellationToken cancellationToken = default)
    {
        OperationResult<BaseApplicationReadiness> initialized = await provider
            .GetRequiredService<IHPDBaseApplication>().InitializeAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!initialized.IsSuccess())
            throw new InvalidOperationException(initialized.Error?.Code ?? "base.logicalIndex.certificationInvalid");
        return provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Admin,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "base-certification",
        }).Collection(BaseLogicalIndexCertificationItem.Collection);
    }

    internal static BaseGeneratedSelectionProfileIdentity ProfileIdentity()
    {
        BaseSelectionOperationProfile profile = Profile();
        return BaseGeneratedSelectionProfiles.RegisterSelectionProfile(
            BaseGeneratedModules.RegisterCollectionModule(profile.ApplicationId, profile.CollectionId),
            new BaseGeneratedSelectionProfileDescriptor
            {
                ApplicationId = profile.ApplicationId,
                CollectionId = profile.CollectionId,
                ProfileId = profile.Id,
                Version = profile.Version,
                Kind = profile.MutationKind,
                Checksum = BaseSelectionProfileChecksum.Compute(profile),
            });
    }

    internal static RecordPatchRequest SequencePatch(long sequence)
    {
        string wire = BaseLogicalIndexCertificationItem.Collection.Definition.Fields!
            .Single(field => field.Id == "base.cert.logicalIndex.sequence").WireName;
        return new RecordPatchRequest
        {
            Patch = new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [wire] = JsonSerializer.SerializeToElement(
                        sequence, BaseLogicalIndexCertificationJsonContext.Default.Int64),
                },
            },
            RemovedFieldIds = [],
        };
    }

    internal static BaseMutationRequestIdentity Identity(string caseId) =>
        BaseMutationRequestIdentity.Create(
            ApplicationId,
            ProfileId,
            caseId,
            BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(caseId))));

    internal static BaseSelectionOperationLimits Limits() => new()
    {
        MaximumQueryNodes = 16,
        MaximumQueryDepth = 4,
        MaximumLiteralValues = 8,
        MaximumSelectedRecords = 4,
        MaximumSelectedBytes = 16_384,
        MaximumProducedMutations = 4,
        MaximumQueryExecutions = 1,
        MaximumReadIntervals = 2,
        MaximumWrittenBytes = 16_384,
        MaximumFactBytes = 16_384,
        MaximumJournalBytes = 32_768,
        MaximumReceiptBytes = 32_768,
        MaximumRelationChecks = 1,
        MaximumUniqueConstraintChecks = 16,
        MaximumPreviousStateRequirements = 1,
        MaximumTransientBytes = 65_536,
        MaximumResultBytes = 4_096,
        AcquisitionTimeout = TimeSpan.FromSeconds(1),
        ExecutionTimeout = TimeSpan.FromSeconds(1),
        CallerCommitObservationTimeout = TimeSpan.FromSeconds(1),
    };

    internal static BaseSelectionOperationProfile Profile() => new()
    {
        Id = ProfileId,
        Version = 1,
        ApplicationId = ApplicationId,
        CollectionId = BaseLogicalIndexCertificationItem.Collection.Id,
        RequiredGrantId = GrantId,
        MutationKind = BaseSelectionMutationKind.MergePatch,
        Limits = Limits(),
    };

    private sealed class AllowAll : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo { MatchedGrantIds = [GrantId] },
            });
    }

    private sealed class TenantA : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new PolicyDecision
                {
                    Effect = PolicyEffect.Allow,
                    Outcome = PolicyOutcome.Allowed,
                    Audit = new PolicyAuditInfo { MatchedGrantIds = [GrantId] },
                }.WithRecordFilter(new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "base.cert.logicalIndex.tenant",
                    Operator = FilterOperator.Equal,
                    Value = BaseQueryValue.From("a"),
                }));
    }
}
