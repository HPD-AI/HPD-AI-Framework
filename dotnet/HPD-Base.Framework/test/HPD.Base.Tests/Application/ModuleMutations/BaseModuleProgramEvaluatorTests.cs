using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HPD.Base.Tests.Subjects;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class BaseModuleProgramEvaluatorTests
{
    [Fact]
    public void Public_builder_surface_does_not_expose_raw_scalar_authority_or_expression_inputs()
    {
        Type[] forbidden =
        [
            typeof(BaseModuleValueType), typeof(BaseModuleValueExpression),
            typeof(BaseModuleRequestPropertyReference), typeof(BaseModuleCapturedFieldReference),
            typeof(BaseModuleObjectExpression), typeof(BaseModuleObjectPropertyExpression),
        ];

        typeof(BaseModuleMutationTemplateBuilder).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SelectMany(static method => method.GetParameters())
            .Should().NotContain(parameter => forbidden.Contains(parameter.ParameterType)
                || parameter.ParameterType.IsArray && forbidden.Contains(parameter.ParameterType.GetElementType()));

        Type[] opaqueNodes =
        [
            typeof(BaseModuleRecordCapture), typeof(BaseModuleGenerationCapture),
            typeof(BaseModuleRevisionEqualsGuard), typeof(BaseModuleFieldEqualsGuard),
            typeof(BaseModuleFieldComparisonGuard), typeof(BaseModuleFieldPresenceGuard),
            typeof(BaseModuleGenerationGuard), typeof(BaseModuleValueEqualsGuard),
            typeof(BaseModuleValueComparisonGuard), typeof(BaseModuleValuePresenceGuard),
            typeof(BaseModuleSetGuard), typeof(BaseModulePrecondition),
            typeof(BaseModuleRecordIdConversionExpression),
            typeof(BaseModuleGenerationKeyFromGuidExpression),
            typeof(BaseModuleMissingExpression), typeof(BaseModulePresenceLiftExpression),
            typeof(BaseModuleConditionalExpression), typeof(BaseModuleCoalesceExpression),
            typeof(BaseModuleIncarnationBytesExpression),
            typeof(BaseModuleSemanticActivationDispositionExpression),
            typeof(BaseModuleSemanticActivationIdExpression),
            typeof(BaseModuleSemanticActivationWasMaterializedExpression),
            typeof(BaseModuleSemanticActivationRetirementDispositionExpression),
            typeof(BaseModuleCreateStatement),
            typeof(BaseModulePatchStatement), typeof(BaseModuleReplaceStatement),
            typeof(BaseModuleDeleteStatement), typeof(BaseModuleUpsertStatement),
            typeof(BaseModuleResultProjection),
        ];

        foreach (Type type in forbidden.Where(static type => type != typeof(BaseModuleValueType)).Concat(opaqueNodes))
            type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Should().BeEmpty(type.FullName);

        opaqueNodes.Where(static type => !typeof(BaseModuleValueExpression).IsAssignableFrom(type))
            .SelectMany(static type => type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            .Should().NotContain(property => forbidden.Contains(property.PropertyType),
                "opaque graph nodes must not reveal reusable raw expressions or authority");
    }

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
        actual.Should().Be("4A7B2BFFC1E34976A4125D9BAB73336FCF3C8DC97078206A41DF3CBEFC4B5386");
    }

    [Fact]
    public void Ordered_field_guard_checksum_matches_the_locked_template_byte_vector()
    {
        BaseRegisteredModuleMutationDefinition source = GenerationDefinition();
        BaseRegisteredModuleMutationDefinition definition = source with
        {
            Template = source.Template with
            {
                Guards =
                [
                    new BaseModuleFieldComparisonGuard
                    {
                        Id = "counter-increases",
                        Field = new BaseModuleCapturedFieldReference
                        {
                            CaptureId = "existing", StableFieldId = "record.counter", Authority = Type<long>(),
                        },
                        Comparison = BaseModuleOrderedComparisonKind.LessThan,
                        Expected = new BaseModuleConstantExpression
                        {
                            Id = "next-counter", ResultType = Type<long>(),
                            CanonicalBaseJson = "42"u8.ToArray().ToImmutableArray(),
                        },
                    },
                ],
            },
        };

        string ordered = Convert.ToHexString(BaseModuleMutationContract.ComputeChecksum(definition).ToArray());
        ordered
            .Should().Be("36E7991690CE7E36124D8637B70D5AD5AF4E1068D97C0E970AB26DAF1329F5DA");
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
            new() { Ordinal = 0, SourceStatementId = "write-a", SourceFieldId = "owner", TargetCollection = target, TargetRecordId = RecordId.Create("a") },
            new() { Ordinal = 1, SourceStatementId = "write-b", SourceFieldId = "owner", TargetCollection = target, TargetRecordId = RecordId.Create("b") },
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
            intent, extension, null, DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(Limits()), valid).Should().BeTrue();

        BaseCapturedAtomicExecution[] hostile =
        [
            Reframe(valid, [valid.ModuleRelationTargets[0]]),
            Reframe(valid, [.. valid.ModuleRelationTargets, valid.ModuleRelationTargets[1] with { Ordinal = 2 }]),
            Reframe(valid, [valid.ModuleRelationTargets[1] with { Ordinal = 0 }, valid.ModuleRelationTargets[0] with { Ordinal = 1 }]),
        ];
        foreach (BaseCapturedAtomicExecution evidence in hostile)
            BaseModuleMutationProcessor<CreateRequest, CreateResult>.CapturedMatches(
                intent, extension, null, DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(Limits()), evidence).Should().BeFalse();
    }

    [Fact]
    public void Lifecycle_projection_capture_rejects_missing_substituted_and_non_point_evidence()
    {
        ImmutableArray<BaseSubjectLifecycleConsumerProjectionCaptureRequest> expected =
        [
            new()
            {
                ConsumerId = "module.lifecycle.consumer",
                ConsumerVersion = 2,
                ConsumerChecksum = new string('a', 64),
                ContractId = "module.subject",
                ContractVersion = 3,
            },
            new()
            {
                ConsumerId = "module.lifecycle.consumer-b",
                ConsumerVersion = 1,
                ConsumerChecksum = new string('d', 64),
                ContractId = "example.sqlite-user",
                ContractVersion = 1,
            },
        ];
        BaseCapturedAtomicExecution valid = LifecycleEvidence(expected);

        BaseModuleMutationProcessor<CreateRequest, CreateResult>.LifecycleCapturedMatches(expected, valid)
            .Should().BeTrue();

        BaseCapturedSubjectLifecycleConsumerProjection projection = valid.LifecycleConsumerProjections[0];
        BaseCapturedAtomicExecution[] hostile =
        [
            valid with { LifecycleConsumerProjections = [] },
            valid with { LifecycleConsumerProjections = [projection with { ConsumerChecksum = new string('b', 64) }, valid.LifecycleConsumerProjections[1]] },
            valid with { LifecycleConsumerProjections = [projection with { ContractId = "module.other-subject" }, valid.LifecycleConsumerProjections[1]] },
            valid with { LifecycleConsumerProjections = [projection with { ProjectionGeneration = 0 }, valid.LifecycleConsumerProjections[1]] },
            valid with { LifecycleConsumerProjections = [projection with { PublishedGraphGeneration = 0 }, valid.LifecycleConsumerProjections[1]] },
            valid with { ReadIntervals = [] },
            valid with
            {
                ReadIntervals = [valid.ReadIntervals[0] with { UpperInclusive = false }, valid.ReadIntervals[1]],
            },
            valid with { ReadIntervals = [valid.ReadIntervals[0], valid.ReadIntervals[0], valid.ReadIntervals[1]] },
            valid with { ReadIntervals = [valid.ReadIntervals[0], valid.ReadIntervals[1], valid.ReadIntervals[1]] },
            valid with { ReadIntervals = [valid.ReadIntervals[1], valid.ReadIntervals[0]] },
        ];

        foreach (BaseCapturedAtomicExecution captured in hostile)
            BaseModuleMutationProcessor<CreateRequest, CreateResult>.LifecycleCapturedMatches(expected, captured)
                .Should().BeFalse();
    }

    [Fact]
    public void Lifecycle_projection_retained_work_is_exact_and_enforces_transient_limit()
    {
        ImmutableArray<BaseSubjectLifecycleConsumerProjectionCaptureRequest> expected =
        [
            new()
            {
                ConsumerId = "module.lifecycle.consumer",
                ConsumerVersion = 2,
                ConsumerChecksum = new string('a', 64),
                ContractId = "module.subject",
                ContractVersion = 3,
            },
        ];
        BaseCapturedAtomicExecution valid = LifecycleEvidence(expected);
        BaseAtomicMutationAuthorityRequirement requirement = AuthorityRequirement();
        var intent = new BaseAtomicMutationIntent
        {
            IntentDigest = "intent",
            Authority = requirement,
            Items = [],
        };
        var extension = new BaseModuleMutationCaptureExtension
        {
            OperationId = "module.create",
            OperationVersion = 1,
            OperationChecksum = new string('a', 64),
            RequestDigest = new string('b', 64),
            Records = [],
            RelationTargets = [],
            Generations = [],
        };
        BaseAtomicMutationExecutionLimits limits = DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(Limits());

        BaseModuleMutationProcessor<CreateRequest, CreateResult>.CapturedMatches(
            intent, extension, null, limits, valid).Should().BeTrue();
        BaseModuleMutationProcessor<CreateRequest, CreateResult>.CapturedMatches(
            intent, extension, null,
            limits with { MaximumTransientBytes = valid.Accounting.TransientBytes - 1 },
            valid).Should().BeFalse();
        BaseModuleMutationProcessor<CreateRequest, CreateResult>.CapturedMatches(
            intent, extension, null, limits,
            valid with { Accounting = valid.Accounting with { TransientBytes = valid.Accounting.TransientBytes - 1 } })
            .Should().BeFalse();
    }

    [Fact]
    public void Finalized_graph_requires_every_module_operation_to_admit_fixed_lifecycle_projection_work()
    {
        var consumer = new BaseInstalledSubjectLifecycleConsumer(
            new BaseSubjectLifecycleConsumerDefinition
            {
                Id = "module.lifecycle.consumer",
                Version = 2,
                OwningModuleId = "module.consumer",
                Audience = BaseSubjectLifecycleConsumerAudience.Service,
                ContractId = "example.sqlite-user",
                ContractVersion = 1,
                ObservedStates = [BaseSubjectLifecycleState.Active],
                DeliveryGrantId = "module.lifecycle.deliver",
                Limits = new BaseSubjectLifecycleConsumerLimits
                {
                    MaximumFactsPerPage = 1,
                    MaximumResultBytes = 1,
                    MaximumCheckpointLag = TimeSpan.FromHours(1),
                    ReadTimeout = TimeSpan.FromMilliseconds(100),
                },
            },
            new string('a', 64));
        BaseRegisteredModuleMutationDefinition source = CreateDefinition();
        ImmutableArray<BaseAtomicReadIntervalEvidence> intervals =
        [
            new()
            {
                LogicalAccessPathId = "collection:module-records:record",
                CanonicalLowerBound = [(byte)'a'],
                LowerInclusive = true,
                CanonicalUpperBound = [(byte)'a'],
                UpperInclusive = true,
            },
            new()
            {
                LogicalAccessPathId = "subject-lifecycle:consumer-projection",
                CanonicalLowerBound = System.Text.Encoding.UTF8.GetBytes(
                    $"{consumer.Definition.Id}\0{consumer.Definition.Version}").ToImmutableArray(),
                LowerInclusive = true,
                CanonicalUpperBound = System.Text.Encoding.UTF8.GetBytes(
                    $"{consumer.Definition.Id}\0{consumer.Definition.Version}").ToImmutableArray(),
                UpperInclusive = true,
            },
        ];
        ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection> projections =
        [
            new()
            {
                ConsumerId = consumer.Definition.Id,
                ConsumerVersion = consumer.Definition.Version,
                ConsumerChecksum = consumer.Checksum,
                ContractId = consumer.Definition.ContractId,
                ContractVersion = consumer.Definition.ContractVersion,
                ProjectionGeneration = 1,
                PublishedGraphGeneration = 1,
            },
        ];
        long evidence = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
        long transient = evidence
            + BaseSubjectCanonicalRetainedWork.MeasureLifecycleConsumerProjections(projections);
        BaseRegisteredModuleMutationDefinition exact = BaseModuleMutationContract.Seal(source with
        {
            Limits = source.Limits with
            {
                MaximumReadIntervals = intervals.Length,
                MaximumEvidenceBytes = evidence,
                MaximumTransientBytes = transient,
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        });

        Action accepted = () => BuildLifecycleCapacityGraph(exact, consumer.Definition);
        accepted.Should().NotThrow();

        BaseRegisteredModuleMutationDefinition[] insufficient =
        [
            BaseModuleMutationContract.Seal(exact with { Limits = exact.Limits with { MaximumReadIntervals = exact.Limits.MaximumReadIntervals - 1 }, Checksum = BaseModuleMutationChecksum.Create(new byte[32]) }),
            BaseModuleMutationContract.Seal(exact with { Limits = exact.Limits with { MaximumEvidenceBytes = evidence - 1 }, Checksum = BaseModuleMutationChecksum.Create(new byte[32]) }),
            BaseModuleMutationContract.Seal(exact with { Limits = exact.Limits with { MaximumTransientBytes = transient - 1 }, Checksum = BaseModuleMutationChecksum.Create(new byte[32]) }),
        ];
        foreach (BaseRegisteredModuleMutationDefinition operation in insufficient)
        {
            Action rejected = () => BuildLifecycleCapacityGraph(operation, consumer.Definition);
            rejected.Should().Throw<InvalidOperationException>()
                .WithMessage(BaseModuleMutationErrorCodes.CapabilityMissing);
        }
    }

    [Fact]
    public void Finalized_graph_generation_capture_capacity_uses_the_provider_canonical_key()
    {
        BaseSubjectLifecycleConsumerDefinition consumer = LifecycleCapacityConsumer();
        var installed = new BaseInstalledSubjectLifecycleConsumer(consumer, new string('a', 64));
        BaseModuleGenerationCellDefinition cell = GenerationCell();
        byte[] generationKey = BaseModuleGenerationStorageKey.Minimum(cell, 0);
        generationKey.Should().Equal(BaseModuleGenerationStorageKey.Encode(
            cell, new BaseModuleGenerationScopeAuthority { Kind = BaseModuleGenerationScope.Application }, []));
        ImmutableArray<BaseAtomicReadIntervalEvidence> intervals =
        [
            Point("module-generation", generationKey),
            Point("subject-lifecycle:consumer-projection", System.Text.Encoding.UTF8.GetBytes(
                $"{consumer.Id}\0{consumer.Version}")),
        ];
        ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection> projections =
        [new()
        {
            ConsumerId = consumer.Id, ConsumerVersion = consumer.Version,
            ConsumerChecksum = installed.Checksum, ContractId = consumer.ContractId,
            ContractVersion = consumer.ContractVersion, ProjectionGeneration = 1,
            PublishedGraphGeneration = 1,
        }];
        long evidence = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
        long transient = evidence + BaseSubjectCanonicalRetainedWork.MeasureLifecycleConsumerProjections(projections);
        BaseRegisteredModuleMutationDefinition source = GenerationDefinition();
        BaseRegisteredModuleMutationDefinition exact = BaseModuleMutationContract.Seal(source with
        {
            Limits = source.Limits with
            {
                MaximumReadIntervals = intervals.Length,
                MaximumEvidenceBytes = evidence,
                MaximumTransientBytes = transient,
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        });

        Action accepted = () => BuildLifecycleCapacityGraph(exact, consumer);
        accepted.Should().NotThrow();
        foreach (BaseRegisteredModuleMutationDefinition insufficient in new[]
        {
            BaseModuleMutationContract.Seal(exact with { Limits = exact.Limits with { MaximumReadIntervals = intervals.Length - 1 }, Checksum = BaseModuleMutationChecksum.Create(new byte[32]) }),
            BaseModuleMutationContract.Seal(exact with { Limits = exact.Limits with { MaximumEvidenceBytes = evidence - 1 }, Checksum = BaseModuleMutationChecksum.Create(new byte[32]) }),
            BaseModuleMutationContract.Seal(exact with { Limits = exact.Limits with { MaximumTransientBytes = transient - 1 }, Checksum = BaseModuleMutationChecksum.Create(new byte[32]) }),
        })
        {
            Action rejected = () => BuildLifecycleCapacityGraph(insufficient, consumer);
            rejected.Should().Throw<InvalidOperationException>()
                .WithMessage(BaseModuleMutationErrorCodes.CapabilityMissing);
        }

        static BaseAtomicReadIntervalEvidence Point(string path, byte[] key) => new()
        {
            LogicalAccessPathId = path,
            CanonicalLowerBound = key.ToImmutableArray(), LowerInclusive = true,
            CanonicalUpperBound = key.ToImmutableArray(), UpperInclusive = true,
        };
    }

    private static void BuildLifecycleCapacityGraph(
        BaseRegisteredModuleMutationDefinition operation,
        BaseSubjectLifecycleConsumerDefinition consumer)
    {
        var services = new ServiceCollection();
        var builder = new HPDBaseBuilder(services);
        BaseGeneratedSubjectLifecycleConsumerIdentity<L45SqliteUserSubject> lifecycle =
            BaseGeneratedSubjectLifecycleConsumers.Register<L45SqliteUserSubject>(
                consumer, L45SqliteUserSubject.HPDBaseSubjectRegistration);
        builder.ConfigureSchema(static options => options.ApplicationId = "module.application")
            .ConfigureTokenProtection(static options => options.ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1,
                Key = Enumerable.Repeat((byte)7, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            })
            .AddCollection(ModuleMutationRecord.Collection)
            .AddCollection(L45SqlitePrivateUser.Collection)
            .AddExportedSubject(L45SqliteUserSubject.HPDBaseSubjectRegistration)
            .AddSubjectLifecycleConsumer(lifecycle);
        if (operation.GenerationCellIds.Length != 0)
            builder.AddModuleGenerationCell(GenerationCell())
                .AddModuleMutation(operation, GenerationIdentity(operation));
        else
            builder.AddModuleMutation(operation, CreateIdentity(operation));
        builder.Build();
    }

    private static BaseSubjectLifecycleConsumerDefinition LifecycleCapacityConsumer() => new()
    {
        Id = "module.lifecycle.consumer", Version = 2, OwningModuleId = "module.consumer",
        Audience = BaseSubjectLifecycleConsumerAudience.Service,
        ContractId = "example.sqlite-user", ContractVersion = 1,
        ObservedStates = [BaseSubjectLifecycleState.Active], DeliveryGrantId = "module.lifecycle.deliver",
        Limits = new BaseSubjectLifecycleConsumerLimits
        {
            MaximumFactsPerPage = 1, MaximumResultBytes = 1,
            MaximumCheckpointLag = TimeSpan.FromHours(1), ReadTimeout = TimeSpan.FromMilliseconds(100),
        },
    };

    private static BaseModuleGenerationCellDefinition GenerationCell() => new()
    {
        Id = "module.generation", Version = 1, OwningModuleId = "module",
        Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 1,
        MaximumCellsPerOperation = 1,
    };

    [Fact]
    public void Read_only_subject_retirement_capture_is_not_mapped_to_an_unrelated_mutation()
    {
        ImmutableArray<BaseCapturedSubjectRetirementProjection> captured =
        [
            RetirementProjection(sourceCaptureOrdinal: 0, subjectId: "read-only-subject"),
            RetirementProjection(sourceCaptureOrdinal: 1, subjectId: "mutated-subject"),
        ];
        ImmutableArray<BaseModuleMutationItemCaptureBinding> bindings =
        [
            new() { MutationOrdinal = 0, RecordCaptureOrdinal = 1 },
        ];

        ImmutableArray<BaseCapturedSubjectRetirementProjection> mapped =
            BaseModuleMutationProcessor<CreateRequest, CreateResult>.MapRetirementCaptures(captured, bindings);

        BaseCapturedSubjectRetirementProjection projection = mapped.Should().ContainSingle().Subject;
        projection.SubjectId.Value.Should().Be("mutated-subject");
        projection.SourceMutationOrdinal.Should().Be(0);
    }

    private static BaseCapturedSubjectRetirementProjection RetirementProjection(
        int sourceCaptureOrdinal,
        string subjectId) => new()
    {
        SourceMutationOrdinal = sourceCaptureOrdinal,
        ContractId = "module.subject",
        ContractVersion = 1,
        ContractChecksum = new string('a', 64),
        RetirementPolicyChecksum = new string('b', 64),
        AcceptedConsumerSetChecksum = new string('c', 64),
        SubjectId = BaseSubjectId.Create(subjectId, BaseSubjectIdKind.OrdinalString),
        ProtectedScope = new BaseProtectedSubjectScope
        {
            Kind = BaseSubjectScopeKind.Tenant,
            IndexDigest = Enumerable.Repeat((byte)1, 32).ToArray(),
            ProtectedCanonicalValue = [2],
        },
        AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)4, 16).ToArray()),
        Incarnation = BaseSubjectIncarnation.Create(1),
        CurrentSubjectSequence = 1,
        CurrentState = BaseSubjectLifecycleState.Active,
    };

    private static BaseCapturedAtomicExecution LifecycleEvidence(
        ImmutableArray<BaseSubjectLifecycleConsumerProjectionCaptureRequest> expected)
    {
        ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection> projections =
        [.. expected.Select(static request => new BaseCapturedSubjectLifecycleConsumerProjection
        {
            ConsumerId = request.ConsumerId,
            ConsumerVersion = request.ConsumerVersion,
            ConsumerChecksum = request.ConsumerChecksum,
            ContractId = request.ContractId,
            ContractVersion = request.ContractVersion,
            ProjectionGeneration = 7,
            PublishedGraphGeneration = 11,
        })];
        ImmutableArray<BaseAtomicReadIntervalEvidence> intervals =
        [.. expected.Select(static request =>
        {
            ImmutableArray<byte> key = System.Text.Encoding.UTF8.GetBytes(
                $"{request.ConsumerId}\0{request.ConsumerVersion}").ToImmutableArray();
            return new BaseAtomicReadIntervalEvidence
            {
                LogicalAccessPathId = "subject-lifecycle:consumer-projection",
                CanonicalLowerBound = key,
                LowerInclusive = true,
                CanonicalUpperBound = key,
                UpperInclusive = true,
            };
        })];
        long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
        long transientBytes = checked(evidenceBytes
            + BaseSubjectCanonicalRetainedWork.MeasureLifecycleConsumerProjections(projections));
        BaseAtomicMutationAuthorityRequirement requirement = AuthorityRequirement();
        return new BaseCapturedAtomicExecution
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            IntentDigest = "intent",
            CaptureDigest = new string('c', 64),
            Authority = new BaseAtomicMutationAuthorityEvidence
            {
                ApplicationId = requirement.ApplicationId,
                StoreInstanceId = requirement.StoreInstanceId,
                RestoreEpoch = requirement.RestoreEpoch,
                SchemaGeneration = requirement.SchemaGeneration,
                LogicalSchemaChecksum = requirement.LogicalSchemaChecksum,
                Collections = requirement.Collections,
                Isolation = BaseAtomicSelectionIsolationClass.NativeSerializable,
                TransactionEvidenceToken = [1],
            },
            Items = [],
            ModuleRecords = [],
            ModuleRelationTargets = [],
            Generations = [],
            LifecycleConsumerProjections = projections,
            ReadIntervals = intervals,
            Accounting = new BaseAtomicCaptureAccounting
            {
                Records = 0,
                RelationTargetReads = 0,
                GenerationReads = 0,
                SelectedBytes = 0,
                RelationTargetBytes = 0,
                GenerationBytes = 0,
                ReadIntervals = intervals.Length,
                EvidenceBytes = evidenceBytes,
                TransientBytes = transientBytes,
                RetirementBarrierReads = 0,
                RetirementAcknowledgementReads = 0,
                RetirementProjections = 0,
                RetirementPublications = 0,
                RetirementEvidenceBytes = 0,
                RetirementPublicationBytes = 0,
            },
        };
    }

    private static BaseAtomicMutationAuthorityRequirement AuthorityRequirement() => new()
    {
        ApplicationId = "module.application", StoreInstanceId = "module-store", RestoreEpoch = 1,
        SchemaGeneration = 1, LogicalSchemaChecksum = BaseSchemaAuthorityChecksum.Create(new byte[32]), Collections = [],
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
                LogicalSchemaChecksum = requirement.LogicalSchemaChecksum,
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
                ReadIntervals = intervals.Length, EvidenceBytes = evidenceBytes,
                TransientBytes = evidenceBytes
                    + BaseSubjectCanonicalRetainedWork.MeasureLifecycleConsumerProjections([]),
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
                Captures = [BaseModuleMutationTemplateBuilder.CaptureGenerationRaw(
                    "generation", "module.generation", null, BaseModuleGenerationAbsenceBehavior.AllowEither)],
                Guards = [],
                Preconditions = [],
                Body = BaseModuleMutationTemplateBuilder.Block(
                    BaseModuleMutationTemplateBuilder.IncrementGeneration("increment", "generation", true)),
                Result = BaseModuleMutationTemplateBuilder.ResultRaw(
                    BaseModuleMutationTemplateBuilder.Object("result",
                        BaseModuleMutationTemplateBuilder.Property("result.generation",
                            new BaseModuleResultingGenerationExpression
                            {
                                Id = "result-generation", ResultType = Dto<string>("result.generation").ValueType,
                                CaptureId = "generation",
                            }))),
            },
        }).Build();

        authored.Checksum.Should().Be(direct.Checksum);
    }

    [Fact]
    public void Every_manual_factory_matches_its_direct_closed_union_shape()
    {
        BaseModuleValueType integer = Type<long>();
        BaseModuleValueType recordId = Type<string>();
        BaseModuleDtoScalarAuthority requestAuthority = Dto<long>("request.value");
        BaseModuleValueExpression constant = BaseModuleMutationTemplateBuilder.Constant("constant", integer, "1"u8);
        var requestReference = new BaseModuleRequestPropertyReference { StablePropertyPath = ["request.value"], Authority = requestAuthority };
        var fieldReference = new BaseModuleCapturedFieldReference { CaptureId = "record", StableFieldId = "record.value", Authority = integer };
        BaseModuleObjectExpression payload = BaseModuleMutationTemplateBuilder.Object("payload",
            BaseModuleMutationTemplateBuilder.Property("payload.value", constant));
        BaseModuleMutationBlock empty = BaseModuleMutationTemplateBuilder.Block();

        (object Factory, object Direct)[] pairs =
        [
            (BaseModuleMutationTemplateBuilder.CaptureRecord("record", "records", constant, BaseModuleCapturePresence.RequirePresent),
                new BaseModuleRecordCapture { Id = "record", CollectionId = "records", RecordId = constant, Presence = BaseModuleCapturePresence.RequirePresent }),
            (BaseModuleMutationTemplateBuilder.CaptureGenerationRaw("generation", "module.generation", constant, BaseModuleGenerationAbsenceBehavior.AllowEither),
                new BaseModuleGenerationCapture { Id = "generation", CellId = "module.generation", Key = constant, Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }),
            (BaseModuleMutationTemplateBuilder.RecordPresent("record-present", "record", true),
                new BaseModuleRecordPresenceGuard { Id = "record-present", CaptureId = "record", MustBePresent = true }),
            (BaseModuleMutationTemplateBuilder.RevisionEquals("revision", "record", constant),
                new BaseModuleRevisionEqualsGuard { Id = "revision", CaptureId = "record", Expected = constant }),
            (BaseModuleMutationTemplateBuilder.FieldEquals("field-equals", fieldReference, constant),
                new BaseModuleFieldEqualsGuard { Id = "field-equals", Field = fieldReference, Expected = constant }),
            (BaseModuleMutationTemplateBuilder.FieldCompare("field-greater", fieldReference, BaseModuleOrderedComparisonKind.GreaterThan, constant),
                new BaseModuleFieldComparisonGuard { Id = "field-greater", Field = fieldReference, Comparison = BaseModuleOrderedComparisonKind.GreaterThan, Expected = constant }),
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
            (BaseModuleMutationTemplateBuilder.RequestProperty("request", requestReference),
                new BaseModuleRequestPropertyExpression { Id = "request", ResultType = requestAuthority.ValueType, Property = requestReference }),
            (constant, new BaseModuleConstantExpression { Id = "constant", ResultType = integer, CanonicalBaseJson = "1"u8.ToArray().ToImmutableArray() }),
            (BaseModuleMutationTemplateBuilder.CapturedField("captured-field", fieldReference),
                new BaseModuleCapturedFieldExpression { Id = "captured-field", ResultType = integer, Field = fieldReference }),
            (BaseModuleMutationTemplateBuilder.CapturedRecordId("captured-id", recordId, "record"),
                new BaseModuleCapturedRecordIdExpression { Id = "captured-id", ResultType = recordId, CaptureId = "record" }),
            (BaseModuleMutationTemplateBuilder.CapturedRevisionRaw("captured-revision", "record"),
                new BaseModuleCapturedRevisionExpression { Id = "captured-revision", ResultType = Type<RevisionToken>(), CaptureId = "record" }),
            (BaseModuleMutationTemplateBuilder.CapturedGenerationRaw("captured-generation", "generation"),
                new BaseModuleCapturedGenerationExpression { Id = "captured-generation", ResultType = Type<BaseModuleGeneration>(), CaptureId = "generation" }),
            (BaseModuleMutationTemplateBuilder.CommittedRecordId("committed-id", recordId, "create"),
                new BaseModuleCommittedRecordIdExpression { Id = "committed-id", ResultType = recordId, StatementId = "create" }),
            (BaseModuleMutationTemplateBuilder.CommittedRevisionRaw("committed-revision", "create"),
                new BaseModuleCommittedRevisionExpression { Id = "committed-revision", ResultType = Type<RevisionToken>(), StatementId = "create" }),
            (BaseModuleMutationTemplateBuilder.CommittedUpsertDisposition("upsert-disposition", Type<string>(), "upsert"),
                new BaseModuleCommittedUpsertDispositionExpression { Id = "upsert-disposition", ResultType = Type<string>(), StatementId = "upsert" }),
            (BaseModuleMutationTemplateBuilder.ResultingGenerationRaw("resulting-generation", "generation"),
                new BaseModuleResultingGenerationExpression { Id = "resulting-generation", ResultType = Type<BaseModuleGeneration>(), CaptureId = "generation" }),
            (BaseModuleMutationTemplateBuilder.Coalesce("coalesce", integer, constant, constant),
                new BaseModuleCoalesceExpression { Id = "coalesce", ResultType = integer, Values = [constant, constant] }),
            (BaseModuleMutationTemplateBuilder.Conditional("conditional", integer, "guard", constant, constant),
                new BaseModuleConditionalExpression { Id = "conditional", ResultType = integer, GuardId = "guard", WhenTrue = constant, WhenFalse = constant }),
            (BaseModuleMutationTemplateBuilder.Numeric("numeric", integer, BaseModuleNumericOperator.IntegerAddChecked, constant, constant),
                new BaseModuleBinaryNumericExpression { Id = "numeric", ResultType = integer, Operator = BaseModuleNumericOperator.IntegerAddChecked, Left = constant, Right = constant }),
            (payload, new BaseModuleObjectExpression { Id = "payload", Properties = [new BaseModuleObjectPropertyExpression { StablePropertyId = "payload.value", Value = constant }] }),
            (BaseModuleMutationTemplateBuilder.Block(new BaseModuleRequireStatement { Id = "required", GuardId = "guard", RequirementId = "required" }),
                new BaseModuleMutationBlock { Statements = [new BaseModuleRequireStatement { Id = "required", GuardId = "guard", RequirementId = "required" }] }),
            (BaseModuleMutationTemplateBuilder.ResultRaw(payload), new BaseModuleResultProjection { Value = payload }),
        ];

        foreach ((object factory, object direct) in pairs)
            factory.Should().BeEquivalentTo(direct, options => options.RespectingRuntimeTypes().IncludingInternalProperties(), factory.GetType().Name);
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
            Id = id, ResultType = Type<BaseModuleGeneration>(), CaptureId = capture,
        };
        var conditional = new BaseModuleConditionalExpression
        {
            Id = "selected", ResultType = Type<BaseModuleGeneration>(), GuardId = "choose-a",
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
                Preconditions = [],
                Body = BaseModuleMutationTemplateBuilder.Block(new BaseModuleIfStatement
                {
                    Id = "choose", GuardId = "choose-a",
                    WhenTrue = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.IncrementGeneration("increment-a", "a", true)),
                    WhenFalse = BaseModuleMutationTemplateBuilder.Block(BaseModuleMutationTemplateBuilder.IncrementGeneration("increment-b", "b", true)),
                }),
                Result = BaseModuleMutationTemplateBuilder.ResultRaw(BaseModuleMutationTemplateBuilder.Object("result",
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
                Result = BaseModuleMutationTemplateBuilder.ResultRaw(BaseModuleMutationTemplateBuilder.Object("result",
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
                BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.id", nameof(CreateRequest.Id), BaseGeneratedModuleScalarManifest.Primitive<string>()),
                BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.name", nameof(CreateRequest.Name), BaseGeneratedModuleScalarManifest.Primitive<string>()),
            ],
            [BaseModuleDtoPropertyBinding.Create<UnsupportedResult, DateTimeOffset>("result.id", nameof(UnsupportedResult.Id), BaseGeneratedModuleScalarManifest.Primitive<DateTimeOffset>())]);
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
                "result.generation", nameof(GenerationResult.Generation), BaseGeneratedModuleScalarManifest.Primitive<string>(),
                BaseFieldConfidentiality.Confidential, BaseRecordDisclosure.Omit),
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
            Limits = definition.Limits with
            {
                MaximumStatements = checked(BaseModuleMutationPlatform.MaximumLimits.MaximumStatements + 1),
            },
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
                            Id = "captured-key", ResultType = Type<string>(),
                            Field = new BaseModuleCapturedFieldReference
                            {
                                CaptureId = "record", StableFieldId = "field.name", Authority = Type<string>(),
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
    public void Relation_target_identity_cannot_depend_on_provider_captured_values()
    {
        CollectionDefinition source = ModuleCollection() with
        {
            Fields =
            [
                .. ModuleCollection().Fields!,
                new FieldDefinition
                {
                    Id = "field.owner", ApplicationName = "Owner", WireName = "owner", Type = BaseFieldTypes.String,
                    Presence = BaseFieldPresence.Required,
                    Relation = new RelationDefinition
                    {
                        Id = "module-record-owner", SourceCollectionId = "module-records",
                        SourceFieldId = "field.owner", TargetCollectionId = "module-targets",
                    },
                },
            ],
        };
        CollectionDefinition target = ModuleCollection() with
        {
            Id = "module-targets", Name = "module-targets",
        };
        BaseRegisteredModuleMutationDefinition valid = CreateDefinition();
        BaseModuleCreateStatement create = valid.Template.Body.Statements.OfType<BaseModuleCreateStatement>().Single();
        BaseRegisteredModuleMutationDefinition invalid = BaseModuleMutationContract.Seal(valid with
        {
            SystemCollectionIds = ["module-records", "module-targets"],
            SystemSourceGrants =
            [
                .. valid.SystemSourceGrants,
                new BaseModuleSystemSourceGrant
                {
                    CollectionId = "module-targets", GrantId = "module.targets.source",
                },
            ],
            Template = valid.Template with
            {
                Body = valid.Template.Body with
                {
                    Statements =
                    [
                        create with
                        {
                            Payload = create.Payload with
                            {
                                Properties =
                                [
                                    .. create.Payload.Properties,
                                    new BaseModuleObjectPropertyExpression
                                    {
                                        StablePropertyId = "field.owner",
                                        Value = new BaseModuleCapturedFieldExpression
                                        {
                                            Id = "captured-relation-target",
                                            ResultType = Type<string>(),
                                            Field = new BaseModuleCapturedFieldReference
                                            {
                                                CaptureId = "record", StableFieldId = "field.name",
                                                Authority = Type<string>(),
                                            },
                                        },
                                    },
                                ],
                            },
                        },
                        .. valid.Template.Body.Statements.Skip(1),
                    ],
                },
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        });

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            invalid,
            new Dictionary<string, CollectionDefinition>
            {
                [source.Id] = source,
                [target.Id] = target,
            },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<CreateRequest, CreateResult>(invalid, CreateIdentity()));

        validate.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
    }

    [Fact]
    public void Partial_payload_must_explicitly_restate_every_source_owned_relation()
    {
        CollectionDefinition source = ModuleCollection() with
        {
            Fields =
            [
                SourceScalarField(),
                SourceRelationField(),
            ],
        };
        CollectionDefinition target = ModuleCollection() with
        {
            Id = "module-targets", Name = "module-targets",
        };
        BaseRegisteredModuleMutationDefinition draft = CreateDefinition();
        BaseModuleCreateStatement create = draft.Template.Body.Statements.OfType<BaseModuleCreateStatement>().Single();
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(draft with
        {
            SystemCollectionIds = ["module-records", "module-targets"],
            SystemSourceGrants =
            [
                .. draft.SystemSourceGrants,
                new BaseModuleSystemSourceGrant { CollectionId = "module-targets", GrantId = "module.targets.source" },
            ],
            Template = draft.Template with
            {
                Body = draft.Template.Body with
                {
                    Statements =
                    [
                        create with
                        {
                            Payload = create.Payload with
                            {
                                Properties =
                                [
                                    .. create.Payload.Properties,
                                    new BaseModuleObjectPropertyExpression
                                    {
                                        StablePropertyId = "field.owner",
                                        Value = Request("request.id", "request-owner-create"),
                                    },
                                ],
                            },
                        },
                        .. draft.Template.Body.Statements.Skip(1),
                    ],
                },
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition>
            {
                [source.Id] = source,
                [target.Id] = target,
            },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<CreateRequest, CreateResult>(definition, CreateIdentity(definition)));

        validate.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
    }

    [Fact]
    public void Partial_payload_may_explicitly_supply_every_source_owned_relation_from_the_request()
    {
        CollectionDefinition source = ModuleCollection() with
        {
            Fields =
            [
                SourceScalarField(),
                SourceRelationField(),
            ],
        };
        CollectionDefinition target = ModuleCollection() with
        {
            Id = "module-targets", Name = "module-targets",
        };
        BaseRegisteredModuleMutationDefinition draft = CreateDefinition();
        BaseModuleCreateStatement create = draft.Template.Body.Statements.OfType<BaseModuleCreateStatement>().Single();
        BaseModulePatchStatement patch = draft.Template.Body.Statements.OfType<BaseModulePatchStatement>().Single();
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(draft with
        {
            SystemCollectionIds = ["module-records", "module-targets"],
            SystemSourceGrants =
            [
                .. draft.SystemSourceGrants,
                new BaseModuleSystemSourceGrant { CollectionId = "module-targets", GrantId = "module.targets.source" },
            ],
            Template = draft.Template with
            {
                Body = draft.Template.Body with
                {
                    Statements =
                    [
                        create with
                        {
                            Payload = create.Payload with
                            {
                                Properties =
                                [
                                    .. create.Payload.Properties,
                                    new BaseModuleObjectPropertyExpression
                                    {
                                        StablePropertyId = "field.owner",
                                        Value = Request("request.id", "request-owner-create"),
                                    },
                                ],
                            },
                        },
                        patch with
                        {
                            Patch = patch.Patch with
                            {
                                Properties =
                                [
                                    .. patch.Patch.Properties,
                                    new BaseModuleObjectPropertyExpression
                                    {
                                        StablePropertyId = "field.owner",
                                        Value = Request("request.id", "request-owner-patch"),
                                    },
                                ],
                            },
                        },
                    ],
                },
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition>
            {
                [source.Id] = source,
                [target.Id] = target,
            },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<CreateRequest, CreateResult>(definition, CreateIdentity(definition)));

        validate.Should().NotThrow();
    }

    [Fact]
    public void Partial_upsert_update_must_explicitly_restate_every_source_owned_relation()
    {
        (BaseRegisteredModuleMutationDefinition definition, CollectionDefinition source,
            CollectionDefinition target) = PartialUpsertDefinition(updateOwner: null);

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition>
            {
                [source.Id] = source,
                [target.Id] = target,
            },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<CreateRequest, CreateResult>(definition, CreateIdentity(definition)));

        validate.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
    }

    [Fact]
    public void Partial_upsert_update_rejects_provider_captured_relation_authority()
    {
        var capturedOwner = new BaseModuleCapturedFieldExpression
        {
            Id = "captured-upsert-owner",
            ResultType = Type<string>(),
            Field = new BaseModuleCapturedFieldReference
            {
                CaptureId = "record",
                StableFieldId = "field.name",
                Authority = Type<string>(),
            },
        };
        (BaseRegisteredModuleMutationDefinition definition, CollectionDefinition source,
            CollectionDefinition target) = PartialUpsertDefinition(capturedOwner);

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition>
            {
                [source.Id] = source,
                [target.Id] = target,
            },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<CreateRequest, CreateResult>(definition, CreateIdentity(definition)));

        validate.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
    }

    [Fact]
    public void Partial_upsert_update_accepts_complete_request_owned_relation_authority()
    {
        (BaseRegisteredModuleMutationDefinition definition, CollectionDefinition source,
            CollectionDefinition target) = PartialUpsertDefinition(
                Request("request.id", "request-owner-upsert-update"));

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition>
            {
                [source.Id] = source,
                [target.Id] = target,
            },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<CreateRequest, CreateResult>(definition, CreateIdentity(definition)));

        validate.Should().NotThrow();
    }

    [Fact]
    public void Provider_dependent_capture_branch_retains_both_bounded_statement_paths()
    {
        const string guardId = "provider-record-present";
        var guard = new BaseModuleRecordPresenceGuard
        {
            Id = guardId, CaptureId = "record", MustBePresent = true,
        };
        var body = new BaseModuleMutationBlock
        {
            Statements =
            [
                new BaseModuleIfStatement
                {
                    Id = "provider-branch", GuardId = guardId,
                    WhenTrue = new BaseModuleMutationBlock
                    {
                        Statements = [new BaseModuleRequireStatement { Id = "present-path", GuardId = guardId, RequirementId = "present" }],
                    },
                    WhenFalse = new BaseModuleMutationBlock
                    {
                        Statements = [new BaseModuleRequireStatement { Id = "missing-path", GuardId = guardId, RequirementId = "missing" }],
                    },
                },
            ],
        };
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            Definition(), Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, Captured(),
            new Dictionary<string, CollectionDefinition>());

        string[] statements = DefaultBaseModuleMutationRuntime.EnumerateCaptureStatements(
                body, evaluator, new Dictionary<string, BaseModuleGuard> { [guardId] = guard })
            .Select(static statement => statement.Id).ToArray();

        statements.Should().Equal("present-path", "missing-path");
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
        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> replay = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "record-1", Name = "Ada" }, requestIdentity, null, default);
        replay.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        replay.RequireValue().Result.Id.Should().Be("record-1");
        OperationResult<RecordEnvelope> stored = await store.GetAsync(collection, RecordId.Create("record-1"), session.Operation(BaseOperationKind.Get, collection.Id));
        stored.Value!.Payload.Fields!["name"].GetString().Should().Be("Grace");
    }

    [Fact]
    public async Task Required_logical_index_tracks_L50_final_overlay_key_moves_and_delete()
    {
        CollectionDefinition collection = IndexedModuleCollection();
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "module-store",
            Collections = [collection],
        });
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration
        {
            StoreId = "module-store", Store = store, CollectionIds = [collection.Id],
        });
        BaseRegisteredModuleMutationDefinition source = CreateDefinition();
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(source with
        {
            Limits = source.Limits with
            {
                MaximumFactBytes = 8_192,
                MaximumJournalBytes = 8_192,
                MaximumReceiptBytes = 16_384,
                MaximumTransientBytes = 131_072,
            },
        });
        BaseRegisteredModuleMutationDefinition patchDefinition = IndexedTransitionDefinition(
            "module.indexed-patch", delete: false);
        BaseRegisteredModuleMutationDefinition deleteDefinition = IndexedTransitionDefinition(
            "module.indexed-delete", delete: true);
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>
            {
                [collection.Id] = collection,
            }),
            new BaseModuleMutationRegistry([definition, patchDefinition, deleteDefinition], []), new DefaultBaseSchemaValidator(),
            Policy("module.create", "module.indexed-patch", "module.indexed-delete", "module.records.source"),
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(),
            new CreateRequest { Id = "record-1", Name = "Ada" },
            BaseMutationRequestIdentity.Create("module", "indexed-create", "one",
                BaseMutationRequestFingerprint.Create(new byte[32])), null, default);

        result.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<CreateResult>>>(
            result is BaseFailure<BaseModuleMutationExecutionResult<CreateResult>> failure
                ? failure.Error.Code : string.Empty);
        BaseLogicalIndexDirectory directory = store.ReadLogicalIndexDirectoryForTesting(
            collection.Id, collection.Indexes![0].Checksum)!;
        InMemoryLogicalIndexAuthority createdAuthority = store.ReadLogicalIndexAuthorityForTesting(
            collection.Id, collection.Indexes[0].Checksum)!;
        directory.EqualityPostings.Should().ContainSingle();
        directory.EqualityPostings[0].RecordIds.Should().Equal("record-1");
        directory.ComparatorEntries.Should().ContainSingle();
        directory.ComparatorEntries[0].Payload.Fields!["name"].GetString().Should().Be("Grace");
        BaseLogicalIndexDirectoryContract.Validate(collection, collection.Indexes[0], directory)
            .Should().BeTrue();

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> patched = await runtime.ExecuteAsync(
            session, patchDefinition, CreateIdentity(patchDefinition),
            new CreateRequest { Id = "record-1", Name = "Lin" },
            BaseMutationRequestIdentity.Create("module", "indexed-patch", "one",
                BaseMutationRequestFingerprint.Create(Enumerable.Repeat((byte)1, 32).ToArray())), null, default);

        patched.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<CreateResult>>>(
            patched is BaseFailure<BaseModuleMutationExecutionResult<CreateResult>> patchFailure
                ? patchFailure.Error.Code : string.Empty);
        BaseLogicalIndexDirectory moved = store.ReadLogicalIndexDirectoryForTesting(
            collection.Id, collection.Indexes[0].Checksum)!;
        InMemoryLogicalIndexAuthority movedAuthority = store.ReadLogicalIndexAuthorityForTesting(
            collection.Id, collection.Indexes[0].Checksum)!;
        moved.EqualityPostings.Should().ContainSingle();
        moved.ComparatorEntries.Should().ContainSingle();
        moved.ComparatorEntries[0].Payload.Fields!["name"].GetString().Should().Be("Lin");
        movedAuthority.Generation.Should().BeGreaterThan(createdAuthority.Generation);
        movedAuthority.DirectoryAuthority!.PreviousDirectoryPublicationChecksum.Should().Equal(
            createdAuthority.DirectoryAuthority!.DirectoryPublicationChecksum);

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> deleted = await runtime.ExecuteAsync(
            session, deleteDefinition, CreateIdentity(deleteDefinition),
            new CreateRequest { Id = "record-1", Name = "unused" },
            BaseMutationRequestIdentity.Create("module", "indexed-delete", "one",
                BaseMutationRequestFingerprint.Create(Enumerable.Repeat((byte)2, 32).ToArray())), null, default);

        deleted.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<CreateResult>>>(
            deleted is BaseFailure<BaseModuleMutationExecutionResult<CreateResult>> deleteFailure
                ? deleteFailure.Error.Code : string.Empty);
        BaseLogicalIndexDirectory empty = store.ReadLogicalIndexDirectoryForTesting(
            collection.Id, collection.Indexes[0].Checksum)!;
        InMemoryLogicalIndexAuthority deletedAuthority = store.ReadLogicalIndexAuthorityForTesting(
            collection.Id, collection.Indexes[0].Checksum)!;
        empty.EqualityPostings.Should().BeEmpty();
        empty.ComparatorEntries.Should().BeEmpty();
        deletedAuthority.Generation.Should().BeGreaterThan(movedAuthority.Generation);
        deletedAuthority.DirectoryAuthority!.PreviousDirectoryPublicationChecksum.Should().Equal(
            movedAuthority.DirectoryAuthority!.DirectoryPublicationChecksum);
        BaseLogicalIndexDirectoryContract.Validate(collection, collection.Indexes[0], empty)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(RecordMutationExecutionOutcome.RollbackConfirmed, "base.runtime.transaction.timeout", "base.runtime.transaction.timeout")]
    [InlineData(RecordMutationExecutionOutcome.RollbackConfirmed, "hostile.provider.prose", BaseModuleMutationErrorCodes.StoreError)]
    [InlineData(RecordMutationExecutionOutcome.Indeterminate, "hostile.provider.prose", BaseModuleMutationErrorCodes.CommitIndeterminate)]
    public async Task L50_provider_failure_never_exposes_provisional_state_result_or_receipt(
        RecordMutationExecutionOutcome outcome, string providerCode, string expectedCode)
    {
        CollectionDefinition collection = ModuleCollection();
        var store = new FakeRecordStore("module-store", includeAtomicRequestCapability: true)
        {
            ForcedOutcomeAfterProcessing = outcome,
            ForcedOutcomeError = new BaseError
            {
                Code = providerCode,
                Message = "Hostile provider prose must not become a successful result.",
                Category = ErrorCategory.Store,
            },
        };
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration
        {
            StoreId = "module-store", Store = store, CollectionIds = [collection.Id],
        });
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition> { [collection.Id] = collection }),
            new BaseModuleMutationRegistry([definition], []), new DefaultBaseSchemaValidator(),
            Policy("module.create", "module.records.source"),
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "module", "provider-failure", outcome.ToString(), BaseMutationRequestFingerprint.Create(new byte[32]));

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "provisional", Name = "Ada" },
            identity, null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<CreateResult>>>()
            .Which.Error.Code.Should().Be(expectedCode);
        (await store.GetAsync(collection, RecordId.Create("provisional"),
            session.Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> receipt = await runtime.ResolveAsync(
            session, definition, CreateIdentity(), identity, default);
        receipt.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<CreateResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("collection-descriptor")]
    [InlineData("payload")]
    [InlineData("changed-fields")]
    [InlineData("resource")]
    public async Task L50_rejects_hostile_mutation_fact_before_commit(string hostility)
    {
        CollectionDefinition collection = ModuleCollection();
        var store = new FakeRecordStore("module-store", includeAtomicRequestCapability: true)
        {
            MutationFactTransform = fact => hostility switch
            {
                "collection" => fact with
                {
                    After = fact.After is null ? null : fact.After with { CollectionId = "hostile.collection" },
                },
                "collection-descriptor" => fact with
                {
                    Collection = fact.Collection with { DisplayName = "Hostile same-ID descriptor" },
                },
                "payload" => fact with
                {
                    After = fact.After is null ? null : fact.After with
                    {
                        Payload = new RecordPayload
                        {
                            Kind = RecordPayloadKind.FieldMap,
                            Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                ["name"] = JsonSerializer.SerializeToElement("hostile"),
                            },
                        },
                    },
                },
                "changed-fields" => fact with { ChangedFields = ["hostile.field"] },
                "resource" => fact with { Event = fact.Event with { Resource = "hostile.resource" } },
                _ => throw new ArgumentOutOfRangeException(nameof(hostility)),
            },
        };
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration
        {
            StoreId = "module-store", Store = store, CollectionIds = [collection.Id],
        });
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition> { [collection.Id] = collection }),
            new BaseModuleMutationRegistry([definition], []), new DefaultBaseSchemaValidator(),
            Policy("module.create", "module.records.source"),
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            }, new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "hostile", Name = "Ada" },
            BaseMutationRequestIdentity.Create("module", "hostile-fact", "one",
                BaseMutationRequestFingerprint.Create(new byte[32])), null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<CreateResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ProviderContractInvalid);
        (await store.GetAsync(collection, RecordId.Create("hostile"),
            session.Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
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
        (await store.GetAsync(collection, RecordId.Create("record-denied"), session.Operation(BaseOperationKind.Get, collection.Id)))
            .Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task L50_rejects_hostile_delete_previous_before_commit()
    {
        CollectionDefinition collection = ModuleCollection();
        var store = new FakeRecordStore("module-store", includeAtomicRequestCapability: true)
        {
            AtomicProvisionalTransform = provisional => provisional with
            {
                Facts = provisional.Facts.Select((owned, index) =>
                {
                    BaseRecordMutationFact fact = owned.MaterializeOwned();
                    if (fact.CommittedOperation != BaseCommittedRecordMutationKind.Delete)
                        return owned;
                    return BaseOwnedMutationFact.Freeze(fact with
                    {
                        Delete = fact.Delete! with
                        {
                            Previous = new RecordEnvelope
                            {
                                CollectionId = collection.Id,
                                Id = RecordId.Create("hostile-previous"),
                                Payload = new RecordPayload
                                {
                                    Kind = RecordPayloadKind.FieldMap,
                                    Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                                    {
                                        ["name"] = JsonSerializer.SerializeToElement("hostile"),
                                    },
                                },
                                Metadata = new RecordMetadata(),
                            },
                        },
                    }, checked(index + 1));
                }).ToImmutableArray(),
            },
        };
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration
        {
            StoreId = "module-store", Store = store, CollectionIds = [collection.Id],
        });
        BaseRegisteredModuleMutationDefinition source = CreateDefinition();
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(source with
        {
            Template = source.Template with
            {
                Body = source.Template.Body with
                {
                    Statements =
                    [
                        .. source.Template.Body.Statements,
                        new BaseModuleDeleteStatement
                        {
                            Id = "delete", CollectionId = collection.Id,
                            RecordId = RecordIdFromRequest("delete-id"),
                        },
                    ],
                },
            },
        });
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition> { [collection.Id] = collection }),
            new BaseModuleMutationRegistry([definition], []), new DefaultBaseSchemaValidator(),
            Policy("module.create", "module.records.source"),
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            }, new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "hostile-delete", Name = "Ada" },
            BaseMutationRequestIdentity.Create("module", "hostile-delete", "one",
                BaseMutationRequestFingerprint.Create(new byte[32])), null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<CreateResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ProviderContractInvalid);
        (await store.GetAsync(collection, RecordId.Create("hostile-delete"),
            session.Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("fact-bytes")]
    [InlineData("written-bytes")]
    [InlineData("transient-bytes")]
    [InlineData("relation-checks")]
    [InlineData("unique-checks")]
    [InlineData("retirement-publications")]
    [InlineData("retirement-publication-bytes")]
    [InlineData("journal-order")]
    [InlineData("published-at")]
    public async Task L50_rejects_hostile_provisional_authority_and_accounting(string hostility)
    {
        CollectionDefinition collection = ModuleCollection();
        var store = new FakeRecordStore("module-store", includeAtomicRequestCapability: true)
        {
            AtomicProvisionalTransform = provisional => hostility switch
            {
                "authority" => provisional with
                {
                    Authority = provisional.Authority with { StoreInstanceId = "hostile.store" },
                },
                "fact-bytes" => provisional with
                {
                    Accounting = provisional.Accounting with
                    {
                        FactBytes = checked(provisional.Accounting.FactBytes + 1),
                    },
                },
                "written-bytes" => provisional with { Accounting = provisional.Accounting with { WrittenBytes = checked(provisional.Accounting.WrittenBytes + 1) } },
                "transient-bytes" => provisional with { Accounting = provisional.Accounting with { TransientBytes = checked(provisional.Accounting.TransientBytes + 1) } },
                "relation-checks" => provisional with { Accounting = provisional.Accounting with { RelationChecks = checked(provisional.Accounting.RelationChecks + 1) } },
                "unique-checks" => provisional with { Accounting = provisional.Accounting with { UniqueConstraintChecks = checked(provisional.Accounting.UniqueConstraintChecks + 1) } },
                "retirement-publications" => provisional with { Accounting = provisional.Accounting with { RetirementPublications = checked(provisional.Accounting.RetirementPublications + 1) } },
                "retirement-publication-bytes" => provisional with { Accounting = provisional.Accounting with { RetirementPublicationBytes = checked(provisional.Accounting.RetirementPublicationBytes + 1) } },
                "journal-order" => provisional with
                {
                    Facts = provisional.Facts.Select((owned, index) =>
                    {
                        BaseRecordMutationFact fact = owned.MaterializeOwned();
                        return BaseOwnedMutationFact.Freeze(fact with
                        {
                            JournalPosition = new BaseMutationJournalPosition(1),
                        }, checked(index + 1));
                    }).ToImmutableArray(),
                },
                "published-at" => provisional with
                {
                    Facts = provisional.Facts.Select((owned, index) =>
                    {
                        BaseRecordMutationFact fact = owned.MaterializeOwned();
                        return BaseOwnedMutationFact.Freeze(fact with
                        {
                            Event = fact.Event with { PublishedAt = null },
                        }, checked(index + 1));
                    }).ToImmutableArray(),
                },
                _ => throw new ArgumentOutOfRangeException(nameof(hostility)),
            },
        };
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration
        {
            StoreId = "module-store", Store = store, CollectionIds = [collection.Id],
        });
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition> { [collection.Id] = collection }),
            new BaseModuleMutationRegistry([definition], []), new DefaultBaseSchemaValidator(),
            Policy("module.create", "module.records.source"),
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            }, new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "hostile-provisional", Name = "Ada" },
            BaseMutationRequestIdentity.Create("module", "hostile-provisional", hostility,
                BaseMutationRequestFingerprint.Create(new byte[32])), null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<CreateResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ProviderContractInvalid);
        (await store.GetAsync(collection, RecordId.Create("hostile-provisional"),
            session.Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task L50_rejects_hostile_captured_record_evidence_before_planning()
    {
        CollectionDefinition collection = ModuleCollection();
        var store = new FakeRecordStore("module-store", includeAtomicRequestCapability: true)
        {
            AtomicCaptureTransform = captured => captured with
            {
                ModuleRecords = captured.ModuleRecords.SetItem(0,
                    captured.ModuleRecords[0] with { CollectionId = "hostile.collection" }),
            },
        };
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration
        {
            StoreId = "module-store", Store = store, CollectionIds = [collection.Id],
        });
        BaseRegisteredModuleMutationDefinition definition = CreateDefinition();
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition> { [collection.Id] = collection }),
            new BaseModuleMutationRegistry([definition], []), new DefaultBaseSchemaValidator(),
            Policy("module.create", "module.records.source"),
            new DefaultBaseResultNormalizer(NullLogger<DefaultBaseResultNormalizer>.Instance),
            new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            }, new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");

        BaseResult<BaseModuleMutationExecutionResult<CreateResult>> result = await runtime.ExecuteAsync(
            session, definition, CreateIdentity(), new CreateRequest { Id = "hostile-capture", Name = "Ada" },
            BaseMutationRequestIdentity.Create("module", "hostile-capture", "one",
                BaseMutationRequestFingerprint.Create(new byte[32])), null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<CreateResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ProviderContractInvalid);
        (await store.GetAsync(collection, RecordId.Create("hostile-capture"),
            session.Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task L50_rejects_hostile_captured_generation_evidence_before_planning()
    {
        var store = new FakeRecordStore("module-store", includeAtomicRequestCapability: true)
        {
            AtomicCaptureTransform = captured => captured with
            {
                Generations = captured.Generations.SetItem(0,
                    captured.Generations[0] with { CellId = "hostile.cell" }),
            },
        };
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseModuleGenerationCellDefinition cell = new()
        {
            Id = "module.generation", Version = 1, OwningModuleId = "module",
            Scope = BaseModuleGenerationScope.Application,
            MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
        };
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition();
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            new BaseModuleMutationRegistry([definition], [cell]), null!, Policy("module.increment"),
            null!, new BaseSubjectContractRegistry([]), TimeProvider.System);
        var session = new BaseSession(null!, TimeProvider.System,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            }, new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");

        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> result = await runtime.ExecuteAsync(
            session, definition, GenerationIdentity(), new GenerationRequest(),
            BaseMutationRequestIdentity.Create("module", "hostile-generation", "one",
                BaseMutationRequestFingerprint.Create(new byte[32])), null, default);

        result.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ProviderContractInvalid);
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
    public async Task Activation_context_resolves_committed_module_receipt_without_reexecuting_the_program()
    {
        var clock = new ModuleMutationTimeProvider(
            new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero));
        using var tokenProtector = new BaseOpaqueTokenProtector(
            Options.Create(new HPDBaseTokenProtectionOptions
            {
                ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1,
                    Key = Enumerable.Repeat((byte)0x83, 32).ToArray(),
                    IssueNotBefore = DateTimeOffset.UnixEpoch,
                },
            }),
            clock);
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "module-store",
            Collections = [],
        }, tokenProtector, clock);
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseModuleGenerationCellDefinition cell = new()
        {
            Id = "module.generation", Version = 1, OwningModuleId = "module",
            Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32,
            MaximumCellsPerOperation = 1,
        };
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition();
        BaseRegisteredModuleMutationDefinition alternateDefinition = BaseModuleMutationContract.Seal(
            definition with
            {
                Id = "module.increment.alternate",
                GrantId = "module.increment.alternate",
                Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
            });
        BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> operation =
            GenerationIdentity(definition);
        BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> alternateOperation =
            GenerationIdentity(alternateDefinition);
        var registration = new BaseModuleMutationRegistration<GenerationRequest, GenerationResult>(
            definition, operation);
        var alternateRegistration = new BaseModuleMutationRegistration<GenerationRequest, GenerationResult>(
            alternateDefinition, alternateOperation);
        var registry = new BaseModuleMutationRegistry(
            [definition, alternateDefinition], [cell], [registration, alternateRegistration]);
        DefaultBasePolicyOrchestrator policy = Policy("module.increment", "module.increment.alternate");
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores,
            new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            registry,
            null!,
            policy,
            null!,
            new BaseSubjectContractRegistry([]),
            clock);
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton<IBaseModuleMutationRuntime>(runtime)
            .BuildServiceProvider();
        var claim = new BaseActivationClaimAuthority
        {
            ActivationId = "activation", AttemptNumber = 1, ActivationGeneration = 1,
            ClaimEpoch = 1, FencingToken = new byte[32].ToImmutableArray(),
            WorkerIdentity = "worker", CancellationGeneration = 0,
            StoreInstanceId = "module-store", RestoreEpoch = 1,
            DefinitionChecksum = new byte[32].ToImmutableArray(), ExecutionSliceOrdinal = 1,
            AttemptStartedAt = 1, SliceStartedAt = 1, YieldCount = 0, MaximumYields = 0,
        };
        var session = new BaseSession(
            null!,
            clock,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "system",
            },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            services: services,
            applicationId: "module.application").WithActivationProvenance(claim);
        var context = new BaseActivationContext(
            new BaseActivationDefinitionKey
            {
                Id = "definition", Version = 1,
                Checksum = claim.DefinitionChecksum,
            },
            claim,
            new BaseActivationLeaseObservation
            {
                LeaseRevision = 1, LeaseExpiresAt = 100,
                Checksum = new byte[32].ToImmutableArray(),
            },
            new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
            null,
            0,
            0,
            1,
            (_, _) => throw new InvalidOperationException(),
            CancellationToken.None,
            1,
            session);
        BaseMutationRequestIdentity committedIdentity = BaseMutationRequestIdentity.Create(
            "module", "increment", "committed", BaseMutationRequestFingerprint.Create(new byte[32]));

        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> absent =
            await context.ResolveModuleMutationAsync(operation, committedIdentity);

        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> committed =
            await context.ExecuteModuleMutationAsync(
                operation, new GenerationRequest(), committedIdentity);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> substitutedFingerprint =
            await context.ResolveModuleMutationAsync(
                operation,
                BaseMutationRequestIdentity.Create(
                    committedIdentity.Scope,
                    committedIdentity.Operation,
                    committedIdentity.IdempotencyKey,
                    BaseMutationRequestFingerprint.Create(
                        Enumerable.Repeat((byte)0x7f, 32).ToArray())));
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> substitutedOperation =
            await context.ResolveModuleMutationAsync(alternateOperation, committedIdentity);
        var foreignApplicationSession = new BaseSession(
            null!, clock, session.Principal,
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            services: services,
            applicationId: "foreign.application");
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> substitutedApplication =
            await runtime.ResolveAsync(
                foreignApplicationSession, definition, operation, committedIdentity, default);
        var foreignStore = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "foreign-store",
            Collections = [],
        });
        var foreignStores = new DefaultRecordStoreRegistry();
        foreignStores.Add(new RecordStoreRegistration { StoreId = "foreign-store", Store = foreignStore });
        var foreignRuntime = new DefaultBaseModuleMutationRuntime(
            foreignStores,
            new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            registry,
            null!,
            policy,
            null!,
            new BaseSubjectContractRegistry([]),
            clock);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> substitutedStore =
            await foreignRuntime.ResolveAsync(session, definition, operation, committedIdentity, default);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> resolved =
            await context.ResolveModuleMutationAsync(operation, committedIdentity);
        clock.Advance(TimeSpan.FromDays(1));
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> expired =
            await context.ResolveModuleMutationAsync(operation, committedIdentity);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> next =
            await context.ExecuteModuleMutationAsync(
                operation,
                new GenerationRequest(),
                BaseMutationRequestIdentity.Create(
                    "module", "increment", "next",
                    BaseMutationRequestFingerprint.Create(new byte[32])));

        absent.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
        committed.RequireValue().Result.Generation.Should().Be("1",
            "resolving an absent receipt must not execute the operation");
        committed.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        substitutedFingerprint.Should()
            .BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
        substitutedOperation.Should()
            .BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
        substitutedApplication.Should()
            .BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
        substitutedStore.Should()
            .BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
        typeof(BaseGeneratedModuleMutationIdentity<,>)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Should().BeEmpty("application code must not fabricate a different typed result authority");
        resolved.RequireValue().Result.Generation.Should().Be("1");
        resolved.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        expired.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>>>()
            .Which.Error.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
        next.RequireValue().Result.Generation.Should().Be("2",
            "absent, mismatched, exact, and expired receipt resolution must never execute the program");
    }

    [Fact]
    public async Task Semantic_receipt_replay_reauthorizes_current_result_disclosure_without_changing_history()
    {
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition();
        BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> identity = GenerationIdentity();
        var principal = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "system",
        };
        var operation = new OperationContext
        {
            ApplicationId = "module.application",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Operation = BaseOperationKind.ModuleMutation,
            CollectionId = definition.Id,
            Now = DateTimeOffset.UtcNow,
        };
        BaseSemanticActivationReceiptEvidence semantic = new()
        {
            Operation = BaseSemanticActivationOperationKind.Ensure,
            DefinitionId = "module.semantic.v1",
            DefinitionVersion = 1,
            DefinitionChecksum = Enumerable.Repeat((byte)1, 32).ToImmutableArray(),
            Key = BaseSemanticActivationKeyDigest.Create(Enumerable.Repeat((byte)2, 32).ToArray()),
            State = BaseSemanticActivationSlotState.Live,
            SlotGeneration = 1,
            EnsureDisposition = BaseSemanticActivationEnsureDisposition.Created,
            ActivationId = new string('a', 64),
            SlotChecksum = Enumerable.Repeat((byte)3, 32).ToImmutableArray(),
            JournalPosition = 7,
            CommitEvidenceChecksum = Enumerable.Repeat((byte)4, 32).ToImmutableArray(),
            Checksum = [],
        };
        semantic = semantic with { Checksum = BaseSemanticActivationEvidenceContract.ReceiptChecksum(semantic) };
        BaseAtomicReceiptResult historical = new()
        {
            Kind = BaseAtomicReceiptResultKind.ModuleMutation,
            Mutations = [],
            ModuleMutation = new BaseModuleMutationReceiptResult
            {
                OperationId = definition.Id,
                OperationVersion = definition.Version,
                Disposition = BaseMutationRequestDisposition.Committed,
                Outcome = BaseModuleMutationOutcome.Committed,
                Generations = [],
                CanonicalResultBytes = JsonSerializer.SerializeToUtf8Bytes(
                    new GenerationResult { Generation = "1" }, identity.ResultTypeInfo).ToImmutableArray(),
                SemanticActivation = semantic,
            },
        };

        var denied = new BaseModuleMutationReceiptResolver<GenerationResult>(
            definition, identity.ResultTypeInfo, identity.ResultBindings, principal, operation,
            PolicyWithReadMask(new FieldMask { Mode = FieldMaskMode.DenyAll }));
        AtomicMutationProcessingResult deniedResult = await denied.ResolveReceiptAsync(historical);

        deniedResult.Outcome.Should().Be(AtomicMutationProcessingOutcome.Failed);
        deniedResult.Error!.Code.Should().Be(BaseModuleMutationErrorCodes.ReceiptUnavailable);
        denied.Result.Should().BeNull();
        denied.SemanticReceipt.Should().BeNull();

        var allowed = new BaseModuleMutationReceiptResolver<GenerationResult>(
            definition, identity.ResultTypeInfo, identity.ResultBindings, principal, operation,
            Policy("module.increment"));
        AtomicMutationProcessingResult allowedResult = await allowed.ResolveReceiptAsync(historical);

        allowedResult.Outcome.Should().Be(AtomicMutationProcessingOutcome.ReadyToCommit);
        allowed.Result!.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        allowed.Result.Result.Generation.Should().Be("1");
        allowed.SemanticReceipt.Should().Be(semantic);
        allowedResult.Receipt.Should().BeSameAs(historical);
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
            BaseActivationDefinitionBuilder.CreateGeneratedTransactional(new BaseActivationDefinitionDraft
            {
                Id = "module.transactional", Version = 1, OwningModuleId = "module",
                ExecutionClass = BaseActivationExecutionClass.TransactionalOperation,
                Grants = grants, SourceGrantIds = [],
                Retry = new BaseActivationRetryProfile
                {
                    MaximumAttempts = 1, InitialDelayMilliseconds = 1, MaximumDelayMilliseconds = 1,
                    MultiplierNumerator = 1, MultiplierDenominator = 1, JitterBasisPoints = 0,
                    RetryableFailureCodes = [],
                },
                ReceiptRetention = new BaseActivationReceiptRetentionPolicy
                {
                    FormatVersion = 1, DuplicateResolutionLifetime = TimeSpan.FromHours(24),
                    ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
                },
                Limits = new BaseActivationLimits
                {
                    MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 1, MaximumYields = 0,
                    MaximumRenewalsPerSlice = 1, MaximumChildrenPerSlice = 1, MaximumLineageDepth = 1,
                    LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(1),
                    Provider = ActivationProviderLimits(), AtomicCreation = DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits),
                },
                Handler = null,
                TransactionalTarget = new BaseModuleMutationActivationTarget
                {
                    OperationId = moduleDefinition.Id, OperationVersion = moduleDefinition.Version,
                    OperationChecksum = Convert.ToHexStringLower(moduleDefinition.Checksum.ToArray()),
                },
            }, EvaluatorActivationDtos.HPDBaseActivationDtoAuthority);
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
            ResultType = Type<long>(),
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

    [Fact]
    public void Scalar_equality_compares_JSON_values_instead_of_escape_spellings()
    {
        BaseRegisteredModuleMutationDefinition source = Definition();
        BaseRegisteredModuleMutationDefinition definition = source with
        {
            Template = source.Template with
            {
                Guards =
                [
                    new BaseModuleValueEqualsGuard
                    {
                        Id = "escaped-string-equality",
                        Left = new BaseModuleConstantExpression
                        {
                            Id = "literal-plus", ResultType = Type<string>(),
                            CanonicalBaseJson = "\"A+B\""u8.ToArray().ToImmutableArray(),
                        },
                        Right = new BaseModuleConstantExpression
                        {
                            Id = "escaped-plus", ResultType = Type<string>(),
                            CanonicalBaseJson = "\"A\\u002BB\""u8.ToArray().ToImmutableArray(),
                        },
                    },
                ],
            },
        };
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            definition, Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, null,
            new Dictionary<string, CollectionDefinition>());

        evaluator.Guard("escaped-string-equality").Should().BeTrue();
    }

    [Fact]
    public void Nested_request_edges_resolve_from_the_generated_leaf_binding()
    {
        BaseModuleDtoPropertyBinding binding = BaseModuleDtoPropertyBinding.CreatePathWire<
            NestedEvaluatorRequest, long>(
                ["request.wrapper", "request.wrapper.amount"],
                nameof(NestedEvaluatorValue.Amount),
                [nameof(NestedEvaluatorRequest.Wrapper), nameof(NestedEvaluatorValue.Amount)],
                BaseGeneratedModuleScalarManifest.Primitive<long>());
        var identity = new BaseGeneratedModuleMutationIdentity<NestedEvaluatorRequest, EvaluatorResult>(
            "module.nested", 1, new byte[32], EvaluatorJsonContext.Default.NestedEvaluatorRequest,
            EvaluatorJsonContext.Default.EvaluatorResult, [binding],
            [BaseModuleDtoPropertyBinding.Create<EvaluatorResult, long>(
                "result.amount", nameof(EvaluatorResult.Amount), BaseGeneratedModuleScalarManifest.Primitive<long>())]);
        var evaluator = new BaseModuleProgramEvaluator<NestedEvaluatorRequest, EvaluatorResult>(
            Definition(), identity,
            new NestedEvaluatorRequest { Wrapper = new NestedEvaluatorValue { Amount = 41 } },
            Captured(), new Dictionary<string, CollectionDefinition>());

        BaseModuleProgramValue value = evaluator.Evaluate(new BaseModuleRequestPropertyExpression
        {
            Id = "nested-amount",
            ResultType = binding.ScalarAuthority.ValueType,
            Property = new BaseModuleRequestPropertyReference
            {
                StablePropertyPath = ["request.wrapper", "request.wrapper.amount"],
                Authority = binding.ScalarAuthority,
            },
        });

        value.Value.GetInt64().Should().Be(41);
    }

    [Fact]
    public void Typed_record_id_conversion_preserves_the_exact_generated_target_authority()
    {
        BaseModuleValue<Guid> guid = new(new BaseModuleConstantExpression
        {
            Id = "request-guid",
            ResultType = Type<Guid>(),
            CanonicalBaseJson = "\"00112233-4455-6677-8899-aabbccddeeff\""u8.ToArray().ToImmutableArray(),
        });

        BaseModuleValue<BaseRecordId<L67Record>> converted =
            BaseModuleMutationTemplateBuilder.RecordIdFromGuid<L67Record>("record-id", guid);

        converted.Authority.Kind.Should().Be(BaseModuleValueKind.RecordId);
        converted.Authority.RecordTargetCollectionId.Should().Be("l67-records");
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            Definition(), Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, null,
            new Dictionary<string, CollectionDefinition>());
        evaluator.Evaluate(converted.Expression).Value.GetString()
            .Should().Be("00112233-4455-6677-8899-aabbccddeeff");
    }

    [Fact]
    public void Sha256_string_identity_uses_the_closed_domain_and_length_framing()
    {
        const string sourceText = "activation-001";
        BaseModuleValue<string> source = BaseModuleMutationTemplateBuilder.Constant(
            "source", L67Record.Fields.Name.ConstantAuthority, sourceText);
        BaseModuleValue<string> identity = BaseModuleMutationTemplateBuilder.Sha256HexStringIdentity(
            "identity", L67Record.Fields.HashId.ModuleMutation, "base.test.identity.v1", source);
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            Definition(), Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, null,
            new Dictionary<string, CollectionDefinition>());

        byte[] domain = System.Text.Encoding.UTF8.GetBytes("base.test.identity.v1");
        byte[] value = System.Text.Encoding.UTF8.GetBytes(sourceText);
        byte[] preimage = new byte[domain.Length + 1 + 4 + value.Length];
        domain.CopyTo(preimage, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            preimage.AsSpan(domain.Length + 1, 4), checked((uint)value.Length));
        value.CopyTo(preimage, domain.Length + 5);

        evaluator.Evaluate(identity.Expression).Value.GetString().Should().Be(
            Convert.ToHexStringLower(SHA256.HashData(preimage)));
        identity.Authority.Constraints!.MinimumUtf8Bytes.Should().Be(64);
        identity.Authority.Constraints.MaximumUtf8Bytes.Should().Be(64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("contains\0nul")]
    public void Sha256_string_identity_rejects_noncanonical_domains(string domain)
    {
        BaseModuleValue<string> source = BaseModuleMutationTemplateBuilder.Constant(
            "source", L67Record.Fields.Name.ConstantAuthority, "value");

        Action build = () => BaseModuleMutationTemplateBuilder.Sha256HexStringIdentity(
            "identity", L67Record.Fields.HashId.ModuleMutation, domain, source);

        build.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    public void Canonical_string_record_id_conversion_accepts_exact_positive_boundaries(int length)
    {
        string value = new('a', length);
        BaseModuleValue<string> source = new(new BaseModuleConstantExpression
        {
            Id = "source",
            ResultType = Type<string>(),
            CanonicalBaseJson = JsonSerializer.SerializeToUtf8Bytes(value).ToImmutableArray(),
        });
        BaseModuleValue<BaseRecordId<L67Record>> converted =
            BaseModuleMutationTemplateBuilder.RecordIdFromString<L67Record>("record-id", source);
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            Definition(), Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, null,
            new Dictionary<string, CollectionDefinition>());

        evaluator.Evaluate(converted.Expression).Value.GetString().Should().Be(value);
    }

    [Fact]
    public void Record_id_conversion_checksum_is_sensitive_to_conversion_and_target_authority()
    {
        BaseModuleValue<string> source = new(new BaseModuleConstantExpression
        {
            Id = "source",
            ResultType = Type<string>(),
            CanonicalBaseJson = "\"record-one\""u8.ToArray().ToImmutableArray(),
        });
        BaseModuleValue<BaseRecordId<L67Record>> converted =
            BaseModuleMutationTemplateBuilder.RecordIdFromString<L67Record>("record-id", source);
        BaseRegisteredModuleMutationDefinition first = DefinitionWithResultExpression(converted.Expression);
        BaseRegisteredModuleMutationDefinition second = DefinitionWithResultExpression(
            new BaseModuleRecordIdConversionExpression
            {
                Id = "record-id",
                ResultType = RecordIdType("other-l67-records"),
                Conversion = BaseModuleRecordIdConversionKind.CanonicalString,
                Source = source.Expression,
            });

        BaseModuleMutationContract.ComputeChecksum(first).Should()
            .NotBe(BaseModuleMutationContract.ComputeChecksum(second));
    }

    [Fact]
    public void Invalid_record_id_string_is_rejected_before_provider_influence()
    {
        BaseModuleValue<string> source = new(new BaseModuleConstantExpression
        {
            Id = "source",
            ResultType = Type<string>(),
            CanonicalBaseJson = "\"\""u8.ToArray().ToImmutableArray(),
        });
        BaseModuleValue<BaseRecordId<L67Record>> converted =
            BaseModuleMutationTemplateBuilder.RecordIdFromString<L67Record>("record-id", source);
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            Definition(), Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, Captured(),
            new Dictionary<string, CollectionDefinition>());

        Action act = () => evaluator.Evaluate(converted.Expression);

        act.Should().Throw<BaseModuleScalarContractException>()
            .Which.ProviderInfluenced.Should().BeFalse();
    }

    [Fact]
    public void Invalid_host_record_id_constant_fails_definition_validation()
    {
        BaseRegisteredModuleMutationDefinition valid = CreateDefinition();
        BaseModuleRecordCapture capture = valid.Template.Captures.OfType<BaseModuleRecordCapture>().Single();
        var invalidConversion = new BaseModuleRecordIdConversionExpression
        {
            Id = "invalid-record-id",
            ResultType = RecordIdType("module-records"),
            Conversion = BaseModuleRecordIdConversionKind.CanonicalString,
            Source = new BaseModuleConstantExpression
            {
                Id = "invalid-record-id-source",
                ResultType = Type<string>(),
                CanonicalBaseJson = "\"\""u8.ToArray().ToImmutableArray(),
            },
        };
        BaseRegisteredModuleMutationDefinition invalid = BaseModuleMutationContract.Seal(valid with
        {
            Template = valid.Template with
            {
                Captures = [capture with { RecordId = invalidConversion }],
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        });

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            invalid,
            new Dictionary<string, CollectionDefinition> { ["module-records"] = ModuleCollection() },
            new Dictionary<string, BaseModuleGenerationCellDefinition>(),
            new BaseModuleMutationRegistration<CreateRequest, CreateResult>(invalid, CreateIdentity()));

        validate.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
    }

    [Fact]
    public void Request_conversion_failure_is_not_reclassified_by_unrelated_or_unselected_provider_state()
    {
        var requestSource = new BaseModuleRequestPropertyExpression
        {
            Id = "request-id",
            ResultType = Dto<string>("request.id").ValueType,
            Property = new BaseModuleRequestPropertyReference
            {
                StablePropertyPath = ["request.id"],
                Authority = Dto<string>("request.id"),
            },
        };
        var providerSource = new BaseModuleCapturedFieldExpression
        {
            Id = "provider-name",
            ResultType = Type<string>(),
            Field = new BaseModuleCapturedFieldReference
            {
                CaptureId = "existing", StableFieldId = "field.name", Authority = Type<string>(),
            },
        };
        var selectedRequest = new BaseModuleConditionalExpression
        {
            Id = "selected-request",
            ResultType = requestSource.ResultType,
            GuardId = "enabled",
            WhenTrue = requestSource,
            WhenFalse = providerSource,
        };
        var conversion = new BaseModuleRecordIdConversionExpression
        {
            Id = "converted-request",
            ResultType = RecordIdType("module-records"),
            Conversion = BaseModuleRecordIdConversionKind.CanonicalString,
            Source = selectedRequest,
        };
        var evaluator = new BaseModuleProgramEvaluator<CreateRequest, CreateResult>(
            Definition(), CreateIdentity(), new CreateRequest { Id = "", Name = "request" }, Captured(),
            new Dictionary<string, CollectionDefinition>
            {
                ["records"] = new CollectionDefinition
                {
                    Id = "records", Name = "records", Kind = BaseCollectionKinds.Document,
                    SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                    Fields = [new FieldDefinition { Id = "field.name", ApplicationName = "Name", WireName = "name", Type = "string" }],
                },
            });

        Action act = () => evaluator.Evaluate(conversion);

        act.Should().Throw<BaseModuleScalarContractException>()
            .Which.ProviderInfluenced.Should().BeFalse();
    }

    [Fact]
    public void Hostile_captured_record_id_source_is_provider_influenced()
    {
        BaseCapturedAtomicExecution captured = Captured();
        BaseCapturedModuleRecord record = captured.ModuleRecords[0];
        captured = captured with
        {
            ModuleRecords = [record with
            {
                Current = record.Current! with
                {
                    Payload = new RecordPayload
                    {
                        Kind = RecordPayloadKind.FieldMap,
                        Fields = new Dictionary<string, JsonElement>
                        {
                            ["name"] = JsonSerializer.SerializeToElement(""),
                        },
                    },
                },
            }],
        };
        var conversion = new BaseModuleRecordIdConversionExpression
        {
            Id = "captured-record-id",
            ResultType = RecordIdType("module-records"),
            Conversion = BaseModuleRecordIdConversionKind.CanonicalString,
            Source = new BaseModuleCapturedFieldExpression
            {
                Id = "captured-record-id-source",
                ResultType = Type<string>(),
                Field = new BaseModuleCapturedFieldReference
                {
                    CaptureId = "existing", StableFieldId = "field.name", Authority = Type<string>(),
                },
            },
        };
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            Definition(), Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, captured,
            new Dictionary<string, CollectionDefinition>
            {
                ["records"] = new CollectionDefinition
                {
                    Id = "records", Name = "records", Kind = BaseCollectionKinds.Document,
                    SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                    Fields = [new FieldDefinition { Id = "field.name", ApplicationName = "Name", WireName = "name", Type = "string" }],
                },
            });

        Action act = () => evaluator.Evaluate(conversion);

        act.Should().Throw<BaseModuleScalarContractException>()
            .Which.ProviderInfluenced.Should().BeTrue();
    }

    [Fact]
    public void Hostile_captured_scalar_is_rejected_as_provider_influenced()
    {
        BaseCapturedAtomicExecution captured = Captured();
        BaseCapturedModuleRecord record = captured.ModuleRecords[0];
        captured = captured with
        {
            ModuleRecords =
            [
                record with
                {
                    CollectionId = "records",
                    Current = record.Current! with
                    {
                        CollectionId = "records",
                        Payload = new RecordPayload
                        {
                            Kind = RecordPayloadKind.FieldMap,
                            Fields = new Dictionary<string, JsonElement>
                            {
                                ["counter"] = JsonSerializer.SerializeToElement("not-an-integer"),
                            },
                        },
                    },
                },
            ],
        };
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            Definition(), Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, captured,
            new Dictionary<string, CollectionDefinition>
            {
                ["records"] = new CollectionDefinition
                {
                    Id = "records", Name = "records", Kind = BaseCollectionKinds.Document,
                    SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                    Fields = [new FieldDefinition { Id = "record.counter", ApplicationName = "Counter", WireName = "counter", Type = BaseFieldTypes.Integer }],
                },
            });
        var expression = new BaseModuleCapturedFieldExpression
        {
            Id = "captured-counter", ResultType = Type<long>(),
            Field = new BaseModuleCapturedFieldReference { CaptureId = "existing", StableFieldId = "record.counter", Authority = Type<long>() },
        };

        Action act = () => evaluator.Evaluate(expression);

        act.Should().Throw<BaseModuleScalarContractException>()
            .Which.ProviderInfluenced.Should().BeTrue();
    }

    [Fact]
    public void Typed_constant_owns_and_validates_its_exact_authority()
    {
        BaseModuleDtoScalarAuthority scalar = Dto<Guid>("request.id");
        var property = new BaseModuleRequestProperty<CreateRequest, Guid>(scalar);
        Guid value = Guid.Parse("0f9a4bc4-f95f-4d9e-840c-35d6d81bed52");

        BaseModuleValue<Guid> expression = BaseModuleMutationTemplateBuilder.Constant(
            "constant-id", property.ConstantAuthority, value);

        expression.Expression.Should().BeOfType<BaseModuleConstantExpression>()
            .Which.CanonicalBaseJson.AsSpan().ToArray().Should().Equal(
                JsonSerializer.SerializeToUtf8Bytes(value.ToString("D")));
    }

    [Theory]
    [InlineData(40, BaseModuleOrderedComparisonKind.LessThan, true)]
    [InlineData(41, BaseModuleOrderedComparisonKind.LessThan, false)]
    [InlineData(41, BaseModuleOrderedComparisonKind.LessThanOrEqual, true)]
    [InlineData(42, BaseModuleOrderedComparisonKind.GreaterThan, true)]
    [InlineData(41, BaseModuleOrderedComparisonKind.GreaterThanOrEqual, true)]
    public void Ordered_field_guard_uses_exact_int64_semantics(
        long capturedValue,
        BaseModuleOrderedComparisonKind comparison,
        bool expected)
    {
        BaseModuleCapturedFieldReference field = new()
        {
            CaptureId = "existing", StableFieldId = "record.counter", Authority = Type<long>(),
        };
        BaseModuleConstantExpression threshold = new()
        {
            Id = "threshold", ResultType = Type<long>(),
            CanonicalBaseJson = "41"u8.ToArray().ToImmutableArray(),
        };
        BaseRegisteredModuleMutationDefinition definition = Definition() with
        {
            Template = Definition().Template with
            {
                Guards =
                [
                    new BaseModuleFieldComparisonGuard
                    {
                        Id = "ordered", Field = field, Comparison = comparison, Expected = threshold,
                    },
                ],
            },
        };
        BaseCapturedAtomicExecution captured = Captured();
        BaseCapturedModuleRecord record = captured.ModuleRecords[0];
        captured = captured with
        {
            ModuleRecords =
            [
                record with
                {
                    Current = record.Current! with
                    {
                        Payload = new RecordPayload
                        {
                            Kind = RecordPayloadKind.FieldMap,
                            Fields = new Dictionary<string, System.Text.Json.JsonElement>
                            {
                                ["counter"] = System.Text.Json.JsonSerializer.SerializeToElement(capturedValue),
                            },
                        },
                    },
                },
            ],
        };
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            definition, Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, captured,
            new Dictionary<string, CollectionDefinition>
            {
                ["records"] = new CollectionDefinition
                {
                    Id = "records", Name = "records", Kind = BaseCollectionKinds.Document,
                    SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                    MutationMode = BaseCollectionMutationMode.Mutable,
                    Fields = [new FieldDefinition { Id = "record.counter", ApplicationName = "Counter", WireName = "counter", Type = BaseFieldTypes.Integer }],
                },
            });

        evaluator.Guard("ordered").Should().Be(expected);
    }

    [Fact]
    public void Ordered_decimal_and_canonical_UTC_date_guards_are_closed()
    {
        EvaluateOrdered("decimal", BaseFieldTypes.Decimal,
            System.Text.Json.JsonSerializer.SerializeToElement(10.250m), "10.249"u8).Should().BeTrue();
        EvaluateOrdered("dateTime", BaseFieldTypes.DateTime,
            System.Text.Json.JsonSerializer.SerializeToElement("2026-08-22T17:00:00.0000001Z"),
            "\"2026-08-22T17:00:00.0000000Z\""u8).Should().BeTrue();

        Action nonzeroOffset = () => EvaluateOrdered("dateTime", BaseFieldTypes.DateTime,
            System.Text.Json.JsonSerializer.SerializeToElement("2026-08-22T12:00:00.0000000-05:00"),
            "\"2026-08-22T17:00:00.0000000Z\""u8);
        Action noncanonicalUtc = () => EvaluateOrdered("dateTime", BaseFieldTypes.DateTime,
            System.Text.Json.JsonSerializer.SerializeToElement("2026-08-22T17:00:00+00:00"),
            "\"2026-08-22T17:00:00.0000000Z\""u8);

        nonzeroOffset.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
        noncanonicalUtc.Should().Throw<InvalidOperationException>().WithMessage("base.moduleMutation.invalid");
    }

    [Theory]
    [InlineData("\"2026-08-22T17:00:00.0000000Z\"", true)]
    [InlineData("\"2026-08-22T12:00:00.0000000-05:00\"", false)]
    [InlineData("\"2026-08-22T17:00:00+00:00\"", false)]
    [InlineData("\"2026-08-22T17:00:00Z\"", false)]
    [InlineData("null", false)]
    public void UTC_date_contract_accepts_one_canonical_base_json_representation(string json, bool expected)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        BaseModuleDateTimeContract.TryRead(document.RootElement, out _).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"2026-08-22T17:00:00.0000000Z\"", true)]
    [InlineData("\"2026-08-22T12:00:00.0000000-05:00\"", false)]
    [InlineData("\"2026-08-22T17:00:00+00:00\"", false)]
    public void Graph_validation_rejects_noncanonical_UTC_date_constants(string json, bool expected)
    {
        BaseRegisteredModuleMutationDefinition source = CreateDefinition();
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(source with
        {
            Template = source.Template with
            {
                Guards =
                [
                    new BaseModuleFieldComparisonGuard
                    {
                        Id = "ordered-date",
                        Field = new BaseModuleCapturedFieldReference
                        {
                            CaptureId = "record", StableFieldId = "record.value", Authority = Type<DateTimeOffset>(),
                        },
                        Comparison = BaseModuleOrderedComparisonKind.GreaterThan,
                        Expected = new BaseModuleConstantExpression
                        {
                            Id = "expected-date", ResultType = Type<DateTimeOffset>(),
                            CanonicalBaseJson = System.Text.Encoding.UTF8.GetBytes(json).ToImmutableArray(),
                        },
                    },
                ],
                Body = new BaseModuleMutationBlock
                {
                    Statements =
                    [
                        BaseModuleMutationTemplateBuilder.Require("require-ordered-date", "ordered-date", "proof.ordered-date"),
                        .. source.Template.Body.Statements,
                    ],
                },
            },
        });
        CollectionDefinition sourceCollection = ModuleCollection();
        var collections = new Dictionary<string, CollectionDefinition>
        {
            ["module-records"] = sourceCollection with
            {
                Fields =
                [
                    .. sourceCollection.Fields!,
                    new FieldDefinition { Id = "record.value", ApplicationName = "Value", WireName = "value", Type = BaseFieldTypes.DateTime },
                ],
            },
        };

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition, collections, new Dictionary<string, BaseModuleGenerationCellDefinition>());

        if (expected) validate.Should().NotThrow();
        else validate.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    [Fact]
    public void Graph_validation_rejects_declared_but_unreachable_guards()
    {
        BaseRegisteredModuleMutationDefinition source = CreateDefinition();
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(source with
        {
            Template = source.Template with
            {
                Guards =
                [
                    new BaseModuleValueEqualsGuard
                    {
                        Id = "unused-guard",
                        Left = new BaseModuleConstantExpression
                        {
                            Id = "unused-left", ResultType = Type<bool>(), CanonicalBaseJson = "true"u8.ToArray().ToImmutableArray(),
                        },
                        Right = new BaseModuleConstantExpression
                        {
                            Id = "unused-right", ResultType = Type<bool>(), CanonicalBaseJson = "true"u8.ToArray().ToImmutableArray(),
                        },
                    },
                ],
            },
        });

        Action validate = () => BaseModuleMutationContractValidator.ValidateDefinition(
            definition,
            new Dictionary<string, CollectionDefinition> { ["module-records"] = ModuleCollection() },
            new Dictionary<string, BaseModuleGenerationCellDefinition>());

        validate.Should().Throw<InvalidOperationException>().WithMessage(BaseModuleMutationErrorCodes.Invalid);
    }

    private static bool EvaluateOrdered(string typeId, string fieldType, System.Text.Json.JsonElement capturedValue, ReadOnlySpan<byte> expectedJson)
    {
        BaseModuleValueType valueType = typeId switch
        {
            "int64" => Type<long>(),
            "decimal" => Type<decimal>(),
            "dateTime" => Type<DateTimeOffset>(),
            _ => throw new InvalidOperationException(),
        };
        BaseModuleCapturedFieldReference field = new()
        {
            CaptureId = "existing", StableFieldId = "record.value", Authority = valueType,
        };
        BaseRegisteredModuleMutationDefinition source = Definition();
        BaseRegisteredModuleMutationDefinition definition = source with
        {
            Template = source.Template with
            {
                Guards =
                [
                    new BaseModuleFieldComparisonGuard
                    {
                        Id = "ordered", Field = field,
                        Comparison = BaseModuleOrderedComparisonKind.GreaterThan,
                        Expected = new BaseModuleConstantExpression
                        {
                            Id = "expected", ResultType = valueType,
                            CanonicalBaseJson = expectedJson.ToArray().ToImmutableArray(),
                        },
                    },
                ],
            },
        };
        BaseCapturedAtomicExecution captured = Captured();
        BaseCapturedModuleRecord record = captured.ModuleRecords[0];
        captured = captured with
        {
            ModuleRecords =
            [
                record with
                {
                    Current = record.Current! with
                    {
                        Payload = new RecordPayload
                        {
                            Kind = RecordPayloadKind.FieldMap,
                            Fields = new Dictionary<string, System.Text.Json.JsonElement> { ["value"] = capturedValue },
                        },
                    },
                },
            ],
        };
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            definition, Identity(), new EvaluatorRequest { Amount = 0, Enabled = true }, captured,
            new Dictionary<string, CollectionDefinition>
            {
                ["records"] = new CollectionDefinition
                {
                    Id = "records", Name = "records", Kind = BaseCollectionKinds.Document,
                    SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                    MutationMode = BaseCollectionMutationMode.Mutable,
                    Fields = [new FieldDefinition { Id = "record.value", ApplicationName = "Value", WireName = "value", Type = fieldType }],
                },
            });
        return evaluator.Guard("ordered");
    }

    private static BaseGeneratedModuleMutationIdentity<EvaluatorRequest, EvaluatorResult> Identity() => new(
        "module.test", 1, new byte[32], EvaluatorJsonContext.Default.EvaluatorRequest,
        EvaluatorJsonContext.Default.EvaluatorResult,
        [
            BaseModuleDtoPropertyBinding.Create<EvaluatorRequest, long>("request.amount", nameof(EvaluatorRequest.Amount), BaseGeneratedModuleScalarManifest.Primitive<long>()),
            BaseModuleDtoPropertyBinding.Create<EvaluatorRequest, bool>("request.enabled", nameof(EvaluatorRequest.Enabled), BaseGeneratedModuleScalarManifest.Primitive<bool>()),
        ],
        [BaseModuleDtoPropertyBinding.Create<EvaluatorResult, long>("result.amount", nameof(EvaluatorResult.Amount), BaseGeneratedModuleScalarManifest.Primitive<long>())]);

    private static BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> GenerationIdentity() => new(
        "module.increment", 1, new byte[32], EvaluatorJsonContext.Default.GenerationRequest,
        EvaluatorJsonContext.Default.GenerationResult,
        [BaseModuleDtoPropertyBinding.Create<GenerationRequest, string>(
            "generation.request.scope", nameof(GenerationRequest.Scope), BaseGeneratedModuleScalarManifest.Primitive<string>())],
        [BaseModuleDtoPropertyBinding.Create<GenerationResult, string>("result.generation", nameof(GenerationResult.Generation), BaseGeneratedModuleScalarManifest.Primitive<string>())]);

    private static BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> GenerationIdentity(
        BaseRegisteredModuleMutationDefinition definition) => new(
        definition.Id, definition.Version, definition.Checksum.ToArray(),
        EvaluatorJsonContext.Default.GenerationRequest, EvaluatorJsonContext.Default.GenerationResult,
        [BaseModuleDtoPropertyBinding.Create<GenerationRequest, string>(
            "generation.request.scope", nameof(GenerationRequest.Scope), BaseGeneratedModuleScalarManifest.Primitive<string>())],
        [BaseModuleDtoPropertyBinding.Create<GenerationResult, string>(
            "result.generation", nameof(GenerationResult.Generation), BaseGeneratedModuleScalarManifest.Primitive<string>())]);

    private static BaseGeneratedModuleMutationIdentity<CreateRequest, CreateResult> CreateIdentity() => new(
        "module.create", 1, new byte[32], EvaluatorJsonContext.Default.CreateRequest, EvaluatorJsonContext.Default.CreateResult,
        [
            BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.id", nameof(CreateRequest.Id), BaseGeneratedModuleScalarManifest.Primitive<string>()),
            BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.name", nameof(CreateRequest.Name), BaseGeneratedModuleScalarManifest.Primitive<string>()),
        ],
        [BaseModuleDtoPropertyBinding.Create<CreateResult, string>("result.id", nameof(CreateResult.Id), BaseGeneratedModuleScalarManifest.Primitive<string>())]);

    private static BaseGeneratedModuleMutationIdentity<CreateRequest, CreateResult> CreateIdentity(
        BaseRegisteredModuleMutationDefinition definition) => new(
        definition.Id, definition.Version, definition.Checksum.ToArray(),
        EvaluatorJsonContext.Default.CreateRequest, EvaluatorJsonContext.Default.CreateResult,
        [
            BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.id", nameof(CreateRequest.Id), BaseGeneratedModuleScalarManifest.Primitive<string>()),
            BaseModuleDtoPropertyBinding.Create<CreateRequest, string>("request.name", nameof(CreateRequest.Name), BaseGeneratedModuleScalarManifest.Primitive<string>()),
        ],
        [BaseModuleDtoPropertyBinding.Create<CreateResult, string>("result.id", nameof(CreateResult.Id), BaseGeneratedModuleScalarManifest.Primitive<string>())]);

    private static BaseRegisteredModuleMutationDefinition CreateDefinition() => BaseModuleMutationContract.Seal(new()
    {
        Id = "module.create", Version = 1, OwningModuleId = "module", GrantId = "module.create",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = ["module-records"],
        SystemSourceGrants = [new() { CollectionId = "module-records", GrantId = "module.records.source" }],
        GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleRecordCapture { Id = "record", CollectionId = "module-records", Presence = BaseModuleCapturePresence.RequireMissing, RecordId = RecordIdFromRequest("id") }],
            Guards = [],
            Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    new BaseModuleCreateStatement
                    {
                        Id = "create", CollectionId = "module-records", RecordId = RecordIdFromRequest("create-id"),
                        Payload = new BaseModuleObjectExpression
                        {
                            Id = "payload", Properties =
                            [new BaseModuleObjectPropertyExpression { StablePropertyId = "field.name", Value = Request("request.name", "name") }],
                        },
                    },
                    new BaseModulePatchStatement
                    {
                        Id = "patch", CollectionId = "module-records", RecordId = RecordIdFromRequest("patch-id"),
                        Patch = new BaseModuleObjectExpression
                        {
                            Id = "patch-payload", Properties =
                            [new BaseModuleObjectPropertyExpression { StablePropertyId = "field.name", Value = Constant("grace", "\"Grace\""u8) }],
                        },
                    },
                ],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", Properties =
                    [new BaseModuleObjectPropertyExpression { StablePropertyId = "result.id", Value = new BaseModuleCommittedRecordIdExpression { Id = "committed-id", ResultType = Dto<string>("result.id").ValueType, StatementId = "create" } }],
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
        Fields = [new FieldDefinition { Id = "field.name", ApplicationName = "Name", WireName = "name", Type = "string", Presence = BaseFieldPresence.Required }],
    };

    private static FieldDefinition SourceRelationField()
    {
        BaseModuleValueType authority = Type<string>();
        return new FieldDefinition
        {
            Id = "field.owner", ApplicationName = "Owner", WireName = "owner", Type = BaseFieldTypes.String,
            Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable,
            ScalarKind = BaseScalarKind.String, ScalarCodec = authority.Codec,
            ScalarConstraints = authority.Constraints, ScalarConstraintChecksum = authority.ConstraintChecksum,
            Relation = new RelationDefinition
            {
                Id = "module-record-owner", SourceCollectionId = "module-records",
                SourceFieldId = "field.owner", TargetCollectionId = "module-targets",
            },
        };
    }

    private static FieldDefinition SourceScalarField()
    {
        BaseModuleValueType authority = Type<string>();
        return ModuleCollection().Fields![0] with
        {
            Nullability = BaseFieldNullability.NonNullable,
            ScalarKind = BaseScalarKind.String,
            ScalarCodec = authority.Codec,
            ScalarConstraints = authority.Constraints,
            ScalarConstraintChecksum = authority.ConstraintChecksum,
        };
    }

    private static (BaseRegisteredModuleMutationDefinition Definition, CollectionDefinition Source,
        CollectionDefinition Target) PartialUpsertDefinition(BaseModuleValueExpression? updateOwner)
    {
        CollectionDefinition source = ModuleCollection() with
        {
            Fields = [SourceScalarField(), SourceRelationField()],
        };
        CollectionDefinition target = ModuleCollection() with
        {
            Id = "module-targets",
            Name = "module-targets",
        };
        BaseRegisteredModuleMutationDefinition draft = CreateDefinition();
        var createPayload = new BaseModuleObjectExpression
        {
            Id = "upsert-create-payload",
            Properties =
            [
                new BaseModuleObjectPropertyExpression
                {
                    StablePropertyId = "field.name",
                    Value = Request("request.name", "request-name-upsert-create"),
                },
                new BaseModuleObjectPropertyExpression
                {
                    StablePropertyId = "field.owner",
                    Value = Request("request.id", "request-owner-upsert-create"),
                },
            ],
        };
        var updateProperties = new List<BaseModuleObjectPropertyExpression>
        {
            new()
            {
                StablePropertyId = "field.name",
                Value = Request("request.name", "request-name-upsert-update"),
            },
        };
        if (updateOwner is not null)
        {
            updateProperties.Add(new BaseModuleObjectPropertyExpression
            {
                StablePropertyId = "field.owner",
                Value = updateOwner,
            });
        }
        var upsert = new BaseModuleUpsertStatement
        {
            Id = "upsert",
            CollectionId = source.Id,
            RecordId = RecordIdFromRequest("upsert-id"),
            Create = createPayload,
            Update = new BaseModuleObjectExpression
            {
                Id = "upsert-update-payload",
                Properties = updateProperties.ToImmutableArray(),
            },
            UpdateMode = RecordUpsertUpdateMode.Patch,
        };
        BaseRegisteredModuleMutationDefinition definition = BaseModuleMutationContract.Seal(draft with
        {
            SystemCollectionIds = [source.Id, target.Id],
            SystemSourceGrants =
            [
                .. draft.SystemSourceGrants,
                new BaseModuleSystemSourceGrant
                {
                    CollectionId = target.Id,
                    GrantId = "module.targets.source",
                },
            ],
            Template = draft.Template with
            {
                Body = new BaseModuleMutationBlock { Statements = [upsert] },
                Result = new BaseModuleResultProjection
                {
                    Value = new BaseModuleObjectExpression
                    {
                        Id = "upsert-result",
                        Properties =
                        [
                            new BaseModuleObjectPropertyExpression
                            {
                                StablePropertyId = "result.id",
                                Value = new BaseModuleCommittedRecordIdExpression
                                {
                                    Id = "upsert-committed-id",
                                    ResultType = Dto<string>("result.id").ValueType,
                                    StatementId = upsert.Id,
                                },
                            },
                        ],
                    },
                },
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[BaseModuleMutationChecksum.Length]),
        });
        return (definition, source, target);
    }

    private static CollectionDefinition IndexedModuleCollection()
    {
        CollectionDefinition collection = ModuleCollection();
        FieldDefinition field = collection.Fields![0] with
        {
            ScalarKind = BaseScalarKind.String,
            ScalarCodec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.String),
            ScalarConstraints = new BaseScalarConstraintSet(),
        };
        BaseLogicalIndexDefinition index = new()
        {
            Id = BaseLogicalIndexId.Create("module-records.by-name"),
            Version = 1,
            CollectionId = collection.Id,
            Parts = [new BaseLogicalIndexPart
            {
                FieldOrdinal = 0,
                Direction = BaseIndexSortDirection.Ascending,
                Collation = BaseIndexCollation.OrdinalBinary,
                NullOrder = BaseIndexNullOrder.MissingThenNullThenValue,
            }],
            Unique = false,
            StoreRequired = true,
            MembershipPredicate = new BaseIndexPredicateRegistry
            {
                Root = BaseIndexPredicateId.Create("p0"),
                Nodes = [new BaseIndexPredicateNode
                {
                    Id = BaseIndexPredicateId.Create("p0"),
                    Kind = BaseIndexPredicateNodeKind.True,
                }],
                Checksum = BaseSchemaAuthorityChecksum.Create(Enumerable.Repeat((byte)0x31, 32).ToArray()),
            },
            Checksum = BaseLogicalIndexChecksum.Create(Enumerable.Repeat((byte)0x41, 32).ToArray()),
        };
        BaseLogicalIndexDefinition sealedIndex = BaseSchemaContract.SealIndex(index, [field]);
        return collection with { Fields = [field], Indexes = [sealedIndex] };
    }

    private static BaseRegisteredModuleMutationDefinition IndexedTransitionDefinition(
        string id,
        bool delete)
    {
        BaseModuleStatement statement = delete
            ? new BaseModuleDeleteStatement
            {
                Id = "transition",
                CollectionId = "module-records",
                RecordId = RecordIdFromRequest("transition-id"),
            }
            : new BaseModulePatchStatement
            {
                Id = "transition",
                CollectionId = "module-records",
                RecordId = RecordIdFromRequest("transition-id"),
                Patch = new BaseModuleObjectExpression
                {
                    Id = "transition-payload",
                    Properties = [new BaseModuleObjectPropertyExpression
                    {
                        StablePropertyId = "field.name",
                        Value = Request("request.name", "transition-name"),
                    }],
                },
            };
        BaseModuleMutationLimits limits = Limits() with
        {
            MaximumFactBytes = 8_192,
            MaximumJournalBytes = 8_192,
            MaximumReceiptBytes = 16_384,
            MaximumTransientBytes = 131_072,
        };
        return BaseModuleMutationContract.Seal(new BaseRegisteredModuleMutationDefinition
        {
            Id = id,
            Version = 1,
            OwningModuleId = "module",
            GrantId = id,
            Audience = BaseModuleMutationAudience.System,
            RequestTypeId = "request",
            ResultTypeId = "result",
            SystemCollectionIds = ["module-records"],
            SystemSourceGrants = [new BaseModuleSystemSourceGrant
            {
                CollectionId = "module-records",
                GrantId = "module.records.source",
            }],
            GenerationCellIds = [],
            ImportedSubjectContractIds = [],
            Template = new BaseModuleMutationTemplate
            {
                Captures = [new BaseModuleRecordCapture
                {
                    Id = "record",
                    CollectionId = "module-records",
                    Presence = BaseModuleCapturePresence.RequirePresent,
                    RecordId = RecordIdFromRequest("capture-id"),
                }],
                Guards = [],
                Preconditions = [],
                Body = new BaseModuleMutationBlock { Statements = [statement] },
                Result = new BaseModuleResultProjection
                {
                    Value = new BaseModuleObjectExpression
                    {
                        Id = "result",
                        Properties = [new BaseModuleObjectPropertyExpression
                        {
                            StablePropertyId = "result.id",
                            Value = new BaseModuleCommittedRecordIdExpression
                            {
                                Id = "committed-id",
                                ResultType = Dto<string>("result.id").ValueType,
                                StatementId = "transition",
                            },
                        }],
                    },
                },
            },
            Limits = limits,
            ReceiptPolicy = new BaseModuleMutationReceiptPolicy
            {
                FormatVersion = 1,
                Lifetime = TimeSpan.FromDays(1),
            },
            Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
        });
    }

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

    private static DefaultBasePolicyOrchestrator PolicyWithReadMask(FieldMask mask)
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new ConstrainedPolicyEvaluator(readMask: mask));
        builder.AddStaticGrant(new BaseGrantAuthorityDefinition
        {
            Id = "module.increment", Version = 1, OwningModuleId = "module",
            SourceContractId = "module.grants", SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = "module.increment", ApplicationId = "module.application", ModuleId = "module",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "system" },
            Action = "module.increment", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private static BaseActivationGrantSet TransactionalActivationGrants() => new()
    {
        Enqueue = "module.transactional.enqueue", Observe = "module.transactional.observe",
        Claim = "module.transactional.claim", Execute = "module.transactional.execute",
        Renew = "module.transactional.renew", Complete = "module.transactional.complete",
        Fail = "module.transactional.fail", Yield = "module.transactional.yield", Cancel = "module.transactional.cancel",
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
            Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result",
                    Properties =
                    [
                        new BaseModuleObjectPropertyExpression
                        {
                            StablePropertyId = "result.generation",
                            Value = new BaseModuleResultingGenerationExpression { Id = "result-generation", ResultType = Dto<string>("result.generation").ValueType, CaptureId = "generation" },
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
        MaximumPreconditions = 8, MaximumRequestGuardEvaluations = 32,
        MaximumStaticSetMembers = 128, MaximumStaticSetComparisons = 8_128,
        MaximumDisabledCaptures = 8, MaximumRemovedFields = 8,
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
            Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements = [new BaseModuleRequireStatement { Id = "require", GuardId = "enabled", RequirementId = "enabled" }],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result",
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
        Id = id,
        ResultType = stableId == "request.amount" ? Dto<long>(stableId).ValueType : Dto<string>(stableId).ValueType,
        Property = new BaseModuleRequestPropertyReference
        {
            StablePropertyPath = [stableId],
            Authority = stableId == "request.amount" ? Dto<long>(stableId) : Dto<string>(stableId),
        },
    };

    private static BaseModuleConstantExpression Constant(string id, ReadOnlySpan<byte> bytes) => new()
    {
        Id = id,
        ResultType = !bytes.IsEmpty && bytes[0] == (byte)'"' ? Type<string>() : Type<long>(),
        CanonicalBaseJson = bytes.ToArray().ToImmutableArray(),
    };

    private static BaseModuleValueType Type<TValue>(
        BaseFieldNullability? nullability = null) =>
        BaseModuleValueAuthorityContract.Primitive<TValue>(nullability: nullability);

    private static BaseModuleValueType RecordIdType(string collectionId)
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.RecordId);
        var constraints = new BaseScalarConstraintSet
        {
            MinimumUtf8Bytes = 1,
            MaximumUtf8Bytes = 256,
            StringNormalization = BaseStringNormalizationRequirement.RequireNfc,
        };
        BaseScalarConstraintChecksum checksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(
            "module-records", "record-id", BaseFieldPresence.Required,
            BaseFieldNullability.NonNullable, codec, constraints);
        return BaseModuleValueAuthorityContract.Create(
            BaseModuleValueKind.RecordId, BaseFieldPresence.Required,
            BaseFieldNullability.NonNullable, codec, constraints, checksum, collectionId);
    }

    private static BaseModuleRecordIdConversionExpression RecordIdFromRequest(string id) => new()
    {
        Id = id,
        ResultType = RecordIdType("module-records"),
        Conversion = BaseModuleRecordIdConversionKind.CanonicalString,
        Source = Request("request.id", id + ".source"),
    };

    private static BaseRegisteredModuleMutationDefinition DefinitionWithResultExpression(
        BaseModuleValueExpression expression) => Definition() with
        {
            Limits = Limits(),
            Template = Definition().Template with
            {
                Result = new BaseModuleResultProjection
                {
                    Value = new BaseModuleObjectExpression
                    {
                        Id = "result",
                        Properties = [new BaseModuleObjectPropertyExpression { StablePropertyId = "result.amount", Value = expression }],
                    },
                },
            },
        };

    private static BaseModuleDtoScalarAuthority Dto<TValue>(params string[] path) =>
        BaseGeneratedModuleScalarManifest.Primitive<TValue>().Seal(path);

    private static BaseCapturedAtomicExecution Captured() => new()
    {
        Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
        IntentDigest = "intent", CaptureDigest = "capture", Authority = null!,
        Items = [],
        ModuleRecords =
        [
            new BaseCapturedModuleRecord
            {
                Ordinal = 0, CaptureId = "existing", CollectionId = "records", RecordId = RecordId.Create("one"), Exists = true,
                Current = new RecordEnvelope
                {
                    CollectionId = "records", Id = RecordId.Create("one"),
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

file sealed class ModuleMutationTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    internal void Advance(TimeSpan duration) => _now = _now.Add(duration);
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

public sealed record NestedEvaluatorRequest
{
    public required NestedEvaluatorValue Wrapper { get; init; }
}

public sealed record NestedEvaluatorValue
{
    public required long Amount { get; init; }
}

public sealed record GenerationRequest
{
    [BaseField("generation.request.scope", MaximumUtf8Bytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public string Scope { get; init; } = "application";
}
public sealed record GenerationResult
{
    [BaseField("generation.result.value", MaximumUtf8Bytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Generation { get; init; }
}
public sealed record CreateRequest { public required string Id { get; init; } public required string Name { get; init; } }
public sealed record CreateResult { public required string Id { get; init; } }
public sealed record UnsupportedResult { public required DateTimeOffset Id { get; init; } }

[BaseCollection("module-records", typeof(EvaluatorJsonContext), SystemOwnerModuleId = "module")]
public sealed partial record ModuleMutationRecord
{
    [BaseField("field.name")]
    public required string Name { get; init; }
}

[BaseCollection("l67-records", typeof(EvaluatorJsonContext))]
public sealed partial record L67Record
{
    [BaseField("l67-record.hash-id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)]
    public required string HashId { get; init; }

    [BaseField("l67-record.name")]
    public required string Name { get; init; }
}

[JsonSerializable(typeof(EvaluatorRequest))]
[JsonSerializable(typeof(EvaluatorResult))]
[JsonSerializable(typeof(NestedEvaluatorRequest))]
[JsonSerializable(typeof(GenerationRequest))]
[JsonSerializable(typeof(GenerationResult))]
[JsonSerializable(typeof(CreateRequest))]
[JsonSerializable(typeof(CreateResult))]
[JsonSerializable(typeof(UnsupportedResult))]
[JsonSerializable(typeof(ModuleMutationRecord))]
[JsonSerializable(typeof(L67Record))]
internal sealed partial class EvaluatorJsonContext : JsonSerializerContext;

[BaseActivationDtoAuthority("module.transactional.dto", 1, "module", "request", "result",
    typeof(EvaluatorJsonContext), typeof(GenerationRequest), typeof(GenerationResult))]
internal static partial class EvaluatorActivationDtos;
