using System.Text.Json;
using HPD.Base;
using HPD.Base.Tests.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Policy.Admin;

public sealed class BasePolicyExplainServiceTests
{
    [Fact]
    public async Task ExplainAsync_DeniesAnonymousBeforePolicyEvaluation()
    {
        using var provider = OperationTestServices.Build();
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Unauthorized, result.Status);
        Assert.Equal("base.policyExplain.unauthorized", result.Error!.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ExplainAsync_DeniesAuthenticatedNonAdmin()
    {
        using var provider = OperationTestServices.Build();
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated },
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.policyExplain.adminRequired", result.Error!.Code);
    }

    [Fact]
    public async Task ExplainAsync_DeniesServicePrincipalByDefault()
    {
        using var provider = OperationTestServices.Build();
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Service },
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.policyExplain.adminRequired", result.Error!.Code);
    }

    [Fact]
    public async Task ExplainAsync_AllowsServicePrincipalWithExplicitOption()
    {
        using var provider = OperationTestServices.Build(
            store: new FakeRecordStore("memory"),
            configureServices: services => services.AddSingleton<IOptions<HPDBasePolicyAdminOptions>>(
                Options.Create(new HPDBasePolicyAdminOptions { AllowServicePrincipalExplain = true })));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Service },
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Allowed, result.Value!.Outcome);
    }

    [Fact]
    public async Task ExplainAsync_AllowsSystemPrincipal()
    {
        using var provider = OperationTestServices.Build(store: new FakeRecordStore("memory"));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System },
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.System });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Allowed, result.Value!.Outcome);
    }

    [Fact]
    public async Task ExplainAsync_AllowsAdminAndReturnsSafeDecisionProjection()
    {
        using var provider = OperationTestServices.Build(store: new FakeRecordStore("memory"), policy: new AuditedAllowPolicyEvaluator());
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin, CorrelationId = "corr_1" });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Allowed, result.Value!.Outcome);
        Assert.Equal("test.policy", result.Value.Decision!.EvaluatorId);
        Assert.Equal(["Role", "User"], result.Value.Decision.MatchedSubjectKinds ?? []);
        Assert.Equal(["grant_1"], result.Value.Decision.MatchedGrantRefs ?? []);
        Assert.Equal("corr_1", result.Value.CorrelationId);
    }

    [Fact]
    public async Task ExplainAsync_ReturnsDeniedTargetOutcomeAsSuccessfulExplain()
    {
        using var provider = OperationTestServices.Build(store: new FakeRecordStore("memory"), policy: new DenyPolicyEvaluator());
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Denied, result.Value!.Outcome);
        Assert.Equal(PolicyEffect.Deny, result.Value.Decision!.Effect);
        Assert.Equal("denied", result.Value.Decision.ReasonCode);
    }

    [Fact]
    public async Task ExplainAsync_NoPolicyEvaluatorFailsClosed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        var store = new FakeRecordStore("memory");
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = store.Capabilities.StoreId,
            Store = store,
            CollectionIds = ["items"]
        });

        var service = provider.GetRequiredService<IBasePolicyExplainService>();
        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.runtime.policy.unavailable", result.Error!.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ExplainAsync_AbstainDeniesTargetByDefault()
    {
        using var provider = OperationTestServices.Build(store: new FakeRecordStore("memory"), policy: new AbstainPolicyEvaluator());
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Denied, result.Value!.Outcome);
        Assert.Equal("abstain", result.Value.Decision!.ReasonCode);
    }

    [Fact]
    public async Task ExplainAsync_AbstainAllowedWhenRuntimeOptionAllowsIt()
    {
        using var provider = BuildDirect(
            new FakeRecordStore("memory"),
            new AbstainPolicyEvaluator(),
            configureRuntimeBuilder: builder => builder.UseDevelopmentPolicyAbstainAsAllow());
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Allowed, result.Value!.Outcome);
        Assert.Equal(PolicyEffect.Abstain, result.Value.Decision!.Effect);
    }

    [Fact]
    public async Task ExplainAsync_RequiredObligationIsSummarizedAsUnsupportedTargetOutcome()
    {
        using var provider = OperationTestServices.Build(store: new FakeRecordStore("memory"), policy: new RequiredObligationPolicyEvaluator());
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Unsupported, result.Value!.Outcome);
        Assert.Contains(result.Value.Constraints!.Obligations!, obligation => obligation.Kind == "audit.review" && obligation.Enforcement == ObligationEnforcement.Required);
    }

    [Fact]
    public async Task ExplainAsync_ComposesPolicyFilterWithUserFilterAndRedactsValues()
    {
        var policyFilter = Compare("tenantId", QueryValueKind.String, "tenant-secret");
        var userFilter = Compare("title", QueryValueKind.String, "draft-secret");
        using var provider = OperationTestServices.Build(
            store: new FakeRecordStore("memory"),
            policy: new ConstrainedPolicyEvaluator(recordFilter: policyFilter));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Query,
                CollectionId = "items",
                Query = new RecordQuery { Filter = userFilter },
                Options = new BasePolicyExplainOptions { IncludeConstraintAst = true }
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.AllowedWithConstraints, result.Value!.Outcome);
        Assert.True(result.Value.Runtime!.EffectiveFilterComposed);
        Assert.DoesNotContain("tenant-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("draft-secret", json, StringComparison.Ordinal);
        Assert.Contains("redacted:string", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_InvalidPolicyFilterReturnsValidationFailure()
    {
        using var provider = OperationTestServices.Build(
            store: new FakeRecordStore("memory"),
            policy: new ConstrainedPolicyEvaluator(recordFilter: Compare("tenantId", QueryValueKind.String, "tenant-secret")),
            fields:
            [
                new FieldDefinition
                {
                    Id = "title",
                    ApplicationName = "title", WireName = "title",
                    Type = "string"
                }
            ]);
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Query,
                CollectionId = "items"
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.field.unknown", result.Error!.Code);
        Assert.Null(result.Error.Validation?.FirstOrDefault()?.RejectedValue);
    }

    [Fact]
    public async Task ExplainAsync_PatchComputesProposedRecordAndDoesNotMutateStore()
    {
        var store = new FakeRecordStore("memory");
        store.AddRecord(ExistingRecord("rec_1", ("title", "old"), ("secret", "super-secret-existing")));
        using var provider = OperationTestServices.Build(
            store: store,
            policy: new ConstrainedPolicyEvaluator(writeMask: new FieldMask
            {
                Mode = FieldMaskMode.IncludeOnly,
                Include = ["title"]
            }));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Patch,
                CollectionId = "items",
                RecordId = "rec_1",
                Patch = new RecordPatchRequest { Patch = FieldMapPayload(("title", "new-secret")) },
                Options = new BasePolicyExplainOptions { IncludeRedactedPayloadShape = true }
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.AllowedWithConstraints, result.Value!.Outcome);
        Assert.True(result.Value.Runtime!.ExistingRecordLookupPerformed);
        Assert.True(result.Value.Runtime.ProposedRecordComputed);
        Assert.Equal(1, store.GetCalls);
        Assert.Equal(0, store.PatchCalls);
        Assert.Contains("title", result.Value.Redaction!.OmittedPayloadFields!);
        Assert.DoesNotContain("new-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-existing", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_RecordNotFoundReturnsNotFoundOutcome()
    {
        using var provider = OperationTestServices.Build(store: new FakeRecordStore("memory"));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Record,
                CollectionId = "items",
                RecordId = "missing"
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.NotFound, result.Value!.Outcome);
        Assert.True(result.Value.Runtime!.ExistingRecordLookupPerformed);
        Assert.False(result.Value.Runtime.ExistingRecordFound);
    }

    [Fact]
    public async Task ExplainAsync_RecordCandidateDenyReportsCloakedNotFound()
    {
        var store = new FakeRecordStore("memory");
        store.AddRecord(ExistingRecord("rec_1", ("title", "hidden-title")));
        using var provider = OperationTestServices.Build(store: store, policy: new DenyExistingRecordPolicyEvaluator());
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Record,
                CollectionId = "items",
                RecordId = "rec_1"
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.CloakedNotFound, result.Value!.Outcome);
        Assert.True(result.Value.Runtime!.CloakedNotFoundWouldBeReturnedToPublic);
        Assert.DoesNotContain("hidden-title", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_ReplaceDoesNotMutateStore()
    {
        var store = new FakeRecordStore("memory");
        using var provider = OperationTestServices.Build(store: store);
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Replace,
                CollectionId = "items",
                RecordId = "rec_1",
                Replace = new RecordReplaceRequest { Payload = FieldMapPayload(("title", "replacement-secret")) }
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(0, store.GetCalls);
        Assert.Equal(0, store.ReplaceCalls);
        Assert.DoesNotContain("replacement-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_DeleteLooksUpExistingRecordAndDoesNotMutateStore()
    {
        var store = new FakeRecordStore("memory");
        store.AddRecord(ExistingRecord("rec_1", ("title", "delete-secret")));
        using var provider = OperationTestServices.Build(store: store);
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Delete,
                CollectionId = "items",
                RecordId = "rec_1",
                Delete = new RecordDeleteRequest()
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(1, store.GetCalls);
        Assert.Equal(0, store.DeleteCalls);
        Assert.DoesNotContain("delete-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainAsync_WriteMaskDeniedReportsDeniedWithoutStoreMutation()
    {
        var store = new FakeRecordStore("memory");
        using var provider = OperationTestServices.Build(
            store: store,
            policy: new ConstrainedPolicyEvaluator(writeMask: new FieldMask
            {
                Mode = FieldMaskMode.IncludeOnly,
                Include = ["title"]
            }));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Create,
                CollectionId = "items",
                Create = new RecordCreateRequest { Payload = FieldMapPayload(("secret", "raw-secret")) }
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Denied, result.Value!.Outcome);
        Assert.Equal("writeMask", result.Value.Decision!.ReasonCode);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task ExplainAsync_WriteCheckReportsUnsupportedTargetOutcome()
    {
        using var provider = OperationTestServices.Build(
            store: new FakeRecordStore("memory"),
            policy: new ConstrainedPolicyEvaluator(writeCheck: new FilterExpression
            {
                Kind = FilterNodeKind.Extension,
                Name = "host-only"
            }));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Create,
                CollectionId = "items",
                Create = new RecordCreateRequest { Payload = FieldMapPayload(("title", "value")) }
            },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BasePolicyExplainOutcome.Unsupported, result.Value!.Outcome);
        Assert.True(result.Value.Runtime!.WriteCheckUnsupportedByRuntime);
        Assert.Contains("hpd.base.policy.admin.writeCheckRuntimeUnsupported", result.Value.DiagnosticRefs!);
    }

    [Fact]
    public async Task ExplainAsync_DoesNotExposeClaimsSessionCredentialOrUnsafePolicyMetadata()
    {
        using var provider = OperationTestServices.Build(
            store: new FakeRecordStore("memory"),
            policy: new UnsafeAuditAndTagsPolicyEvaluator());
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Admin,
                Claims = [new ClaimValue { Type = "token", Value = "claim-secret-token" }],
                SessionId = "session-secret",
                CredentialId = "credential-secret"
            },
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.DoesNotContain("claim-secret-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("session-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("policy-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tag-secret", json, StringComparison.Ordinal);
        Assert.Contains("tagName", result.Value!.Constraints!.Tags!);
    }

    [Fact]
    public async Task ExplainAsync_AdvisoryMarksSnapshotAsNonMutating()
    {
        using var provider = OperationTestServices.Build(store: new FakeRecordStore("memory"));
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            AdminPrincipal(),
            RuntimeTestData.Operation(BaseOperationKind.AdminInspect) with { Mode = OperationMode.Admin });

        Assert.Contains("do not reserve or mutate", result.Value!.Advisory, StringComparison.Ordinal);
    }

    private static PrincipalContext AdminPrincipal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Admin
    };

    private static FilterExpression Compare(string field, QueryValueKind kind, string value) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = field,
        Operator = FilterOperator.Equal,
        Value = kind == QueryValueKind.Id
            ? new QueryValue { Kind = QueryValueKind.Id, Id = value }
            : new QueryValue { Kind = kind, String = value }
    };

    private static RecordPayload FieldMapPayload(params (string Name, string Value)[] fields)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            using var document = JsonDocument.Parse($"\"{field.Value}\"");
            values[field.Name] = document.RootElement.Clone();
        }

        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = values
        };
    }

    private static RecordEnvelope ExistingRecord(string id, params (string Name, string Value)[] fields) => new()
    {
        CollectionId = "items",
        Id = RecordId.Create(id),
        Payload = FieldMapPayload(fields),
        Metadata = new RecordMetadata()
    };

    private sealed class AuditedAllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo
                {
                    EvaluatorId = "test.policy",
                    MatchedGrantIds = ["grant_1"],
                    MatchedSubjects =
                    [
                        new AccessSubject { Kind = AccessSubjectKind.User, Id = "user-secret" },
                        new AccessSubject { Kind = AccessSubjectKind.Role, Id = "admin-secret" }
                    ]
                }
            });
        }
    }

    private sealed class RequiredObligationPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.AllowedWithConstraints,
                Obligations =
                [
                    new PolicyObligation
                    {
                        Kind = "audit.review",
                        Code = "audit.review",
                        Enforcement = ObligationEnforcement.Required
                    }
                ]
            });
        }
    }

    private sealed class UnsafeAuditAndTagsPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.AllowedWithConstraints,
                Constraints = new PolicyConstraints
                {
                    Tags = new Dictionary<string, string> { ["tagName"] = "tag-secret" }
                },
                Audit = new PolicyAuditInfo
                {
                    EvaluatorId = "custom.policy",
                    PolicyId = "policy-secret",
                    PolicyVersion = "policy-version-secret",
                    MatchedSubjects = [new AccessSubject { Kind = AccessSubjectKind.User, Id = "subject-secret" }]
                }
            });
        }
    }

    private sealed class CollectionContributor : IBaseDescriptorContributor
    {
        public string Id => "collections";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(new CollectionDefinition
            {
                Id = "items",
                Name = "items",
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve,
                MutationMode = BaseCollectionMutationMode.Mutable
            });
        }
    }

    private static ServiceProvider BuildDirect(
        IRecordStore store,
        IPolicyEvaluator policy,
        Action<HPD.Base.HPDBaseRuntimeOptions>? runtimeOptions = null,
        Action<HPD.Base.IHPDBaseRuntimeBuilder>? configureRuntimeBuilder = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor());
        var builder = services.AddHPDBaseRuntime(runtimeOptions).UsePolicyAuthority(
            "policy-explain-tests",
            new BasePolicyAuthorityDefinition
            {
                Id = "policy-explain-tests.policy",
                Version = 1,
                OwningModuleId = "policy-explain-tests",
                EvaluatorContractId = "policy-explain-tests.evaluator",
                EvaluatorContractVersion = 1,
                CompositionOrder = 0,
            },
            policy);
        configureRuntimeBuilder?.Invoke(builder);
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = store.Capabilities.StoreId,
            Store = store,
            CollectionIds = ["items"]
        });

        return provider;
    }
}
