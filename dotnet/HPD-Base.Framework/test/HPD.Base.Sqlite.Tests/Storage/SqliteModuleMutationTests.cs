using System.Text.Json.Serialization;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed partial class SqliteModuleMutationTests
{
    [Fact]
    public async Task Generation_operation_commits_replays_and_survives_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-{Guid.NewGuid():N}.db");
        try
        {
            BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
                "module", "increment", "one", BaseMutationRequestFingerprint.Create(new byte[32]));
            await using (SqliteRecordStore store = Store(path))
            {
                DefaultBaseModuleMutationRuntime runtime = Runtime(store);
                BaseResult<BaseModuleMutationExecutionResult<Result>> first = await runtime.ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(), requestIdentity, null, default);
                BaseResult<BaseModuleMutationExecutionResult<Result>> duplicate = await runtime.ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(), requestIdentity, null, default);

                first.RequireValue().Result.Generation.Should().Be("1");
                first.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
                duplicate.RequireValue().Result.Generation.Should().Be("1");
                duplicate.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            }

            await using (SqliteRecordStore reopened = Store(path))
            {
                BaseResult<BaseModuleMutationExecutionResult<Result>> resolved = await Runtime(reopened).ResolveAsync(
                    Session(), Definition(), Identity(), requestIdentity, default);
                resolved.RequireValue().Result.Generation.Should().Be("1");
                resolved.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Prepared_module_operation_is_session_bound_and_single_use()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-prepared-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "module.application", [], ExecutionLimits(), default)).Value!;
            var prepareOnly = new PreparedPlanProbe(authority, applyTwice: false);
            await store.ExecuteAtomicAsync(prepareOnly, ExecutionRequest());
            prepareOnly.Prepared.Should().NotBeNull();

            var foreign = new ForeignPreparedProbe(prepareOnly.Prepared!);
            await store.ExecuteAtomicAsync(foreign, ExecutionRequest());
            foreign.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);

            var twice = new PreparedPlanProbe(authority, applyTwice: true);
            await store.ExecuteAtomicAsync(twice, ExecutionRequest());
            twice.RejectedCode.Should().Be(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Generation_provider_accounting_is_enforced_at_exact_boundaries()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-accounting-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteRecordStore store = Store(path);
            BaseAtomicMutationExecutionLimits generous = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "module.application", [], generous, default)).Value!;
            var baseline = new PreparedPlanProbe(authority, applyTwice: false, generous);
            await store.ExecuteAtomicAsync(baseline, ExecutionRequest());
            BasePreparedAtomicMutationAccounting measured = baseline.Prepared!.Accounting;

            BaseAtomicMutationExecutionLimits[] exactLimits =
            [
                generous with { MaximumGenerationReads = measured.GenerationReads },
                generous with { MaximumGenerationIncrements = measured.GenerationIncrements },
                generous with { MaximumReadIntervals = measured.ReadIntervals },
                generous with { MaximumGenerationBytes = measured.GenerationBytes },
                generous with { MaximumEvidenceBytes = measured.EvidenceBytes },
                generous with { MaximumTransientBytes = measured.TransientBytes },
            ];
            foreach (BaseAtomicMutationExecutionLimits exact in exactLimits)
            {
                var accepted = new PreparedPlanProbe(authority, applyTwice: false, exact);
                await store.ExecuteAtomicAsync(accepted, ExecutionRequest());
                accepted.Prepared.Should().NotBeNull();
            }

            BaseAtomicMutationExecutionLimits[] belowLimits =
            [
                generous with { MaximumGenerationReads = checked(measured.GenerationReads - 1) },
                generous with { MaximumGenerationIncrements = checked(measured.GenerationIncrements - 1) },
                generous with { MaximumReadIntervals = checked(measured.ReadIntervals - 1) },
                generous with { MaximumGenerationBytes = checked(measured.GenerationBytes - 1) },
                generous with { MaximumEvidenceBytes = checked(measured.EvidenceBytes - 1) },
                generous with { MaximumTransientBytes = checked(measured.TransientBytes - 1) },
            ];
            for (int index = 0; index < belowLimits.Length; index++)
            {
                BaseAtomicMutationExecutionLimits below = belowLimits[index];
                var rejected = new PreparedPlanProbe(authority, applyTwice: false, below);
                await store.ExecuteAtomicAsync(rejected, ExecutionRequest());
                rejected.Prepared.Should().BeNull("boundary {0} must reject measured work plus one", index);
                rejected.RejectedCode.Should().NotBeNull("boundary {0} must report a stable provider failure", index);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Operation_and_cell_removal_are_rejected_while_receipt_and_generation_authority_remain()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-removal-{Guid.NewGuid():N}.db");
        string cellPath = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-cell-removal-{Guid.NewGuid():N}.db");
        try
        {
            await using (SqliteRecordStore store = Store(path))
            {
                BaseResult<BaseModuleMutationExecutionResult<Result>> committed = await Runtime(store).ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(),
                    BaseMutationRequestIdentity.Create("module", "increment", "retained", BaseMutationRequestFingerprint.Create(new byte[32])),
                    null, default);
                committed.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<Result>>>();
            }

            Func<Task> remove = async () => await Store(path, installModuleAssets: false).DisposeAsync();
            await remove.Should().ThrowAsync<InvalidOperationException>().WithMessage("base.moduleMutation.removalRequired");

            await using (SqliteRecordStore store = Store(cellPath))
            {
                await Runtime(store).ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(),
                    BaseMutationRequestIdentity.Create("module", "increment", "cell-retained", BaseMutationRequestFingerprint.Create(new byte[32])),
                    null, default);
            }
            Func<Task> removeCell = async () => await Store(cellPath, installOperation: true, installCell: false).DisposeAsync();
            await removeCell.Should().ThrowAsync<InvalidOperationException>().WithMessage("base.moduleMutation.removalRequired");
        }
        finally
        {
            foreach (string target in new[] { path, cellPath })
                foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(target + suffix)) File.Delete(target + suffix);
        }
    }

    [Fact]
    public async Task Operation_checksum_drift_is_rejected_during_schema_installation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-drift-{Guid.NewGuid():N}.db");
        try
        {
            await Store(path).DisposeAsync();
            BaseRegisteredModuleMutationDefinition drifted = Definition() with
            {
                Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData("drift"u8)),
            };
            Func<Task> reopen = async () => await Store(path, operation: drifted).DisposeAsync();
            await reopen.Should().ThrowAsync<InvalidOperationException>().WithMessage("base.moduleMutation.schemaDrift");
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Module_receipts_and_generations_round_trip_through_backup_restore()
    {
        string temporary = Path.GetFullPath(Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && temporary.StartsWith("/var/", StringComparison.Ordinal))
            temporary = "/private" + temporary;
        string path = Path.Combine(temporary, $"hpd-base-l50-administration-{Guid.NewGuid():N}.db");
        using BaseOpaqueTokenProtector protector = Protector();
        try
        {
            await using SqliteRecordStore store = AdministrationStore(path, protector);
            store.AdministrationCapability.Backup.Should().BeTrue();
            DefaultBaseModuleMutationRuntime runtime = Runtime(store);
            BaseMutationRequestIdentity original = BaseMutationRequestIdentity.Create(
                "module", "increment", "before-backup", BaseMutationRequestFingerprint.Create(new byte[32]));
            (await runtime.ExecuteAsync(Session(), Definition(), Identity(), new Request(), original, null, default))
                .RequireValue().Result.Generation.Should().Be("1");

            var artifact = new MemoryStream();
            OperationResult<BaseBackupManifest> backup = await store.CreateBackupAsync(artifact, new BaseBackupRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
            });
            backup.IsSuccess().Should().BeTrue(backup.Error?.Code);

            byte[] corrupted = artifact.ToArray();
            corrupted[corrupted.Length / 2] ^= 0xff;
            OperationResult<BaseBackupManifest> validation = await store.ValidateBackupAsync(
                new MemoryStream(corrupted),
                new BaseBackupValidationRequest { StoreId = "module-store", Principal = AdministrationPrincipal() });
            validation.Error!.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);

            (await runtime.ExecuteAsync(Session(), Definition(), Identity(), new Request(),
                BaseMutationRequestIdentity.Create("module", "increment", "after-backup", BaseMutationRequestFingerprint.Create(new byte[32])),
                null, default)).RequireValue().Result.Generation.Should().Be("2");

            artifact.Position = 0;
            BaseBackupManifest manifest = backup.Value!;
            OperationResult<BaseRestoreResult> restore = await store.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
            });
            restore.IsSuccess().Should().BeTrue(restore.Error?.Code);

            (await Runtime(store).ResolveAsync(Session(), Definition(), Identity(), original, default))
                .RequireValue().Result.Generation.Should().Be("1");
            (await Runtime(store).ExecuteAsync(Session(), Definition(), Identity(), new Request(),
                BaseMutationRequestIdentity.Create("module", "increment", "after-restore", BaseMutationRequestFingerprint.Create(new byte[32])),
                null, default)).RequireValue().Result.Generation.Should().Be("2");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!)
                .Where(file => Path.GetFileName(file).Contains(Path.GetFileName(path), StringComparison.Ordinal)))
                File.Delete(candidate);
        }
    }

    private static SqliteRecordStore AdministrationStore(string path, BaseOpaqueTokenProtector protector)
    {
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, AdministrationEnabled = true,
            Collections = [SqliteTestFactory.Collection()], ModuleMutations = [Definition()],
            ModuleGenerationCells = [Cell()], MaxBackupArtifactBytes = 16 * 1024 * 1024,
        };
        SqliteRecordStore store = SqliteTestFactory.Create(options, tokenProtector: protector);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'module-store-instance');
            INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
            VALUES ('module.application','module-store-instance','baseline-1','checksum-1',1,'plan-1','2026-08-19T00:00:00Z');
            """;
        command.ExecuteNonQuery();
        return store;
    }

    private static BaseOpaqueTokenProtector Protector() => new(Microsoft.Extensions.Options.Options.Create(
        new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 50, Key = Enumerable.Repeat((byte)0x50, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }));

    private static PrincipalContext AdministrationPrincipal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "system",
    };

    private static SqliteRecordStore Store(
        string path,
        bool installModuleAssets = true,
        BaseRegisteredModuleMutationDefinition? operation = null,
        bool? installOperation = null,
        bool? installCell = null)
    {
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, Collections = [],
        };
        if (installOperation ?? installModuleAssets)
            options.ModuleMutations = [operation ?? Definition()];
        if (installCell ?? installModuleAssets)
            options.ModuleGenerationCells = [Cell()];
        var store = new SqliteRecordStore(options, NullLoggerFactory.Instance);
        store.InitializeUnacceptedSchemaForTestsAsync().AsTask().GetAwaiter().GetResult();
        return store;
    }

    private static DefaultBaseModuleMutationRuntime Runtime(SqliteRecordStore store)
    {
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseRegisteredModuleMutationDefinition definition = Definition();
        BaseModuleGenerationCellDefinition cell = Cell();
        return new DefaultBaseModuleMutationRuntime(stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            new BaseModuleMutationRegistry([definition], [cell]), null!, Policy(), null!, new BaseSubjectContractRegistry([]), TimeProvider.System);
    }

    private static BaseModuleGenerationCellDefinition Cell() => new()
    {
        Id = "module.generation", Version = 1, OwningModuleId = "module",
        Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
    };

    private static BaseSession Session() => new(null!, TimeProvider.System,
        new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "system" },
        new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane }, applicationId: "module.application");

    private static DefaultBasePolicyOrchestrator Policy()
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
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

    private static BaseGeneratedModuleMutationIdentity<Request, Result> Identity() => new(
        "module.increment", 1, new byte[32], Json.Default.Request, Json.Default.Result, [],
        [BaseModuleDtoPropertyBinding.Create<Result, string>("result.generation", nameof(Result.Generation))]);

    private static BaseRegisteredModuleMutationDefinition Definition() => BaseModuleMutationContract.Seal(new()
    {
        Id = "module.increment", Version = 1, OwningModuleId = "module", GrantId = "module.increment",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = ["module.generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleGenerationCapture { Id = "generation", CellId = "module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }],
            Guards = [],
            Body = new BaseModuleMutationBlock { Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }] },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "result",
                    Properties = [new BaseModuleObjectPropertyExpression
                    {
                        StablePropertyId = "result.generation",
                        Value = new BaseModuleResultingGenerationExpression { Id = "result-generation", ResultTypeId = "string", CaptureId = "generation" },
                    }],
                },
            },
        },
        Limits = Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    });

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8, MaximumGenerationCaptures = 8,
        MaximumRecordMutations = 8, MaximumGenerationReads = 8, MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8,
        MaximumGuardNodes = 8, MaximumGuardDepth = 8, MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32,
        MaximumReadIntervals = 16, MaximumSubjectValidations = 8, MaximumAuthorityReads = 16, MaximumRelationChecks = 8,
        MaximumUniqueConstraintChecks = 8, MaximumRequestBytes = 4096, MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096,
        MaximumEvidenceBytes = 4096, MaximumWrittenBytes = 4096, MaximumFactBytes = 4096, MaximumJournalBytes = 4096,
        MaximumReceiptBytes = 4096, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };

    private static BaseAtomicMutationExecutionLimits ExecutionLimits() =>
        DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(Limits());

    private static RecordMutationExecutionRequest ExecutionRequest() => new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5),
    };

    private sealed class PreparedPlanProbe(
        BaseAtomicMutationAuthorityRequirement authority,
        bool applyTwice,
        BaseAtomicMutationExecutionLimits? suppliedLimits = null) : IAtomicMutationProcessor
    {
        public BasePreparedAtomicMutation? Prepared { get; private set; }
        public string? RejectedCode { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            BaseAtomicMutationExecutionLimits limits = suppliedLimits ?? ExecutionLimits();
            var capture = new BaseAtomicMutationCaptureRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
                Intent = new BaseAtomicMutationIntent { IntentDigest = "l50-probe-intent", Authority = authority, Items = [] },
                Module = new BaseModuleMutationCaptureExtension
                {
                    OperationId = Definition().Id, OperationVersion = Definition().Version,
                    OperationChecksum = Convert.ToHexString(Definition().Checksum.ToArray()).ToLowerInvariant(),
                    RequestDigest = "l50-probe-request", Records = [], RelationTargets = [],
                    Generations = [new BaseModuleGenerationCaptureRequest
                    {
                        Ordinal = 0, CaptureId = "generation", Cell = Cell(),
                        Scope = new BaseModuleGenerationScopeAuthority { Kind = BaseModuleGenerationScope.Application },
                        KeyUtf8 = ImmutableArray<byte>.Empty, Absence = BaseModuleGenerationAbsenceBehavior.AllowEither,
                    }],
                },
                Limits = limits,
            };
            OperationResult<BaseCapturedAtomicMutationAuthority> captured =
                await session.CaptureAtomicMutationAuthorityAsync(capture, cancellationToken);
            if (!captured.IsSuccess() || captured.Value is null)
            {
                RejectedCode = captured.Error?.Code;
                return Failure(captured.Error);
            }
            var plan = new BaseAtomicMutationPlan
            {
                Kind = BaseAtomicMutationExecutionKind.ModuleMutation, PlanDigest = "l50-probe-plan",
                IntentDigest = capture.Intent.IntentDigest, CaptureDigest = captured.Value.CaptureDigest,
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]), Authority = authority,
                Items = [], SubjectValidations = [], Limits = limits,
                Module = new BaseFinalizedModuleMutationExtension
                {
                    OperationId = Definition().Id, OperationVersion = Definition().Version,
                    OperationChecksum = Convert.ToHexString(Definition().Checksum.ToArray()).ToLowerInvariant(),
                    Decisions = [], ItemBindings = [], RelationTargets = [], Comparisons = [],
                    Increments = [new BaseModuleGenerationIncrement { CaptureOrdinal = 0, CreateIfAbsent = true }],
                    ResultProjectionDigest = "l50-probe-result",
                },
            };
            OperationResult<BasePreparedAtomicMutation> prepared =
                await session.PrepareAtomicMutationAsync(captured.Value, plan, cancellationToken);
            if (!prepared.IsSuccess() || prepared.Value is null)
            {
                RejectedCode = prepared.Error?.Code;
                return Failure(prepared.Error);
            }
            Prepared = prepared.Value;
            if (!applyTwice) return Failure(null);
            OperationResult<BaseProvisionalAppliedAtomicMutation> first =
                await session.ApplyPreparedAtomicMutationAsync(prepared.Value, cancellationToken);
            if (!first.IsSuccess()) return Failure(first.Error);
            OperationResult<BaseProvisionalAppliedAtomicMutation> second =
                await session.ApplyPreparedAtomicMutationAsync(prepared.Value, cancellationToken);
            RejectedCode = second.Error?.Code;
            return Failure(second.Error);
        }
    }

    private sealed class ForeignPreparedProbe(BasePreparedAtomicMutation prepared) : IAtomicMutationProcessor
    {
        public string? RejectedCode { get; private set; }
        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseProvisionalAppliedAtomicMutation> result =
                await session.ApplyPreparedAtomicMutationAsync(prepared, cancellationToken);
            RejectedCode = result.Error?.Code;
            return Failure(result.Error);
        }
    }

    private static AtomicMutationProcessingResult Failure(BaseError? error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error ?? new BaseError
        {
            Code = BaseSubjectErrorCodes.ProviderContractInvalid,
            Message = "The prepared-operation probe intentionally rolled back.",
            Category = ErrorCategory.Store,
        });

    public sealed record Request;
    public sealed record Result { public required string Generation { get; init; } }
    [JsonSerializable(typeof(Request))]
    [JsonSerializable(typeof(Result))]
    internal sealed partial class Json : JsonSerializerContext;

    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }
}
