using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class BaseModuleProgramEvaluatorTests
{
    [Theory]
    [MemberData(nameof(InexactGrants))]
    public async Task Inexact_grant_with_matching_registration_id_is_not_L50_authority(AccessGrant grant)
    {
        DefaultBasePolicyOrchestrator orchestrator = PolicyWithGrant(grant);
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
        OperationResult<BasePolicyEvaluation> result = await orchestrator.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = principal, Operation = operation, Collection = ModuleCollection(),
            ResourceKind = PolicyResourceKind.ModuleMutation,
        });

        BaseSystemCollectionGate.HasExactModuleGrant(result, "module.increment", "module", principal, operation).Should().BeFalse();
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

    [Fact]
    public void Canonical_checksum_matches_the_locked_template_byte_vector()
    {
        string actual = Convert.ToHexString(GenerationDefinition().Checksum.ToArray());
        actual.Should().Be("21459C33459A1E437A90889EF6F9CF42F06B554A01B1D07D2997D4FF0404A62C");
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
            SystemCollectionIds = [], GenerationCellIds = ["module.a", "module.b"], ImportedSubjectContractIds = [],
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
        DefaultBasePolicyOrchestrator policy = Policy("module.create");
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
        BaseCapturedAtomicMutationAuthority captured = Captured();
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
        SystemCollectionIds = ["module-records"], GenerationCellIds = [], ImportedSubjectContractIds = [],
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
                Action = grantId,
                Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
            });
        }
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

    private static BaseRegisteredModuleMutationDefinition GenerationDefinition() => BaseModuleMutationContract.Seal(new()
    {
        Id = "module.increment", Version = 1, OwningModuleId = "module", GrantId = "module.increment",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], GenerationCellIds = ["module.generation"], ImportedSubjectContractIds = [],
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
        SystemCollectionIds = [], GenerationCellIds = [], ImportedSubjectContractIds = [],
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

    private static BaseCapturedAtomicMutationAuthority Captured() => new()
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
