using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class BaseModuleProgramEvaluatorTests
{
    [Theory]
    [MemberData(nameof(InexactGrants))]
    public async Task Inexact_grant_with_matching_registration_id_is_not_L50_authority(AccessGrant grant)
    {
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System, SubjectId = "system",
        };
        var operation = new OperationContext
        {
            ApplicationId = "module.application", Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ModuleMutation, CollectionId = "module.increment", Now = DateTimeOffset.UtcNow,
        };
        foreach (DefaultBasePolicyOrchestrator orchestrator in new[] { PolicyWithGrant(grant), PolicyWithDynamicGrant(grant) })
        {
            OperationResult<BasePolicyEvaluation> result = await orchestrator.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = principal, Operation = operation, Collection = ModuleCollection(),
                ResourceKind = PolicyResourceKind.ModuleMutation,
            });

            BaseSystemCollectionGate.HasExactModuleGrant(result, "module.increment", "module", principal, operation).Should().BeFalse();
        }
    }

    public static IEnumerable<object[]> InexactGrants()
    {
        AccessGrant exact = new()
        {
            Id = "module.increment", ApplicationId = "module.application", ModuleId = "module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = "module.increment", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        };
        yield return [exact with { Action = "*" }];
        yield return [exact with { ApplicationId = null }];
        yield return [exact with { ModuleId = null }];
        yield return [exact with { Audience = HPDBaseEndpointAudience.Application }];
        yield return [exact with { Subject = exact.Subject with { Kind = AccessSubjectKind.ServicePrincipal } }];
        yield return [exact with { Subject = exact.Subject with { Id = "another-system" } }];
        yield return [exact with { Subject = exact.Subject with { TenantId = "another-tenant" } }];
        yield return [exact with { Scope = exact.Scope with { TenantId = "another-tenant" } }];
        yield return [exact with { Scope = exact.Scope with { ProjectId = "another-project" } }];
        yield return [exact with { Scope = exact.Scope with { CollectionId = "hidden" } }];
        yield return [exact with { Condition = new FilterExpression { Kind = FilterNodeKind.True } }];
        yield return [exact with { WriteCondition = new FilterExpression { Kind = FilterNodeKind.True } }];
        yield return [exact with { Effect = GrantEffect.Deny }];
        yield return [exact with { ExpiresAt = DateTimeOffset.UnixEpoch }];
    }

    [Theory]
    [MemberData(nameof(InexactSourceGrants))]
    public async Task System_source_grant_must_bind_the_exact_collection(AccessGrant grant)
    {
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System, SubjectId = "system",
        };
        var operation = new OperationContext
        {
            ApplicationId = "module.application", Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ModuleMutation, CollectionId = "module-records", Now = DateTimeOffset.UtcNow,
        };
        foreach (DefaultBasePolicyOrchestrator orchestrator in new[] { PolicyWithGrant(grant), PolicyWithDynamicGrant(grant) })
        {
            OperationResult<BasePolicyEvaluation> result = await orchestrator.EvaluateWriteAsync(new BasePolicyRequest
            {
                Principal = principal, Operation = operation, Collection = ModuleCollection(),
                ResourceKind = PolicyResourceKind.ModuleMutation,
            });

            BaseSystemCollectionGate.HasExactModuleSourceGrant(
                result, "module.records.source", "module", principal, operation, "module-records").Should().BeFalse();
        }
    }

    public static IEnumerable<object[]> InexactSourceGrants()
    {
        AccessGrant exact = new()
        {
            Id = "module.records.source", ApplicationId = "module.application", ModuleId = "module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = "module-records",
            Scope = new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = "module-records" },
        };
        yield return [exact with { Action = "*" }];
        yield return [exact with { Scope = exact.Scope with { Kind = ResourceScopeKind.Runtime, CollectionId = null } }];
        yield return [exact with { Scope = exact.Scope with { CollectionId = "other-records" } }];
        yield return [exact with { ApplicationId = null }];
        yield return [exact with { ModuleId = null }];
        yield return [exact with { Audience = HPDBaseEndpointAudience.Application }];
        yield return [exact with { Subject = exact.Subject with { Id = "another-system" } }];
        yield return [exact with { Subject = exact.Subject with { TenantId = "another-tenant" } }];
        yield return [exact with { Scope = exact.Scope with { TenantId = "another-tenant" } }];
        yield return [exact with { Scope = exact.Scope with { ProjectId = "another-project" } }];
        yield return [exact with { Condition = new FilterExpression { Kind = FilterNodeKind.True } }];
        yield return [exact with { WriteCondition = new FilterExpression { Kind = FilterNodeKind.True } }];
        yield return [exact with { Effect = GrantEffect.Deny }];
        yield return [exact with { ExpiresAt = DateTimeOffset.UnixEpoch }];
    }

    [Fact]
    public async Task Installed_grant_semantics_are_deeply_owned_and_not_public_receipt_state()
    {
        FilterExpression[] children = [new() { Kind = FilterNodeKind.True }];
        AccessGrant grant = new()
        {
            Id = "module.records.source", ApplicationId = "module.application", ModuleId = "module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = "module-records",
            Scope = new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = "module-records" },
            Condition = new FilterExpression { Kind = FilterNodeKind.And, Children = children },
        };
        DefaultBasePolicyOrchestrator orchestrator = PolicyWithGrant(grant);
        children[0] = new FilterExpression { Kind = FilterNodeKind.False };
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System, SubjectId = "system",
        };
        var operation = new OperationContext
        {
            ApplicationId = "module.application", Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ModuleMutation, CollectionId = "module-records", Now = DateTimeOffset.UtcNow,
        };

        OperationResult<BasePolicyEvaluation> evaluated = await orchestrator.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal, Operation = operation, Collection = ModuleCollection(),
            ResourceKind = PolicyResourceKind.ModuleMutation,
        });

        evaluated.Value!.Authority!.GrantSemantics.Single().Grant.Condition!.Children![0].Kind
            .Should().Be(FilterNodeKind.True);
        typeof(BaseAdmittedGrantAuthority).GetProperty("Grant").Should().BeNull();
    }

    [Fact]
    public void Canonical_checksum_matches_the_locked_template_byte_vector()
    {
        string actual = Convert.ToHexString(GenerationDefinition().Checksum.ToArray());
        actual.Should().Be("AEC430456F17DA04BA1E270698D96C74E1D198610D51D0A9D340BBC5B67C95D2");
    }

    [Fact]
    public void Canonical_encoder_rejects_non_NFC_source_strings()
    {
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition() with
        {
            OwningModuleId = "modu\u006Ce\u0301",
        };

        Action encode = () => BaseModuleMutationContract.ComputeChecksum(definition);

        encode.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Every_system_source_requires_one_sorted_distinct_grant_binding()
    {
        BaseRegisteredModuleMutationDefinition valid = CreateDefinition();
        var collections = new Dictionary<string, CollectionDefinition> { ["module-records"] = ModuleCollection() };
        BaseModuleMutationContractValidator.ValidateDefinition(valid, collections, new Dictionary<string, BaseModuleGenerationCellDefinition>());

        BaseRegisteredModuleMutationDefinition[] invalid =
        [
            BaseModuleMutationContract.Seal(valid with { SystemSourceGrants = [] }),
            BaseModuleMutationContract.Seal(valid with { SystemSourceGrants = [
                new() { CollectionId = "module-records", GrantId = "module.records.source" },
                new() { CollectionId = "module-records", GrantId = "module.records.other" }] }),
            BaseModuleMutationContract.Seal(valid with { SystemSourceGrants = [
                new() { CollectionId = "other-records", GrantId = "module.records.source" }] }),
        ];

        foreach (BaseRegisteredModuleMutationDefinition definition in invalid)
        {
            Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
                definition, collections, new Dictionary<string, BaseModuleGenerationCellDefinition>());
            validate.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
        }
    }

    [Fact]
    public void Missing_additional_and_reordered_relation_capture_evidence_fail_closed()
    {
        CollectionDefinition target = ModuleCollection();
        BaseModuleRelationTargetCaptureRequest[] expected =
        [
            new() { Ordinal = 0, SourceStatementId = "write-a", SourceFieldId = "owner", TargetCollection = target, TargetRecordId = new RecordId("a") },
            new() { Ordinal = 1, SourceStatementId = "write-b", SourceFieldId = "owner", TargetCollection = target, TargetRecordId = new RecordId("b") },
        ];
        BaseAtomicMutationAuthorityRequirement requirement = AuthorityRequirement();
        var intent = new BaseAtomicMutationIntent { IntentDigest = "intent", Authority = requirement, Items = [] };
        var extension = new BaseModuleMutationCaptureExtension
        {
            OperationId = "module.create", OperationVersion = 1, OperationChecksum = new string('a', 64),
            RequestDigest = new string('b', 64), Records = [], RelationTargets = [.. expected], Generations = [],
        };
        BaseCapturedAtomicExecution valid = RelationEvidence(expected, requirement);
        BaseModuleMutationProcessor<CreateRequest, CreateResult>.CapturedMatches(
            intent, extension, DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(Limits()), valid).Should().BeTrue();

        BaseCapturedAtomicExecution[] hostile =
        [
            Reframe(valid, [valid.ModuleRelationTargets[0]]),
            Reframe(valid, [.. valid.ModuleRelationTargets, valid.ModuleRelationTargets[1] with { Ordinal = 2 }]),
            Reframe(valid, [valid.ModuleRelationTargets[1] with { Ordinal = 0 }, valid.ModuleRelationTargets[0] with { Ordinal = 1 }]),
        ];
        foreach (BaseCapturedAtomicExecution evidence in hostile)
            BaseModuleMutationProcessor<CreateRequest, CreateResult>.CapturedMatches(
                intent, extension, DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(Limits()), evidence).Should().BeFalse();
    }

    private static BaseAtomicMutationAuthorityRequirement AuthorityRequirement() => new()
    {
        ApplicationId = "module.application", StoreInstanceId = "module-store", RestoreEpoch = 1,
        SchemaGeneration = 1, Collections = [],
    };

    private static BaseCapturedAtomicExecution RelationEvidence(
        IReadOnlyList<BaseModuleRelationTargetCaptureRequest> expected,
        BaseAtomicMutationAuthorityRequirement requirement)
    {
        ImmutableArray<BaseAtomicReadIntervalEvidence> intervals = [.. expected.Select(item => new BaseAtomicReadIntervalEvidence
        {
            LogicalAccessPathId = $"collection:{item.TargetCollection.Id}:record",
            CanonicalLowerBound = System.Text.Encoding.UTF8.GetBytes(item.TargetRecordId.Value).ToImmutableArray(),
            LowerInclusive = true,
            CanonicalUpperBound = System.Text.Encoding.UTF8.GetBytes(item.TargetRecordId.Value).ToImmutableArray(),
            UpperInclusive = true,
        })];
        long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
        return new BaseCapturedAtomicExecution
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation, IntentDigest = "intent", CaptureDigest = new string('c', 64),
            Authority = new BaseAtomicMutationAuthorityEvidence
            {
                ApplicationId = requirement.ApplicationId, StoreInstanceId = requirement.StoreInstanceId,
                RestoreEpoch = requirement.RestoreEpoch, SchemaGeneration = requirement.SchemaGeneration,
                Collections = requirement.Collections, Isolation = BaseAtomicSelectionIsolationClass.NativeSerializable,
                TransactionEvidenceToken = [1],
            },
            Items = [], ModuleRecords = [], Generations = [], ReadIntervals = intervals,
            ModuleRelationTargets = [.. expected.Select(item => new BaseCapturedModuleRelationTarget
            {
                Ordinal = item.Ordinal, SourceStatementId = item.SourceStatementId, SourceFieldId = item.SourceFieldId,
                TargetCollectionId = item.TargetCollection.Id, TargetRecordId = item.TargetRecordId,
            })],
            Accounting = new BaseAtomicCaptureAccounting
            {
                Records = 0, RelationTargetReads = expected.Count, GenerationReads = 0,
                SelectedBytes = 0, RelationTargetBytes = 0, GenerationBytes = 0,
                ReadIntervals = intervals.Length, EvidenceBytes = evidenceBytes, TransientBytes = evidenceBytes,
                RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
            },
        };
    }

    private static BaseCapturedAtomicExecution Reframe(
        BaseCapturedAtomicExecution value,
        ImmutableArray<BaseCapturedModuleRelationTarget> relations) => value with
    {
        ModuleRelationTargets = relations,
        Accounting = value.Accounting with { RelationTargetReads = relations.Length },
    };

    [Fact]
    public void Closed_manual_builder_matches_the_direct_canonical_contract()
    {
        BaseRegisteredModuleMutationDefinition direct = GenerationDefinition();
        BaseRegisteredModuleMutationDefinition authored = BaseModuleMutationTemplateBuilder.Create(direct with
        {
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
            Template = new BaseModuleMutationTemplate
            {
                Captures = [BaseModuleMutationTemplateBuilder.CaptureGeneration(
                    "generation", "module.generation", null, BaseModuleGenerationAbsenceBehavior.AllowEither)],
                Guards = [],
                Body = BaseModuleMutationTemplateBuilder.Block(
                    BaseModuleMutationTemplateBuilder.IncrementGeneration("increment", "generation", true)),
                Result = BaseModuleMutationTemplateBuilder.Result(
                    BaseModuleMutationTemplateBuilder.Object("result", "result",
                        BaseModuleMutationTemplateBuilder.Property("result.generation",
                            BaseModuleMutationTemplateBuilder.ResultingGeneration(
                                "result-generation", "string", "generation")))),
            },
        }).Build();

        authored.Checksum.Should().Be(direct.Checksum);
    }

    [Fact]
    public void Every_manual_factory_matches_its_direct_closed_union_shape()
    {
        BaseModuleValueExpression constant = BaseModuleMutationTemplateBuilder.Constant("constant", "int64", "1"u8);
        var requestReference = new BaseModuleRequestPropertyReference { StablePropertyPath = ["request.value"], DeclaredTypeId = "int64" };
        var fieldReference = new BaseModuleCapturedFieldReference { CaptureId = "record", StableFieldId = "record.value", DeclaredTypeId = "int64" };
        BaseModuleObjectExpression payload = BaseModuleMutationTemplateBuilder.Object("payload", "payload",
            BaseModuleMutationTemplateBuilder.Property("payload.value", constant));
        BaseModuleMutationBlock empty = BaseModuleMutationTemplateBuilder.Block();

        (object Factory, object Direct)[] pairs =
        [
            (BaseModuleMutationTemplateBuilder.CaptureRecord("record", "records", constant, BaseModuleCapturePresence.RequirePresent),
                new BaseModuleRecordCapture { Id = "record", CollectionId = "records", RecordId = constant, Presence = BaseModuleCapturePresence.RequirePresent }),
            (BaseModuleMutationTemplateBuilder.CaptureGeneration("generation", "module.generation", constant, BaseModuleGenerationAbsenceBehavior.AllowEither),
                new BaseModuleGenerationCapture { Id = "generation", CellId = "module.generation", Key = constant, Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }),
            (BaseModuleMutationTemplateBuilder.RecordPresent("record-present", "record", true),
                new BaseModuleRecordPresenceGuard { Id = "record-present", CaptureId = "record", MustBePresent = true }),
            (BaseModuleMutationTemplateBuilder.RevisionEquals("revision", "record", constant),
                new BaseModuleRevisionEqualsGuard { Id = "revision", CaptureId = "record", Expected = constant }),
            (BaseModuleMutationTemplateBuilder.FieldEquals("field-equals", fieldReference, constant),
                new BaseModuleFieldEqualsGuard { Id = "field-equals", Field = fieldReference, Expected = constant }),
            (BaseModuleMutationTemplateBuilder.FieldPresence("field-present", fieldReference, BaseModuleFieldPresenceTest.PresentValue),
                new BaseModuleFieldPresenceGuard { Id = "field-present", Field = fieldReference, Test = BaseModuleFieldPresenceTest.PresentValue }),
            (BaseModuleMutationTemplateBuilder.Generation("generation-equals", "generation", BaseModuleGenerationComparisonKind.MustEqual, constant),
                new BaseModuleGenerationGuard { Id = "generation-equals", CaptureId = "generation", Comparison = BaseModuleGenerationComparisonKind.MustEqual, Expected = constant }),
            (BaseModuleMutationTemplateBuilder.And("and", "a", "b"),
                new BaseModuleLogicalGuard { Id = "and", Kind = BaseModuleLogicalGuardKind.And, ChildGuardIds = ["a", "b"] }),
            (BaseModuleMutationTemplateBuilder.Or("or", "a", "b"),
                new BaseModuleLogicalGuard { Id = "or", Kind = BaseModuleLogicalGuardKind.Or, ChildGuardIds = ["a", "b"] }),
            (BaseModuleMutationTemplateBuilder.Not("not", "a"),
                new BaseModuleLogicalGuard { Id = "not", Kind = BaseModuleLogicalGuardKind.Not, ChildGuardIds = ["a"] }),
            (BaseModuleMutationTemplateBuilder.Create("create", "records", constant, payload),
                new BaseModuleCreateStatement { Id = "create", CollectionId = "records", RecordId = constant, Payload = payload }),
            (BaseModuleMutationTemplateBuilder.Patch("patch", "records", constant, payload, constant),
                new BaseModulePatchStatement { Id = "patch", CollectionId = "records", RecordId = constant, Patch = payload, ExpectedRevision = constant }),
            (BaseModuleMutationTemplateBuilder.Replace("replace", "records", constant, payload, constant),
                new BaseModuleReplaceStatement { Id = "replace", CollectionId = "records", RecordId = constant, Payload = payload, ExpectedRevision = constant }),
            (BaseModuleMutationTemplateBuilder.Delete("delete", "records", constant, constant),
                new BaseModuleDeleteStatement { Id = "delete", CollectionId = "records", RecordId = constant, ExpectedRevision = constant }),
            (BaseModuleMutationTemplateBuilder.Upsert("upsert", "records", constant, payload, payload, RecordUpsertUpdateMode.Replace, constant),
                new BaseModuleUpsertStatement { Id = "upsert", CollectionId = "records", RecordId = constant, Create = payload, Update = payload, UpdateMode = RecordUpsertUpdateMode.Replace, ExpectedRevision = constant }),
            (BaseModuleMutationTemplateBuilder.IncrementGeneration("increment", "generation", true),
                new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }),
            (BaseModuleMutationTemplateBuilder.If("if", "guard", empty, empty),
                new BaseModuleIfStatement { Id = "if", GuardId = "guard", WhenTrue = empty, WhenFalse = empty }),
            (BaseModuleMutationTemplateBuilder.Require("require", "guard", "requirement"),
                new BaseModuleRequireStatement { Id = "require", GuardId = "guard", RequirementId = "requirement" }),
            (BaseModuleMutationTemplateBuilder.RequestProperty("request", "int64", requestReference),
                new BaseModuleRequestPropertyExpression { Id = "request", ResultTypeId = "int64", Property = requestReference }),
            (constant, new BaseModuleConstantExpression { Id = "constant", ResultTypeId = "int64", CanonicalBaseJson = "1"u8.ToArray().ToImmutableArray() }),
            (BaseModuleMutationTemplateBuilder.CapturedField("captured-field", "int64", fieldReference),
                new BaseModuleCapturedFieldExpression { Id = "captured-field", ResultTypeId = "int64", Field = fieldReference }),
            (BaseModuleMutationTemplateBuilder.CapturedRecordId("captured-id", "recordId", "record"),
                new BaseModuleCapturedRecordIdExpression { Id = "captured-id", ResultTypeId = "recordId", CaptureId = "record" }),
            (BaseModuleMutationTemplateBuilder.CapturedRevision("captured-revision", "record"),
                new BaseModuleCapturedRevisionExpression { Id = "captured-revision", ResultTypeId = "revision", CaptureId = "record" }),
            (BaseModuleMutationTemplateBuilder.CapturedGeneration("captured-generation", "base.moduleGeneration", "generation"),
                new BaseModuleCapturedGenerationExpression { Id = "captured-generation", ResultTypeId = "base.moduleGeneration", CaptureId = "generation" }),
            (BaseModuleMutationTemplateBuilder.CommittedRecordId("committed-id", "recordId", "create"),
                new BaseModuleCommittedRecordIdExpression { Id = "committed-id", ResultTypeId = "recordId", StatementId = "create" }),
            (BaseModuleMutationTemplateBuilder.CommittedRevision("committed-revision", "create"),
                new BaseModuleCommittedRevisionExpression { Id = "committed-revision", ResultTypeId = "revision", StatementId = "create" }),
            (BaseModuleMutationTemplateBuilder.CommittedUpsertDisposition("upsert-disposition", "upsertDisposition", "upsert"),
                new BaseModuleCommittedUpsertDispositionExpression { Id = "upsert-disposition", ResultTypeId = "upsertDisposition", StatementId = "upsert" }),
            (BaseModuleMutationTemplateBuilder.ResultingGeneration("resulting-generation", "base.moduleGeneration", "generation"),
                new BaseModuleResultingGenerationExpression { Id = "resulting-generation", ResultTypeId = "base.moduleGeneration", CaptureId = "generation" }),
            (BaseModuleMutationTemplateBuilder.Coalesce("coalesce", "int64", constant, constant),
                new BaseModuleCoalesceExpression { Id = "coalesce", ResultTypeId = "int64", Values = [constant, constant] }),
            (BaseModuleMutationTemplateBuilder.Conditional("conditional", "int64", "guard", constant, constant),
                new BaseModuleConditionalExpression { Id = "conditional", ResultTypeId = "int64", GuardId = "guard", WhenTrue = constant, WhenFalse = constant }),
            (BaseModuleMutationTemplateBuilder.Numeric("numeric", "int64", BaseModuleNumericOperator.IntegerAddChecked, constant, constant),
                new BaseModuleBinaryNumericExpression { Id = "numeric", ResultTypeId = "int64", Operator = BaseModuleNumericOperator.IntegerAddChecked, Left = constant, Right = constant }),
            (payload, new BaseModuleObjectExpression { Id = "payload", ResultTypeId = "payload", Properties = [new BaseModuleObjectPropertyExpression { StablePropertyId = "payload.value", Value = constant }] }),
            (BaseModuleMutationTemplateBuilder.Block(new BaseModuleRequireStatement { Id = "required", GuardId = "guard", RequirementId = "required" }),
                new BaseModuleMutationBlock { Statements = [new BaseModuleRequireStatement { Id = "required", GuardId = "guard", RequirementId = "required" }] }),
            (BaseModuleMutationTemplateBuilder.Result(payload), new BaseModuleResultProjection { Value = payload }),
        ];

        foreach ((object factory, object direct) in pairs)
            factory.Should().BeEquivalentTo(direct, options => options.RespectingRuntimeTypes(), factory.GetType().Name);
    }

    [Fact]
    public void Conditional_result_definite_assignment_is_validated_per_execution_path()
    {
        BaseModuleGenerationCellDefinition Cell(string id) => new()
        {
            Id = id, Version = 1, OwningModuleId = "module", Scope = BaseModuleGenerationScope.Application,
            MaximumKeyUtf8Bytes = 1, MaximumCellsPerOperation = 2,
        };
        var cells = new Dictionary<string, BaseModuleGenerationCellDefinition>
        {
            ["module.a"] = Cell("module.a"), ["module.b"] = Cell("module.b"),
        };
        BaseModuleResultingGenerationExpression Resulting(string id, string capture) => new()
        {
            Id = id, ResultTypeId = "base.moduleGeneration", CaptureId = capture,
        };
        var conditional = new BaseModuleConditionalExpression
        {
            Id = "selected", ResultTypeId = "base.moduleGeneration", GuardId = "choose-a",
            WhenTrue = Resulting("selected-a", "a"), WhenFalse = Resulting("selected-b", "b"),
        };
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(new()
        {
            Id = "module.conditional", Version = 1, OwningModuleId = "module", GrantId = "module.conditional",
            Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
            SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = ["module.a", "module.b"], ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures =
                [
                    new BaseModuleGenerationCapture { Id = "a", CellId = "module.a", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither },
                    new BaseModuleGenerationCapture { Id = "b", CellId = "module.b", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither },
                ],
                Guards = [new BaseModuleGenerationGuard { Id = "choose-a", CaptureId = "a", Comparison = BaseModuleGenerationComparisonKind.MustBeMissing }],
                Body = BaseModuleMutationTemplateBuilder.Block(new BaseModuleIfStatement
                {
                    Id = "choose", GuardId = "choose-a",
                    WhenTrue = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.IncrementGeneration("increment-a", "a", true)),
                    WhenFalse = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.IncrementGeneration("increment-b", "b", true)),
                }),
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.Object("result", "result",
                    BaseModuleMutationTemplateBuilder.Property("result.generation", conditional))),
            },
            Limits = Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        });

        Action valid = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition, new Dictionary<string, CollectionDefinition>(), cells);
        valid.Should().NotThrow();

        BaseRegisteredModuleMutationDefinition invalid = BaseModuleMutationContract.Seal(definition with
        {
            Template = definition.Template with
            {
                Result = BaseModuleMutationTemplateBuilder.Result(BaseModuleMutationTemplateBuilder.Object("result", "result",
                    BaseModuleMutationTemplateBuilder.Property("result.generation", Resulting("unconditional-a", "a")))),
            },
        });
        Action rejected = () => BaseModuleMutationContractValidator.ValidateDefinition(
            invalid, new Dictionary<string, CollectionDefinition>(), cells);
        rejected.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Unknown_CLR_result_type_fails_closed_against_the_L44_node()
    {
        CollectionDefinition collection = ModuleCollection();
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        var identity = new BaseGeneratedModuleMutationIdentity<CreateRequest, UnsupportedResult>(
            definition.Id, definition.Version, definition.Checksum.ToArray(),
            EvaluatorJsonContext.Default.CreateRequest, EvaluatorJsonContext.Default.UnsupportedResult,
            [
                BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.id", nameof(CreateRequest.Id)),
                BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.name", nameof(CreateRequest.Name)),
            ],
            [BaseModuleDtoPropertyBinding.Create<UnsupportedResult, DateTimeOffset>("result.id", nameof(UnsupportedResult.Id))]);
        var registration = new BaseModuleMutationRegistration<CreateRequest, UnsupportedResult>(definition, identity);

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition> { [collection.Id] = collection },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(), registration);

        validate.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public async Task Receipt_result_denies_a_current_L42_ineligible_field()
    {
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition();
        DefaultBasePolicyOrchestrator orchestrator = Policy("module.increment");
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System, SubjectId = "system",
        };
        var operation = new OperationContext
        {
            ApplicationId = "module.application", Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ModuleMutation, CollectionId = definition.Id, Now = DateTimeOffset.UtcNow,
        };
        var receipt = new BaseAtomicReceiptResult
        {
            Kind = BaseAtomicReceiptResultKind.ModuleMutation, Mutations = [],
            ModuleMutation = new BaseModuleMutationReceiptResult
            {
                OperationId = definition.Id, OperationVersion = definition.Version,
                Disposition = BaseMutationRequestDisposition.Committed, Outcome = BaseModuleMutationOutcome.Committed,
                Generations = [], CanonicalResultBytes = "{}"u8.ToArray().ToImmutableArray(),
            },
        };
        IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> bindings = new Dictionary<string, BaseModuleDtoPropertyBinding>
        {
            ["result.generation"] = BaseModuleDtoPropertyBinding.Create<GenerationResult, string>(
                "result.generation", nameof(GenerationResult.Generation), BaseFieldConfidentiality.Confidential, BaseRecordDisclosure.Omit),
        };

        bool allowed = await BaseModuleReceiptDisclosure.AuthorizeAsync(
            receipt, definition, bindings, principal, operation, orchestrator, default);

        allowed.Should().BeFalse();
    }

    [Fact]
    public void Caller_authored_checksum_is_rejected()
    {
        CollectionDefinition collection = ModuleCollection();
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition() with
        {
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        };

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition> { [collection.Id] = collection },
            new Dictionary<string, BaseModuleGenerationCellDefinition>());

        validate.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Platform_limit_plus_one_is_rejected()
    {
        CollectionDefinition collection = ModuleCollection();
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        definition = BaseModuleMutationContract.Seal(definition with
        {
            Limits = definition.Limits with { MaximumStatements = 513 },
        });

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition> { [collection.Id] = collection },
            new Dictionary<string, BaseModuleGenerationCellDefinition>());

        validate.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Theory]
    [MemberData(nameof(PlatformLimitBoundaries))]
    public void Every_platform_limit_accepts_maximum_and_rejects_maximum_plus_one(string member, long maximum)
    {
        BaseModuleMutationLimits atMaximum = WithLimit(Limits(), member, maximum);
        BaseModuleMutationContractValidator.ValidateLimits(atMaximum);

        Action exceeds = () => BaseModuleMutationContractValidator.ValidateLimits(WithLimit(Limits(), member, checked(maximum + 1)));
        exceeds.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    public static IEnumerable<object[]> PlatformLimitBoundaries()
    {
        BaseModuleMutationLimits value = BaseModuleMutationPlatform.MaximumLimits;
        yield return [nameof(value.MaximumCaptures), value.MaximumCaptures];
        yield return [nameof(value.MaximumRecordCaptures), value.MaximumRecordCaptures];
        yield return [nameof(value.MaximumRelationTargetCaptures), value.MaximumRelationTargetCaptures];
        yield return [nameof(value.MaximumGenerationCaptures), value.MaximumGenerationCaptures];
        yield return [nameof(value.MaximumRecordMutations), value.MaximumRecordMutations];
        yield return [nameof(value.MaximumGenerationReads), value.MaximumGenerationReads];
        yield return [nameof(value.MaximumGenerationComparisons), value.MaximumGenerationComparisons];
        yield return [nameof(value.MaximumGenerationIncrements), value.MaximumGenerationIncrements];
        yield return [nameof(value.MaximumGuardNodes), value.MaximumGuardNodes];
        yield return [nameof(value.MaximumGuardDepth), value.MaximumGuardDepth];
        yield return [nameof(value.MaximumStatements), value.MaximumStatements];
        yield return [nameof(value.MaximumBranches), value.MaximumBranches];
        yield return [nameof(value.MaximumExpressionNodes), value.MaximumExpressionNodes];
        yield return [nameof(value.MaximumReadIntervals), value.MaximumReadIntervals];
        yield return [nameof(value.MaximumSubjectValidations), value.MaximumSubjectValidations];
        yield return [nameof(value.MaximumAuthorityReads), value.MaximumAuthorityReads];
        yield return [nameof(value.MaximumRelationChecks), value.MaximumRelationChecks];
        yield return [nameof(value.MaximumUniqueConstraintChecks), value.MaximumUniqueConstraintChecks];
        yield return [nameof(value.MaximumRequestBytes), value.MaximumRequestBytes];
        yield return [nameof(value.MaximumSelectedBytes), value.MaximumSelectedBytes];
        yield return [nameof(value.MaximumGenerationBytes), value.MaximumGenerationBytes];
        yield return [nameof(value.MaximumEvidenceBytes), value.MaximumEvidenceBytes];
        yield return [nameof(value.MaximumWrittenBytes), value.MaximumWrittenBytes];
        yield return [nameof(value.MaximumFactBytes), value.MaximumFactBytes];
        yield return [nameof(value.MaximumJournalBytes), value.MaximumJournalBytes];
        yield return [nameof(value.MaximumReceiptBytes), value.MaximumReceiptBytes];
        yield return [nameof(value.MaximumResultBytes), value.MaximumResultBytes];
        yield return [nameof(value.MaximumTransientBytes), value.MaximumTransientBytes];
        yield return [nameof(value.Deadlines.AcquisitionTimeout), value.Deadlines.AcquisitionTimeout.Ticks];
        yield return [nameof(value.Deadlines.TransactionTimeout), value.Deadlines.TransactionTimeout.Ticks];
        yield return [nameof(value.Deadlines.CommitObservationTimeout), value.Deadlines.CommitObservationTimeout.Ticks];
        yield return [nameof(value.Deadlines.ReceiptResolutionTimeout), value.Deadlines.ReceiptResolutionTimeout.Ticks];
    }

    private static BaseModuleMutationLimits WithLimit(BaseModuleMutationLimits value, string member, long amount) => member switch
    {
        nameof(value.MaximumCaptures) => value with { MaximumCaptures = checked((int)amount) },
        nameof(value.MaximumRecordCaptures) => value with { MaximumRecordCaptures = checked((int)amount) },
        nameof(value.MaximumRelationTargetCaptures) => value with { MaximumRelationTargetCaptures = checked((int)amount) },
        nameof(value.MaximumGenerationCaptures) => value with { MaximumGenerationCaptures = checked((int)amount) },
        nameof(value.MaximumRecordMutations) => value with { MaximumRecordMutations = checked((int)amount) },
        nameof(value.MaximumGenerationReads) => value with { MaximumGenerationReads = checked((int)amount) },
        nameof(value.MaximumGenerationComparisons) => value with { MaximumGenerationComparisons = checked((int)amount) },
        nameof(value.MaximumGenerationIncrements) => value with { MaximumGenerationIncrements = checked((int)amount) },
        nameof(value.MaximumGuardNodes) => value with { MaximumGuardNodes = checked((int)amount) },
        nameof(value.MaximumGuardDepth) => value with { MaximumGuardDepth = checked((int)amount) },
        nameof(value.MaximumStatements) => value with { MaximumStatements = checked((int)amount) },
        nameof(value.MaximumBranches) => value with { MaximumBranches = checked((int)amount) },
        nameof(value.MaximumExpressionNodes) => value with { MaximumExpressionNodes = checked((int)amount) },
        nameof(value.MaximumReadIntervals) => value with { MaximumReadIntervals = checked((int)amount) },
        nameof(value.MaximumSubjectValidations) => value with { MaximumSubjectValidations = checked((int)amount) },
        nameof(value.MaximumAuthorityReads) => value with { MaximumAuthorityReads = checked((int)amount) },
        nameof(value.MaximumRelationChecks) => value with { MaximumRelationChecks = checked((int)amount) },
        nameof(value.MaximumUniqueConstraintChecks) => value with { MaximumUniqueConstraintChecks = checked((int)amount) },
        nameof(value.MaximumRequestBytes) => value with { MaximumRequestBytes = amount },
        nameof(value.MaximumSelectedBytes) => value with { MaximumSelectedBytes = amount },
        nameof(value.MaximumGenerationBytes) => value with { MaximumGenerationBytes = amount },
        nameof(value.MaximumEvidenceBytes) => value with { MaximumEvidenceBytes = amount },
        nameof(value.MaximumWrittenBytes) => value with { MaximumWrittenBytes = amount },
        nameof(value.MaximumFactBytes) => value with { MaximumFactBytes = amount },
        nameof(value.MaximumJournalBytes) => value with { MaximumJournalBytes = amount },
        nameof(value.MaximumReceiptBytes) => value with { MaximumReceiptBytes = amount },
        nameof(value.MaximumResultBytes) => value with { MaximumResultBytes = amount },
        nameof(value.MaximumTransientBytes) => value with { MaximumTransientBytes = amount },
        nameof(value.Deadlines.AcquisitionTimeout) => value with { Deadlines = value.Deadlines with { AcquisitionTimeout = TimeSpan.FromTicks(amount) } },
        nameof(value.Deadlines.TransactionTimeout) => value with { Deadlines = value.Deadlines with { TransactionTimeout = TimeSpan.FromTicks(amount) } },
        nameof(value.Deadlines.CommitObservationTimeout) => value with { Deadlines = value.Deadlines with { CommitObservationTimeout = TimeSpan.FromTicks(amount) } },
        nameof(value.Deadlines.ReceiptResolutionTimeout) => value with { Deadlines = value.Deadlines with { ReceiptResolutionTimeout = TimeSpan.FromTicks(amount) } },
        _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };

    [Fact]
    public void Contract_validation_accepts_the_closed_record_program()
    {
        CollectionDefinition collection = ModuleCollection();
        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            CreateDefinition(),
            new Dictionary<string, CollectionDefinition> { [collection.Id] = collection },
            new Dictionary<string, BaseModuleGenerationCellDefinition>());

        validate.Should().NotThrow();
    }

    [Fact]
    public void Capture_keys_cannot_depend_on_provider_captured_values()
    {
        CollectionDefinition collection = ModuleCollection();
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        definition = definition with
        {
            Template = definition.Template with
            {
                Captures =
                [
                    new BaseModuleRecordCapture
                    {
                        Id = "record", CollectionId = collection.Id, Presence = BaseModuleCapturePresence.AllowEither,
                        RecordId = new BaseModuleCapturedFieldExpression
                        {
                            Id = "captured-key", ResultTypeId = "string",
                            Field = new BaseModuleCapturedFieldReference
                            {
                                CaptureId = "record", StableFieldId = "field.name", DeclaredTypeId = "string",
                            },
                        },
                    },
                ],
            },
        };

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition> { [collection.Id] = collection },
            new Dictionary<string, BaseModuleGenerationCellDefinition>());

        validate.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
    }

    [Fact]
    public async Task Create_statement_uses_the_shared_L30_pipeline_and_commits_its_typed_result()
    {
        CollectionDefinition collection = ModuleCollection();
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "module-store", Collections = [collection] });
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store, CollectionIds = [collection.Id] });
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        DefaultBasePolicyOrchestrator policy = Policy("module.create", "module.records.source");
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition> { [collection.Id] = collection }),
            new BaseModuleMutationRegistry([definition], []), new DefaultBaseSchemaValidator(), policy,
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane }, applicationId: "module.application");
        BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
            "module", "create", "one", BaseMutationRequestFingerprint.Create(new byte[32]));

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "record-1", Name = "Ada" }, requestIdentity, null, default);

        result.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<CreateResult>>>(
            result is BaseFailure<BaseModuleMutationExecutionResult<CreateResult>> failure ? failure.Error.Code : string.Empty);
        result.RequireValue().Result.Id.Should().Be("record-1");
        OperationResult<RecordEnvelope> stored = await store.GetAsync(collection, new RecordId("record-1"), session.Operation(BaseOperationKind.Get, collection.Id));
        stored.Value!.Payload.Fields!["name"].GetString().Should().Be("Grace");
    }

    [Fact]
    public async Task Operation_grant_cannot_stand_in_for_declared_system_source_authority()
    {
        CollectionDefinition collection = ModuleCollection();
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "module-store", Collections = [collection] });
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store, CollectionIds = [collection.Id] });
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition> { [collection.Id] = collection }),
            new BaseModuleMutationRegistry([definition], []), new DefaultBaseSchemaValidator(), Policy("module.create"),
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane }, applicationId: "module.application");

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "record-denied", Name = "Ada" },
            BaseMutationRequestIdentity.Create("module", "create", "denied", BaseMutationRequestFingerprint.Create(new byte[32])), null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<CreateResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.Unauthorized);
        (await store.GetAsync(collection, new RecordId("record-denied"), session.Operation(BaseOperationKind.Get, collection.Id)))
            .Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task Generation_only_operation_commits_and_replays_through_the_real_in_memory_boundary()
    {
        var storeOptions = new HPDBaseInMemoryStoreOptions { StoreId = "module-store", Collections = [] };
        var store = new InMemoryRecordStore(storeOptions);
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseModuleGenerationCellDefinition cell = new()
        {
            Id = "module.generation", Version = 1, OwningModuleId = "module",
            Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
        };
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition();
        var registry = new BaseModuleMutationRegistry([definition], [cell]);
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()), registry,
            null!, Policy("module.increment"), null!, new BaseSubjectContractRegistry([]), TimeProvider.System);
        BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> identity = GenerationIdentity();
        var session = new BaseSession(
            null!, TimeProvider.System,
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");
        BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
            "module", "increment", "one", BaseMutationRequestFingerprint.Create(new byte[32]));

        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> first = await runtime.ExecuteAsync(
            session, definition, identity, new GenerationRequest(), requestIdentity, null, default);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> duplicate = await runtime.ExecuteAsync(
            session, definition, identity, new GenerationRequest(), requestIdentity, null, default);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> resolved = await runtime.ResolveAsync(
            session, definition, identity, requestIdentity, default);

        first.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<GenerationResult>>>(
            first is BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>> failed ? failed.Error.Code : string.Empty);
        duplicate.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<GenerationResult>>>(
            duplicate is BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>> duplicateFailure ? duplicateFailure.Error.Code : string.Empty);
        first.RequireValue().Result.Generation.Should().Be("1");
        first.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.RequireValue().Result.Generation.Should().Be("1");
        duplicate.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.RequireValue().Result.Generation.Should().Be("1");
        resolved.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Fact]
    public async Task Transactional_activation_commits_target_and_terminal_state_in_one_in_memory_transaction()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "module-store", Collections = [] });
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseModuleGenerationCellDefinition cell = new()
        {
            Id = "module.generation", Version = 1, OwningModuleId = "module",
            Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
        };
        BaseRegisteredModuleMutationDefinition moduleDefinition = GenerationDefinition();
        BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> moduleIdentity = GenerationIdentity();
        var moduleRegistration = new BaseModuleMutationRegistration<GenerationRequest, GenerationResult>(moduleDefinition, moduleIdentity);
        var moduleRegistry = new BaseModuleMutationRegistry([moduleDefinition], [cell], [moduleRegistration]);
        DefaultBasePolicyOrchestrator policy = TransactionalActivationPolicy();
        var moduleRuntime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()), moduleRegistry,
            null!, policy, null!, new BaseSubjectContractRegistry([]), TimeProvider.System);
        BaseActivationGrantSet grants = TransactionalActivationGrants();
        BaseTransactionalActivationRegistration<GenerationRequest, GenerationResult> activation =
            BaseActivationDefinitionBuilder.CreateTransactional(new BaseActivationDefinition
            {
                Id = "module.transactional", Version = 1, OwningModuleId = "module",
                ExecutionClass = BaseActivationExecutionClass.TransactionalOperation,
                InputTypeId = "request", ResultTypeId = "result", Grants = grants, SourceGrantIds = [],
                Retry = new BaseActivationRetryProfile
                {
                    MaximumAttempts = 1, InitialDelayMilliseconds = 1, MaximumDelayMilliseconds = 1,
                    MultiplierNumerator = 1, MultiplierDenominator = 1, JitterBasisPoints = 0,
                    RetryableFailureCodes = [],
                },
                Limits = new BaseActivationLimits
                {
                    MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 1,
                    MaximumRenewalsPerAttempt = 1, MaximumChildrenPerAttempt = 1, MaximumLineageDepth = 1,
                    LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(1),
                    Provider = ActivationProviderLimits(), AtomicCreation = DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits),
                },
                Handler = null,
                TransactionalTarget = new BaseModuleMutationActivationTarget
                {
                    OperationId = moduleDefinition.Id, OperationVersion = moduleDefinition.Version,
                    OperationChecksum = Convert.ToHexStringLower(moduleDefinition.Checksum.ToArray()),
                },
                Checksum = [],
            }, EvaluatorJsonContext.Default.GenerationRequest, EvaluatorJsonContext.Default.GenerationResult);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IBaseModuleMutationRuntime>(moduleRuntime)
            .BuildServiceProvider();
        BaseSession session = new(
            null!, TimeProvider.System,
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane }, services: services,
            applicationId: "module.application");
        var enqueue = new DefaultBaseActivationRuntime(stores, policy, TimeProvider.System);
        OperationResult<BaseActivationEnqueueResult> created = await enqueue.EnqueueAsync(
            session, activation.Definition, activation.Identity, new GenerationRequest(),
            BaseMutationRequestIdentity.Create("activation", "enqueue", "one", BaseMutationRequestFingerprint.Create(new byte[32])), null, default);
        created.IsSuccess().Should().BeTrue(created.Error?.Code);
        var activationRegistry = new BaseActivationRegistry(
            [new BaseInstalledTransactionalActivationRegistration<GenerationRequest, GenerationResult>(activation)]);
        var worker = new DefaultBaseActivationWorkerRuntime(
            stores, policy, new BaseActivationAcceptedTimeAuthority(TimeProvider.System), activationRegistry, moduleRegistry);
        OperationResult<BaseActivationDueObservation> observation = await worker.ObserveAsync(session, activation.Definition, default);
        OperationResult<BaseActivationDispatchResult> dispatched = await worker.ExecuteTransactionalAsync(
            session, activation.Definition, observation.Value!.Token, default);
        dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
        dispatched.Value!.State.Should().Be(BaseActivationState.Succeeded);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> second = await moduleRuntime.ExecuteAsync(
            session, moduleDefinition, moduleIdentity, new GenerationRequest(),
            BaseMutationRequestIdentity.Create("module", "increment", "after-activation", BaseMutationRequestFingerprint.Create(new byte[32])),
            null, default);
        second.RequireValue().Result.Generation.Should().Be("2");
        OperationResult<BaseActivationDueObservation> after = await worker.ObserveAsync(session, activation.Definition, default);
        after.Value!.Earliest.Should().BeNull();
    }

    [Fact]
    public async Task Missing_exact_operation_grant_fails_before_execution()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "module-store", Collections = [] });
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseModuleGenerationCellDefinition cell = new()
        {
            Id = "module.generation", Version = 1, OwningModuleId = "module",
            Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
        };
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition();
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            new BaseModuleMutationRegistry([definition], [cell]), null!, Policy(), null!,
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane }, applicationId: "module.application");
        BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
            "module", "increment", "denied", BaseMutationRequestFingerprint.Create(new byte[32]));

        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> result = await runtime.ExecuteAsync(
            session, definition, GenerationIdentity(), new GenerationRequest(), requestIdentity, null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.Unauthorized);
    }

    [Fact]
    public void Stable_request_edges_drive_guards_and_result_projection()
    {
        BaseGeneratedModuleMutationIdentity<EvaluatorRequest, EvaluatorResult> identity = Identity();
        BaseRegisteredModuleMutationDefinition definition = Definition();
        BaseCapturedAtomicExecution captured = Captured();
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            definition,
            identity,
            new EvaluatorRequest { Amount = 41, Enabled = true },
            captured,
            new Dictionary<string, CollectionDefinition>());

        evaluator.Guard("enabled").Should().BeTrue();
        BaseModuleProgramValue sum = evaluator.Evaluate(new BaseModuleBinaryNumericExpression
        {
            Id = "amount-plus-one",
            ResultTypeId = "int64",
            Operator = BaseModuleNumericOperator.IntegerAddChecked,
            Left = Request("request.amount", "amount"),
            Right = Constant("one", "1"u8),
        });

        sum.Value.GetInt64().Should().Be(42);
        EvaluatorResult result = evaluator.ProjectResult(
            definition.Template.Result,
            new Dictionary<string, BaseRecordMutationFact>(),
            new Dictionary<string, BaseModuleCommittedGeneration>(),
            out ImmutableArray<byte> bytes);
        result.Amount.Should().Be(41);
        bytes.Should().Equal("{\"Amount\":41}"u8.ToArray());
    }

    private static BaseGeneratedModuleMutationIdentity<EvaluatorRequest, EvaluatorResult> Identity() => new(
        "module.test", 1, new byte[32], EvaluatorJsonContext.Default.EvaluatorRequest,
        EvaluatorJsonContext.Default.EvaluatorResult,
        [
            BaseModuleDtoPropertyBinding.Create<EvaluatorRequest, long>("request.amount", nameof(EvaluatorRequest.Amount)),
            BaseModuleDtoPropertyBinding.Create<EvaluatorRequest, bool>("request.enabled", nameof(EvaluatorRequest.Enabled)),
        ],
        [BaseModuleDtoPropertyBinding.Create<EvaluatorResult, long>("result.amount", nameof(EvaluatorResult.Amount))]);

    private static BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> GenerationIdentity() => new(
        "module.increment", 1, new byte[32], EvaluatorJsonContext.Default.GenerationRequest,
        EvaluatorJsonContext.Default.GenerationResult, [],
        [BaseModuleDtoPropertyBinding.Create<GenerationResult, string>("result.generation", nameof(GenerationResult.Generation))]);

    private static BaseGeneratedModuleMutationIdentity<CreateRequest, CreateResult> CreateIdentity() => new(
        "module.create", 1, new byte[32], EvaluatorJsonContext.Default.CreateRequest, EvaluatorJsonContext.Default.CreateResult,
        [
            BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.id", nameof(CreateRequest.Id)),
            BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.name", nameof(CreateRequest.Name)),
        ],
        [BaseModuleDtoPropertyBinding.Create<CreateResult, string>("result.id", nameof(CreateResult.Id))]);

    private static BaseRegisteredModuleMutationDefinition CreateDefinition() => BaseModuleMutationContract.Seal(new()
    {
        Id = "module.create", Version = 1, OwningModuleId = "module", GrantId = "module.create",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = ["module-records"],
        SystemSourceGrants = [new() { CollectionId = "module-records", GrantId = "module.records.source" }],
        GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleRecordCapture { Id = "record", CollectionId = "module-records", Presence = BaseModuleCapturePresence.RequireMissing, RecordId = Request("request.id", "id") }],
            Guards = [],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    new BaseModuleCreateStatement
                    {
                        Id = "create", CollectionId = "module-records", RecordId = Request("request.id", "create-id"),
                        Payload = new BaseModuleObjectExpression
                        {
                            Id = "payload", ResultTypeId = "record", Properties =
                            [new BaseModuleObjectPropertyExpression { StablePropertyId = "field.name", Value = Request("request.name", "name") }],
                        },
                    },
                    new BaseModulePatchStatement
                    {
                        Id = "patch", CollectionId = "module-records", RecordId = Request("request.id", "patch-id"),
                        Patch = new BaseModuleObjectExpression
                        {
                            Id = "patch-payload", ResultTypeId = "record", Properties =
                            [new BaseModuleObjectPropertyExpression { StablePropertyId = "field.name", Value = Constant("grace", "\"Grace\""u8) }],
                        },
                    },
                ],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "result", Properties =
                    [new BaseModuleObjectPropertyExpression { StablePropertyId = "result.id", Value = new BaseModuleCommittedRecordIdExpression { Id = "committed-id", ResultTypeId = "string", StatementId = "create" } }],
                },
            },
        },
        Limits = Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    });

    private static CollectionDefinition ModuleCollection() => new()
    {
        Id = "module-records", Name = "module-records", Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, MutationMode = BaseCollectionMutationMode.Mutable,
        System = true, SystemOwnerModuleId = "module",
        Fields = [new FieldDefinition { Id = "field.name", ApplicationName = "Name", WireName = "name", Type = "string", Required = true }],
    };

    private static DefaultBasePolicyOrchestrator Policy(params string[] grantIds)
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
        foreach (string grantId in grantIds)
        {
            builder.AddStaticGrant(new BaseGrantAuthorityDefinition
            {
                Id = grantId, Version = 1, OwningModuleId = "module",
                SourceContractId = "module.grants", SourceContractVersion = 1,
            }, new AccessGrant
            {
                Id = grantId,
                ApplicationId = "module.application", ModuleId = "module", Audience = HPDBaseEndpointAudience.ControlPlane,
                Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
                Action = string.Equals(grantId, "module.records.source", StringComparison.Ordinal) ? "module-records" : grantId,
                Scope = string.Equals(grantId, "module.records.source", StringComparison.Ordinal)
                    ? new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = "module-records" }
                    : new ResourceScope { Kind = ResourceScopeKind.Runtime },
            });
        }
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private static BaseActivationGrantSet TransactionalActivationGrants() => new()
    {
        Enqueue = "module.transactional.enqueue", Observe = "module.transactional.observe",
        Claim = "module.transactional.claim", Execute = "module.transactional.execute",
        Renew = "module.transactional.renew", Complete = "module.transactional.complete",
        Fail = "module.transactional.fail", Cancel = "module.transactional.cancel",
        Inspect = "module.transactional.inspect", Replay = "module.transactional.replay",
        Migrate = "module.transactional.migrate", Reconcile = "module.transactional.reconcile",
        Retry = "module.transactional.retry", Dispose = "module.transactional.dispose",
        Remove = "module.transactional.remove", Repair = "module.transactional.repair",
    };

    private static BaseActivationExecutionLimits ActivationProviderLimits() => new()
    {
        MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 8192, MaximumTransientBytes = 16384,
        MaximumReadIntervals = 8, MaximumIndexOperations = 16,
        AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };

    private static DefaultBasePolicyOrchestrator TransactionalActivationPolicy()
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
        void Add(string id, string action) => builder.AddStaticGrant(new BaseGrantAuthorityDefinition
        {
            Id = id, Version = 1, OwningModuleId = "module",
            SourceContractId = "module.grants", SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = id, ApplicationId = "module.application", ModuleId = "module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = action, Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });
        BaseActivationGrantSet grants = TransactionalActivationGrants();
        foreach (string id in new[] { grants.Enqueue, grants.Observe, grants.Claim, grants.Execute, grants.Renew,
                     grants.Complete, grants.Fail, grants.Cancel, grants.Inspect, grants.Replay, grants.Migrate,
                     grants.Reconcile, grants.Retry, grants.Dispose, grants.Remove, grants.Repair })
            Add(id, "module.transactional");
        Add("module.increment", "module.increment");
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private static DefaultBasePolicyOrchestrator PolicyWithGrant(AccessGrant grant)
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
        builder.AddStaticGrant(new BaseGrantAuthorityDefinition
        {
            Id = grant.Id, Version = 1, OwningModuleId = "module",
            SourceContractId = "module.grants", SourceContractVersion = 1,
        }, grant);
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private static DefaultBasePolicyOrchestrator PolicyWithDynamicGrant(AccessGrant grant)
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
        var source = new FixedGrantSource(grant);
        source.Registration = builder.AddGrant(new BaseGrantAuthorityDefinition
        {
            Id = grant.Id, Version = 1, OwningModuleId = "module",
            SourceContractId = "module.dynamic-grants", SourceContractVersion = 1,
        }, source);
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private sealed class FixedGrantSource(AccessGrant grant) : IBaseGrantAuthoritySource
    {
        internal BaseInstalledGrantRegistration Registration { get; set; } = null!;
        public ValueTask EmitAsync(BaseGrantAuthorityEmissionContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Emit(Registration, grant);
            return ValueTask.CompletedTask;
        }
    }

    private static BaseRegisteredModuleMutationDefinition GenerationDefinition() => BaseModuleMutationContract.Seal(new()
    {
        Id = "module.increment", Version = 1, OwningModuleId = "module", GrantId = "module.increment",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = ["module.generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures =
            [
                new BaseModuleGenerationCapture
                {
                    Id = "generation", CellId = "module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither,
                },
            ],
            Guards = [],
            Body = new BaseModuleMutationBlock
            {
                Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "result",
                    Properties =
                    [
                        new BaseModuleObjectPropertyExpression
                        {
                            StablePropertyId = "result.generation",
                            Value = new BaseModuleResultingGenerationExpression { Id = "result-generation", ResultTypeId = "string", CaptureId = "generation" },
                        },
                    ],
                },
            },
        },
        Limits = Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    });

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8,
        MaximumGenerationCaptures = 8, MaximumRecordMutations = 8, MaximumGenerationReads = 8,
        MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8, MaximumGuardNodes = 8,
        MaximumGuardDepth = 8, MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32,
        MaximumReadIntervals = 16, MaximumSubjectValidations = 8, MaximumAuthorityReads = 16,
        MaximumRelationChecks = 8, MaximumUniqueConstraintChecks = 8, MaximumRequestBytes = 4096,
        MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 4096,
        MaximumWrittenBytes = 4096, MaximumFactBytes = 4096, MaximumJournalBytes = 4096,
        MaximumReceiptBytes = 4096, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };

    private static BaseRegisteredModuleMutationDefinition Definition() => new()
    {
        Id = "module.test", Version = 1, OwningModuleId = "module", GrantId = "module.execute",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [],
            Guards =
            [
                new BaseModuleRecordPresenceGuard
                {
                    Id = "enabled",
                    CaptureId = "existing",
                    MustBePresent = true,
                },
            ],
            Body = new BaseModuleMutationBlock
            {
                Statements = [new BaseModuleRequireStatement { Id = "require", GuardId = "enabled", RequirementId = "enabled" }],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "result",
                    Properties =
                    [
                        new BaseModuleObjectPropertyExpression
                        {
                            StablePropertyId = "result.amount",
                            Value = Request("request.amount", "result-amount"),
                        },
                    ],
                },
            },
        },
        Limits = null!, ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    };

    private static BaseModuleRequestPropertyExpression Request(string stableId, string id) => new()
    {
        Id = id, ResultTypeId = "int64",
        Property = new BaseModuleRequestPropertyReference { StablePropertyPath = [stableId], DeclaredTypeId = "int64" },
    };

    private static BaseModuleConstantExpression Constant(string id, ReadOnlySpan<byte> bytes) => new()
    {
        Id = id, ResultTypeId = "json", CanonicalBaseJson = bytes.ToArray().ToImmutableArray(),
    };

    private static BaseCapturedAtomicExecution Captured() => new()
    {
        Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
        IntentDigest = "intent", CaptureDigest = "capture", Authority = null!,
        Items = [],
        ModuleRecords =
        [
            new BaseCapturedModuleRecord
            {
                Ordinal = 0, CaptureId = "existing", CollectionId = "records", RecordId = new RecordId("one"), Exists = true,
                Current = new RecordEnvelope
                {
                    CollectionId = "records", Id = new RecordId("one"),
                    Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, System.Text.Json.JsonElement>() },
                    Metadata = new RecordMetadata(),
                },
            },
        ],
        ModuleRelationTargets = [], Generations = [], ReadIntervals = [],
        Accounting = new BaseAtomicCaptureAccounting
        {
            Records = 0, RelationTargetReads = 0, GenerationReads = 0, SelectedBytes = 0,
            RelationTargetBytes = 0, GenerationBytes = 0, ReadIntervals = 0, EvidenceBytes = 0, TransientBytes = 0,
            RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
        },
    };
}

public sealed record EvaluatorRequest
{
    public required long Amount { get; init; }
    public required bool Enabled { get; init; }
}

public sealed record EvaluatorResult
{
    public required long Amount { get; init; }
}

public sealed record GenerationRequest;
public sealed record GenerationResult { public required string Generation { get; init; } }
public sealed record CreateRequest { public required string Id { get; init; } public required string Name { get; init; } }
public sealed record CreateResult { public required string Id { get; init; } }
public sealed record UnsupportedResult { public required DateTimeOffset Id { get; init; } }

[JsonSerializable(typeof(EvaluatorRequest))]
[JsonSerializable(typeof(EvaluatorResult))]
[JsonSerializable(typeof(GenerationRequest))]
[JsonSerializable(typeof(GenerationResult))]
[JsonSerializable(typeof(CreateRequest))]
[JsonSerializable(typeof(CreateResult))]
[JsonSerializable(typeof(UnsupportedResult))]
internal sealed partial class EvaluatorJsonContext : JsonSerializerContext;
