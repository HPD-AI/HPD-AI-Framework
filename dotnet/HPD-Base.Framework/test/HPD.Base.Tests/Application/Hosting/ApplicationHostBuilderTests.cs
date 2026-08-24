using FluentAssertions;
using HPD.Base;
using HPD.Base.Tests.Application.Generation;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace HPD.Base.Tests.Application.Hosting;

public sealed class ApplicationHostBuilderTests
{
    private static readonly GeneratedApplicationJsonContext MetadataOwner =
        new(BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase));
    private static GeneratedApplicationJsonContext Metadata() => MetadataOwner;
    private static BaseJsonProperty<GeneratedProject, string> ProjectProperty(string wireName) =>
        BaseJsonProperty<GeneratedProject, string>.Bind(Metadata().GeneratedProject, wireName);
    [Fact]
    public async Task FreshSqlitePlansAreBoundToDistinctPersistentPhysicalStoreIdentities()
    {
        string firstPath = Path.Combine(Path.GetTempPath(), "hpd-base-store-a-" + Guid.NewGuid().ToString("N") + ".db");
        string secondPath = Path.Combine(Path.GetTempPath(), "hpd-base-store-b-" + Guid.NewGuid().ToString("N") + ".db");
        byte[] key = Enumerable.Repeat((byte)0x73, 32).ToArray();
        static ServiceProvider Build(string path, byte[] key)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => { options.ApplicationId = "physical-store-app"; options.PlanProtectionKey = key; })
                .AddCollection(GeneratedProject.Collection)
                .UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; })));
            return services.BuildServiceProvider();
        }

        try
        {
            await using ServiceProvider firstProvider = Build(firstPath, key);
            await using ServiceProvider secondProvider = Build(secondPath, key);
            IBaseSchemaManager firstManager = firstProvider.GetRequiredService<IBaseSchemaManager>();
            IBaseSchemaManager secondManager = secondProvider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan firstPlan = (await firstManager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            BaseSchemaPlan secondPlan = (await secondManager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;

            firstPlan.PersistedStoreInstanceId.Should().NotBe(secondPlan.PersistedStoreInstanceId);
            (await secondManager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = firstPlan.ProtectedArtifact }))
                .Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanStale);

            await using ServiceProvider reconstructed = Build(firstPath, key);
            BaseSchemaPlan reconstructedPlan = (await reconstructed.GetRequiredService<IBaseSchemaManager>()
                .PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            reconstructedPlan.PersistedStoreInstanceId.Should().Be(firstPlan.PersistedStoreInstanceId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string path in new[] { firstPath, secondPath })
                foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                    if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task FreshSqlitePlansAreDeterministicAcrossCallsAndHostReconstruction()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-fresh-plan-" + Guid.NewGuid().ToString("N") + ".db");
        byte[] key = Enumerable.Repeat((byte)0x46, 32).ToArray();
        async ValueTask<BaseSchemaPlan> PlanAsync()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => { options.ApplicationId = "fresh-plan-app"; options.PlanProtectionKey = key; })
                .AddCollection(GeneratedProject.Collection)
                .UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            return (await provider.GetRequiredService<IBaseSchemaManager>()
                .PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
        }

        try
        {
            BaseSchemaPlan first = await PlanAsync();
            BaseSchemaPlan second = await PlanAsync();

            second.Classification.Should().Be(first.Classification);
            second.Operations.Should().Equal(first.Operations);
            second.LogicalPlanDigest.Should().Be(first.LogicalPlanDigest);
            second.ProviderApplyArtifactDigest.Should().Be(first.ProviderApplyArtifactDigest);
            second.PersistedStoreInstanceId.Should().Be(first.PersistedStoreInstanceId);
            second.PlanId.Should().NotBe(first.PlanId);
            second.TargetBaselineId.Should().NotBe(first.TargetBaselineId);
            second.ProtectedArtifact.Should().NotEqual(first.ProtectedArtifact);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task SqliteSchemaPlanApplyVerifyAndHistoryUseAuthenticatedBoundary()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-schema-manager-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options =>
                {
                    options.ApplicationId = "schema-manager-test";
                    options.PlanProtectionKey = Enumerable.Repeat((byte)0x55, 32).ToArray();
                })
                .AddCollection(GeneratedProject.Collection)
                .UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "schema-store"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();

            IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "schema-store" });
            planned.Status.Should().Be(OperationStatus.Ok);
            planned.Value!.Classification.Should().Be(BaseSchemaPlanClassification.SafeStructural);
            planned.Value.ProtectedArtifact.Should().NotBeEmpty();

            OperationResult<BaseSchemaApplyResult> applied = await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value.ProtectedArtifact });
            applied.Status.Should().Be(OperationStatus.Ok);
            applied.Value!.Generation.Should().Be(1);

            OperationResult<BaseApplicationReadiness> initialized = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
            initialized.Value!.State.Should().Be(BaseApplicationReadinessState.Ready);
            initialized.Value.SchemaGeneration.Should().Be(1);

            OperationResult<BaseSchemaObservedState> verified = await manager.VerifyAsync(new BaseSchemaVerifyRequest { StoreId = "schema-store" });
            verified.Status.Should().Be(OperationStatus.Ok);
            verified.Value!.Compatibility.Should().Be(BaseSchemaCompatibility.Compatible);
            verified.Value.AcceptedChecksum.Should().Be(provider.GetRequiredService<BaseLogicalSchema>().CanonicalChecksum);
            verified.Value.Assets.Should().OnlyContain(asset => asset.State == BaseSchemaAssetState.Ready);

            BaseSchemaPlan noChangeOne = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "schema-store" })).Value!;
            BaseSchemaPlan noChangeTwo = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "schema-store" })).Value!;
            noChangeOne.Classification.Should().Be(BaseSchemaPlanClassification.NoChanges);
            noChangeTwo.Classification.Should().Be(BaseSchemaPlanClassification.NoChanges);
            noChangeOne.Operations.Should().BeEmpty();
            noChangeTwo.Operations.Should().BeEmpty();
            noChangeTwo.LogicalPlanDigest.Should().Be(noChangeOne.LogicalPlanDigest);
            noChangeTwo.ProviderApplyArtifactDigest.Should().Be(noChangeOne.ProviderApplyArtifactDigest);
            noChangeTwo.PlanId.Should().NotBe(noChangeOne.PlanId);
            noChangeTwo.TargetBaselineId.Should().NotBe(noChangeOne.TargetBaselineId);
            noChangeTwo.ProtectedArtifact.Should().NotEqual(noChangeOne.ProtectedArtifact);

            OperationResult<BaseSchemaHistoryPage> history = await manager.ReadHistoryAsync("schema-store", new BaseSchemaHistoryRequest { Limit = 10 });
            history.Value!.Items.Should().ContainSingle().Which.PlanId.Should().Be(planned.Value.PlanId);

            OperationResult<BaseSchemaApplyResult> replay = await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value.ProtectedArtifact });
            replay.Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanStale);

            static string NativeCollection(string id) => "b_c_" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).Substring(0, 32);
            await using (var drift = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await drift.OpenAsync();
                await using var command = drift.CreateCommand();
                command.CommandText = $"DROP TABLE {NativeCollection(GeneratedProject.Collection.Id)};";
                await command.ExecuteNonQueryAsync();
            }
            OperationResult<BaseSchemaObservedState> drifted = await manager.VerifyAsync(new BaseSchemaVerifyRequest { StoreId = "schema-store" });
            drifted.Value!.Compatibility.Should().Be(BaseSchemaCompatibility.Drifted);
            BaseSchemaPlan blocked = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "schema-store" })).Value!;
            blocked.Classification.Should().Be(BaseSchemaPlanClassification.DriftBlocked);
            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = blocked.ProtectedArtifact })).Error!.Code
                .Should().Be(BaseSchemaErrorCodes.MigrationRequired);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task ProtectedSchemaPlanRejectsExpiryAndEveryReboundIdentity()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-schema-bindings-" + Guid.NewGuid().ToString("N") + ".db");
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-02T00:00:00Z"));
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options =>
                {
                    options.ApplicationId = "schema-binding-test";
                    options.PlanLifetime = TimeSpan.FromMinutes(1);
                    options.PlanProtectionKey = Enumerable.Repeat((byte)0x59, 32).ToArray();
                })
                .AddCollection(GeneratedProject.Collection)
                .UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "binding-store"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
            IBaseSchemaPlanProtector protector = provider.GetRequiredService<IBaseSchemaPlanProtector>();
            BaseSchemaPlan original = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "binding-store" })).Value!;
            BaseSchemaVerifiedPlan clear = protector.Unprotect(original.ProtectedArtifact).Value!;

            byte[] Rebind(Func<BaseSchemaPlan, BaseSchemaPlan> change) => protector.Protect(change(clear.Plan), clear.ProviderApplyArtifact);

            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = Rebind(plan => plan with { ApplicationId = "other-app" }) })).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanStale);
            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = Rebind(plan => plan with { StoreId = "other-store" }) })).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanStale);
            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = Rebind(plan => plan with { PersistedStoreInstanceId = "other-instance" }) })).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanInvalid);
            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = Rebind(plan => plan with { ProviderId = "other-provider" }) })).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanInvalid);
            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = Rebind(plan => plan with { PlannerVersion = "other-planner" }) })).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanInvalid);

            clock.Advance(TimeSpan.FromMinutes(2));
            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = original.ProtectedArtifact })).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanExpired);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(clear.ProviderApplyArtifact);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task SqliteAppliesExactAdditiveArtifactAndTreatsStableFieldRenameAsMetadataOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-schema-evolution-" + Guid.NewGuid().ToString("N") + ".db");
        byte[] key = Enumerable.Repeat((byte)0x31, 32).ToArray();
        byte[] attestationKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
        BaseCollection<GeneratedProject> Version(string storedName, bool extra, bool requiredNew = false) => HPD.Base.BaseCollection.Define(
            "schema-evolution-projects", Metadata().GeneratedProject, schema =>
            {
                schema.String("schema-evolution.name", storedName, ProjectProperty("name"));
                if (extra)
                {
                    var field = schema.String("schema-evolution.extra", "OrganizationId", ProjectProperty("organizationId"));
                    _ = field;
                }
                if (requiredNew) schema.String("schema-evolution.required", "OptionalNote", ProjectProperty("optionalNote")).Required();
            });
        async ValueTask<(BaseSchemaPlan Plan, BaseSchemaApplyResult? Applied, BaseSchemaHistoryPage? History)> RunAsync(BaseCollection<GeneratedProject> collection, bool apply, BaseExternalMigrationAttestation? attestation = null)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder.ConfigureSchema(options => { options.ApplicationId = "schema-evolution"; options.PlanProtectionKey = key; options.ExternalMigrationAttestationKey = attestationKey; }).AddCollection(collection).UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite", ExternalMigrationAttestation = attestation })).Value!;
            if (!apply) return (plan, null, null);
            OperationResult<BaseSchemaApplyResult> result = await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact, AllowDestructive = true });
            result.IsSuccess().Should().BeTrue((result.Error?.Code ?? "unknown schema error") + "; " + plan.Classification + "; " + string.Join(",", plan.Operations.Select(operation => operation.Kind + ":" + operation.LogicalId)));
            return (plan, result.Value, (await manager.ReadHistoryAsync("sqlite", new BaseSchemaHistoryRequest { Limit = 10 })).Value);
        }
        async ValueTask<OperationResult<BaseSchemaApplyResult>> ApplyExistingAsync(BaseCollection<GeneratedProject> collection, BaseSchemaPlan plan)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder.ConfigureSchema(options => { options.ApplicationId = "schema-evolution"; options.PlanProtectionKey = key; options.ExternalMigrationAttestationKey = attestationKey; }).AddCollection(collection).UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            return await provider.GetRequiredService<IBaseSchemaManager>().ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact, AllowDestructive = true });
        }
        try
        {
            (BaseSchemaPlan initial, BaseSchemaApplyResult? first, _) = await RunAsync(Version("name", false), true);
            initial.Classification.Should().Be(BaseSchemaPlanClassification.SafeStructural); first!.Generation.Should().Be(1);

            (BaseSchemaPlan additive, BaseSchemaApplyResult? second, _) = await RunAsync(Version("name", true), true);
            additive.Operations.Should().ContainSingle(operation => operation.Kind == BaseSchemaOperationKind.AddField);
            additive.Classification.Should().Be(BaseSchemaPlanClassification.SafeStructural); second!.Generation.Should().Be(2);

            (BaseSchemaPlan rename, _, _) = await RunAsync(Version("displayName", true), false);
            rename.Operations.Should().BeEmpty();
            rename.Classification.Should().Be(BaseSchemaPlanClassification.NoChanges);

            var destructiveServices = new ServiceCollection().AddLogging();
            destructiveServices.AddHPDBase(builder => builder
                .ConfigureSchema(options => { options.ApplicationId = "schema-evolution"; options.PlanProtectionKey = key; })
                .AddCollection(Version("displayName", false))
                .UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; })));
            await using (ServiceProvider destructiveProvider = destructiveServices.BuildServiceProvider())
            {
                IBaseSchemaManager destructiveManager = destructiveProvider.GetRequiredService<IBaseSchemaManager>();
                BaseSchemaPlan destructive = (await destructiveManager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
                destructive.Classification.Should().Be(BaseSchemaPlanClassification.Destructive);
                destructive.Operations.Should().Contain(operation => operation.Kind == BaseSchemaOperationKind.RemoveField);
                OperationResult<BaseSchemaApplyResult> rejected = await destructiveManager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = destructive.ProtectedArtifact });
                rejected.Error!.Code.Should().Be(BaseSchemaErrorCodes.MigrationRequired);
                (await destructiveManager.VerifyAsync(new BaseSchemaVerifyRequest { StoreId = "sqlite" })).Value!.Generation.Should().Be(2);
            }

            (BaseSchemaPlan requiresData, _, _) = await RunAsync(Version("displayName", true, requiredNew: true), false);
            requiresData.Classification.Should().Be(BaseSchemaPlanClassification.DataMigrationRequired);
            requiresData.RequiresExternalDataMigration.Should().BeTrue();

            static string Native(string prefix, string id) => prefix + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).Substring(0, 32);
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE {Native("b_c_", "schema-evolution-projects")} ADD COLUMN {Native("f_", "schema-evolution.required")} TEXT NOT NULL DEFAULT '';";
                await command.ExecuteNonQueryAsync();
            }
            var unsigned = new BaseExternalMigrationAttestation
            {
                AttestationId = "migration-1", ApplicationId = "schema-evolution", StoreId = "sqlite",
                SourceChecksum = requiresData.BaselineChecksum!, TargetChecksum = requiresData.TargetChecksum,
                CompletedAt = DateTimeOffset.UtcNow, Tool = "test-migrator", ToolVersion = "1", SignerId = "test-signer", AuthenticationTag = [],
            };
            BaseExternalMigrationAttestation signed = unsigned with { AuthenticationTag = BaseExternalMigrationAttestationAuthenticator.ComputeAuthenticationTag(unsigned, attestationKey) };
            (BaseSchemaPlan adoption, BaseSchemaApplyResult? adopted, BaseSchemaHistoryPage? history) = await RunAsync(Version("displayName", true, requiredNew: true), true, signed);
            adoption.Operations.Should().ContainSingle().Which.Kind.Should().Be(BaseSchemaOperationKind.AdoptExternalBaseline);
            adopted!.Generation.Should().Be(3);
            BaseSchemaHistoryEntry adoptedHistory = history!.Items.Single(item => item.Generation == 3);
            adoptedHistory.Outcome.Should().Be(BaseSchemaApplyOutcome.Applied);
            adoptedHistory.StructuralVerification.Should().Be(BaseSchemaStructuralVerification.Verified);
            adoptedHistory.ExternalDataMigration.Should().Be(BaseExternalDataMigrationVerification.HostAttested);
            adoptedHistory.SemanticConversion.Should().Be(BaseSemanticConversionVerification.NotVerifiedByBase);
            adoptedHistory.ExternalAttestationId.Should().Be("migration-1");
            adoptedHistory.ExternalSignerId.Should().Be("test-signer");

            await using (var seed = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await seed.OpenAsync();
                await using var command = seed.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO {Native("b_c_", "schema-evolution-projects")}
                      (record_id,revision,created_at,updated_at,append_position,{Native("f_", "schema-evolution.name")},{Native("p_", "schema-evolution.extra")},{Native("f_", "schema-evolution.extra")},{Native("f_", "schema-evolution.required")})
                    VALUES ('preserved',7,'2026-08-02T00:00:00.0000000+00:00','2026-08-02T00:00:00.0000000+00:00',1,'kept',1,'removed','required');
                    INSERT INTO hpd_base_mutation_journal
                      (event_id,event_type,schema_version,occurred_at,tenant_id,operation,visibility,collection_id,record_id,before_json,after_json)
                    VALUES ('schema-preservation-event','mutation','1','2026-08-02T00:00:00.0000000+00:00',NULL,0,0,'schema-evolution-projects','preserved',NULL,NULL);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            (BaseSchemaPlan populatedRemoval, _, _) = await RunAsync(Version("displayName", false, requiredNew: true), false);
            populatedRemoval.Classification.Should().Be(BaseSchemaPlanClassification.DataMigrationRequired);
            populatedRemoval.RequiresExternalDataMigration.Should().BeTrue();
            await using (var clearRemovedField = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await clearRemovedField.OpenAsync();
                await using var command = clearRemovedField.CreateCommand();
                command.CommandText = $"UPDATE {Native("b_c_", "schema-evolution-projects")} SET {Native("p_", "schema-evolution.extra")}=0, {Native("f_", "schema-evolution.extra")}=NULL WHERE record_id='preserved';";
                await command.ExecuteNonQueryAsync();
            }
            (BaseSchemaPlan racedRemoval, _, _) = await RunAsync(Version("displayName", false, requiredNew: true), false);
            await using (var raceWrite = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await raceWrite.OpenAsync();
                await using var command = raceWrite.CreateCommand();
                command.CommandText = $"UPDATE {Native("b_c_", "schema-evolution-projects")} SET {Native("p_", "schema-evolution.extra")}=1, {Native("f_", "schema-evolution.extra")}='late' WHERE record_id='preserved';";
                await command.ExecuteNonQueryAsync();
            }
            OperationResult<BaseSchemaApplyResult> raced = await ApplyExistingAsync(Version("displayName", false, requiredNew: true), racedRemoval);
            raced.IsSuccess().Should().BeFalse();
            raced.Error!.Code.Should().Be(BaseSchemaErrorCodes.MigrationRolledBack);
            await using (var clearRace = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await clearRace.OpenAsync();
                await using var command = clearRace.CreateCommand();
                command.CommandText = $"UPDATE {Native("b_c_", "schema-evolution-projects")} SET {Native("p_", "schema-evolution.extra")}=0, {Native("f_", "schema-evolution.extra")}=NULL WHERE record_id='preserved';";
                await command.ExecuteNonQueryAsync();
            }
            (BaseSchemaPlan removal, BaseSchemaApplyResult? removed, _) = await RunAsync(Version("displayName", false, requiredNew: true), true);
            removal.Classification.Should().Be(BaseSchemaPlanClassification.Destructive);
            removed!.Generation.Should().Be(4);
            await using (var verifyPreserved = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await verifyPreserved.OpenAsync();
                await using var command = verifyPreserved.CreateCommand();
                command.CommandText = $"SELECT revision, {Native("f_", "schema-evolution.name")} FROM {Native("b_c_", "schema-evolution-projects")} WHERE record_id='preserved';";
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetInt64(0).Should().Be(7);
                reader.GetString(1).Should().Be("kept");
                await reader.DisposeAsync();
                command.CommandText = "SELECT COUNT(*) FROM hpd_base_mutation_journal WHERE event_id='schema-preservation-event';";
                Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(1);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task SqliteDestructiveRebuildPreservesRecordAndJournalFacts()
    {
        string database = Path.Combine(Path.GetTempPath(), "hpd-base-schema-relation-rebuild-" + Guid.NewGuid().ToString("N") + ".db");
        byte[] key = Enumerable.Repeat((byte)0x73, 32).ToArray();
        BaseCollection<GeneratedProject> target = HPD.Base.BaseCollection.Define(
            "schema-rebuild-targets", Metadata().GeneratedProject,
            schema => schema.String("schema-rebuild.target.name", "Name", ProjectProperty("name")));
        BaseCollection<GeneratedProject> Source(bool removable) => HPD.Base.BaseCollection.Define(
            "schema-rebuild-sources", Metadata().GeneratedProject,
            schema =>
            {
                schema.String("schema-rebuild.source.name", "Name", ProjectProperty("name"));
                if (removable) schema.String("schema-rebuild.source.removable", "OptionalNote", ProjectProperty("optionalNote"));
                schema.String("schema-rebuild.source.members", "OrganizationId", ProjectProperty("organizationId"));
            });
        async ValueTask ApplyAsync(BaseCollection<GeneratedProject> source)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => { options.ApplicationId = "schema-relation-rebuild"; options.PlanProtectionKey = key; })
                .AddCollection(source).AddCollection(target)
                .UseStore(SqliteStore.Configure(options => { options.DataSource = database; options.StoreId = "sqlite"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            OperationResult<BaseSchemaApplyResult> result = await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact, AllowDestructive = true });
            result.IsSuccess().Should().BeTrue(result.Error?.Code);
        }
        static string Native(string prefix, string id) => prefix + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).Substring(0, 32);
        try
        {
            await ApplyAsync(Source(removable: true));
            await using (var seed = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + database))
            {
                await seed.OpenAsync();
                await using var command = seed.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO {Native("b_c_", target.Id)} (record_id,revision,created_at,updated_at,append_position,{Native("p_", "schema-rebuild.target.name")},{Native("f_", "schema-rebuild.target.name")})
                    VALUES ('target',3,'2026-08-02T00:00:00.0000000+00:00','2026-08-02T00:00:00.0000000+00:00',1,1,'target');
                    INSERT INTO {Native("b_c_", "schema-rebuild-sources")}
                      (record_id,revision,created_at,updated_at,append_position,{Native("p_", "schema-rebuild.source.name")},{Native("f_", "schema-rebuild.source.name")},{Native("p_", "schema-rebuild.source.removable")},{Native("f_", "schema-rebuild.source.removable")},{Native("f_", "schema-rebuild.source.members")})
                    VALUES ('source',9,'2026-08-02T00:00:00.0000000+00:00','2026-08-02T00:00:00.0000000+00:00',1,1,'source',0,NULL,'["target"]');
                    INSERT INTO hpd_base_mutation_journal
                      (event_id,event_type,schema_version,occurred_at,tenant_id,operation,visibility,collection_id,record_id,before_json,after_json)
                    VALUES ('schema-rebuild-event','mutation','1','2026-08-02T00:00:00.0000000+00:00',NULL,0,0,'schema-rebuild-sources','source',NULL,NULL);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await ApplyAsync(Source(removable: false));

            await using var verify = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + database);
            await verify.OpenAsync();
            await using var check = verify.CreateCommand();
            check.CommandText = $"""
                SELECT
                  (SELECT revision FROM {Native("b_c_", "schema-rebuild-sources")} WHERE record_id='source'),
                  (SELECT COUNT(*) FROM hpd_base_mutation_journal WHERE event_id='schema-rebuild-event');
                """;
            await using Microsoft.Data.Sqlite.SqliteDataReader reader = await check.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt64(0).Should().Be(9);
            reader.GetInt64(1).Should().Be(1);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { database, database + "-wal", database + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public void LogicalSchemaChecksumIsStableAcrossRegistrationOrderAndSensitiveToStoredShape()
    {
        BaseCollection<GeneratedProject> first = HPD.Base.BaseCollection.Define(
            "logical.first", Metadata().GeneratedProject,
            schema => schema.String("logical.first.name", "Name", ProjectProperty("name")));
        BaseCollection<GeneratedProject> second = HPD.Base.BaseCollection.Define(
            "logical.second", Metadata().GeneratedProject,
            schema => schema.String("logical.second.name", "Name", ProjectProperty("name")));

        static BaseLogicalSchema Build(params BaseCollection<GeneratedProject>[] collections)
        {
            var services = new ServiceCollection();
            services.AddHPDBase(builder =>
            {
                builder.ConfigureSchema(options => options.ApplicationId = "checksum-test");
                foreach (BaseCollection<GeneratedProject> collection in collections) builder.AddCollection(collection);
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<BaseLogicalSchema>();
        }

        BaseLogicalSchema ordered = Build(first, second);
        BaseLogicalSchema reversed = Build(second, first);
        BaseCollection<GeneratedProject> renamed = HPD.Base.BaseCollection.Define(
            "logical.first", Metadata().GeneratedProject,
            schema => schema.String("logical.first.name", "RenamedName", ProjectProperty("name")));

        reversed.CanonicalChecksum.Should().Be(ordered.CanonicalChecksum);
        Build(renamed, second).CanonicalChecksum.Should().NotBe(ordered.CanonicalChecksum);
        ordered.CanonicalChecksum.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task SessionCreationIsSideEffectFreeAndOperationsFailClosedBeforeReadiness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));
        using var provider = services.BuildServiceProvider();

        IHPDBaseApplication application = provider.GetRequiredService<IHPDBaseApplication>();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = "subject_1",
        });
        BaseResult<BaseRecord<GeneratedProject>> result = await session
            .Collection(GeneratedProject.Collection).GetAsync(new RecordId("record_1"));

        application.CurrentReadiness.State.Should().Be(BaseApplicationReadinessState.NotStarted);
        result.Should().BeOfType<BaseFailure<BaseRecord<GeneratedProject>>>()
            .Which.Error.Code.Should().Be("base.application.notReady");
        provider.GetRequiredService<IRecordStoreRegistry>().GetRegistrations().Should().BeEmpty();
    }

    [Fact]
    public async Task AdministrationIsHostOnlyReadinessBoundAndCapabilityHonest()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));
        using ServiceProvider provider = services.BuildServiceProvider();

        IHPDBaseApplication application = provider.GetRequiredService<IHPDBaseApplication>();
        Action beforeReady = () => _ = application.Administration;
        beforeReady.Should().Throw<InvalidOperationException>();

        (await application.InitializeAsync()).Status.Should().Be(OperationStatus.Ok);
        BaseAdministrationCapability capability = application.Administration.Capability;
        capability.Backup.Should().BeFalse();
        capability.Validate.Should().BeFalse();
        capability.Restore.Should().BeFalse();
        capability.AdministrativePurge.Should().BeTrue();
        typeof(BaseSession).GetProperty("Administration").Should().BeNull();
    }

    [Fact]
    public async Task SqliteAdministrationCreatesValidatesAndRestoresAuthenticatedBackup()
    {
        string temporaryDirectory = Path.GetFullPath(Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && temporaryDirectory.StartsWith("/var/", StringComparison.Ordinal))
            temporaryDirectory = "/private" + temporaryDirectory;
        string path = Path.Combine(temporaryDirectory, "hpd-base-administration-" + Guid.NewGuid().ToString("N") + ".db");
        byte[] tokenKey = Enumerable.Repeat((byte)0x71, 32).ToArray();
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => { options.ApplicationId = "administration-test"; options.PlanProtectionKey = Enumerable.Repeat((byte)0x72, 32).ToArray(); })
                .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 7, Key = tokenKey, IssueNotBefore = DateTimeOffset.UnixEpoch })
                .AddPolicyAuthority<AdministrationAllowPolicyEvaluator>(new BasePolicyAuthorityDefinition
                {
                    Id = "hpd.base.hosting.admin-allow", Version = 1, OwningModuleId = "hpd.base.tests",
                    EvaluatorContractId = "hpd.base.hosting.admin-policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
                })
                .AddCollection(GeneratedProject.Collection)
                .UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; options.AdministrationEnabled = true; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            IHPDBaseApplication application = provider.GetRequiredService<IHPDBaseApplication>();
            (await application.InitializeAsync()).IsSuccess().Should().BeTrue();
            var administrator = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System };
            BaseCollectionSession<GeneratedProject> collection = provider.GetRequiredService<IBaseSessionFactory>().For(administrator).Collection(GeneratedProject.Collection);
            BaseRecord<GeneratedProject> created = (await collection.CreateAsync(new RecordId("project-1"), new GeneratedProject { OrganizationId = "org", Name = "before" })).RequireValue();
            _ = (await collection.CreateAsync(new RecordId("project-2"), new GeneratedProject { OrganizationId = "org", Name = "second" })).RequireValue();

            var rawStore = (SqliteRecordStore)provider.GetRequiredService<IRecordStoreRegistry>().GetStore("sqlite")!;
            var firstPageQuery = new RecordQuery
            {
                Sort = [new QuerySort { Field = "name", Direction = QuerySortDirection.Asc }],
                Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 1 },
            };
            OperationContext queryContext = new() { Operation = BaseOperationKind.Query, CollectionId = "projects", Now = DateTimeOffset.UtcNow };
            string preRestoreCursor = (await rawStore.ListAsync(GeneratedProject.Collection.Definition, firstPageQuery, queryContext)).Value!.Page.NextCursor!;

            var artifact = new MemoryStream();
            BaseBackupManifest manifest = (await application.Administration.CreateBackupAsync(artifact, new BaseBackupRequest { StoreId = "sqlite", Principal = administrator })).RequireValue();
            artifact.Position = 0;
            (await application.Administration.ValidateBackupAsync(artifact, new BaseBackupValidationRequest { StoreId = "sqlite", Principal = administrator, ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest })).RequireValue();
            byte[] tampered = artifact.ToArray();
            tampered[tampered.Length / 2] ^= 0x40;
            BaseFailure<BaseBackupManifest> invalid = (BaseFailure<BaseBackupManifest>)await application.Administration.ValidateBackupAsync(
                new MemoryStream(tampered), new BaseBackupValidationRequest { StoreId = "sqlite", Principal = administrator });
            invalid.Error.Code.Should().Be(BaseAdministrationErrorCodes.ArtifactInvalid);
            (await collection.ReplaceAsync(created.Id, new GeneratedProject { OrganizationId = "org", Name = "after" })).RequireValue();

            artifact.Position = 0;
            BaseRestoreResult restored = (await application.Administration.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "sqlite", Principal = administrator,
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
                ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
            })).RequireValue();

            restored.RestoreEpoch.Should().Be(manifest.RestoreEpoch + 1);
            (await collection.GetAsync(created.Id)).RequireValue().Value.Name.Should().Be("before");
            OperationResult<RecordPage> invalidated = await rawStore.ListAsync(
                GeneratedProject.Collection.Definition,
                firstPageQuery with { Page = firstPageQuery.Page! with { Cursor = preRestoreCursor } },
                queryContext);
            invalidated.Error!.Code.Should().Be(BaseQueryErrorCodes.CursorRestoreInvalidated);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*")) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task UnifiedBuilderInstallsCollectionProviderAndManifest()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder

            .AddCollection(GeneratedProject.Collection));

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        IBaseSessionFactory sessions = provider.GetRequiredService<IBaseSessionFactory>();
        _ = sessions.For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "system",
        });
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync())
            .Status.Should().Be(OperationStatus.Ok);

        HPDBaseInstalledFeatures manifest =
            provider.GetRequiredService<HPDBaseInstalledFeatures>();
        manifest.Provider.Should().Be("inmemory");
        manifest.CollectionIds.Should().Equal("projects");
        provider.GetRequiredService<IRecordStoreRegistry>()
            .GetStoreForCollection("projects")
            .Should().NotBeNull();
    }

    [Fact]
    public void UnifiedBuilderDefaultsWhenMissingAndRejectsMultipleExplicitProviders()
    {
        var defaultServices = new ServiceCollection();
        defaultServices.AddHPDBase(
            builder => builder.AddCollection(GeneratedProject.Collection));
        Action duplicate = () => new ServiceCollection().AddHPDBase(
            builder => builder
                .UseStore(InMemoryProviderInstaller.Create(null))
                .UseStore(InMemoryProviderInstaller.Create(null)));

        using var provider = defaultServices.BuildServiceProvider();
        provider.GetRequiredService<HPDBaseInstalledFeatures>().Provider
            .Should().Be("inmemory");
        duplicate.Should().Throw<InvalidOperationException>()
            .WithMessage("base.store.selection.duplicate");
    }

    [Fact]
    public void InMemoryProviderIsPerHostSingletonAndExplicitProvidersSuppressIt()
    {
        static ServiceProvider InMemoryHost()
        {
            var services = new ServiceCollection();
            services.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));
            return services.BuildServiceProvider();
        }

        using var firstHost = InMemoryHost();
        using var secondHost = InMemoryHost();
        var first = firstHost.GetRequiredService<InMemoryRecordStore>();

        firstHost.GetRequiredService<InMemoryRecordStore>().Should().BeSameAs(first);
        secondHost.GetRequiredService<InMemoryRecordStore>().Should().NotBeSameAs(first);

        var explicitServices = new ServiceCollection();
        explicitServices.AddHPDBase(builder => builder
            .UseStore(SqliteStore.Configure())
            .AddCollection(GeneratedProject.Collection));
        using var explicitHost = explicitServices.BuildServiceProvider();
        explicitHost.GetService<InMemoryRecordStore>().Should().BeNull();
        explicitHost.GetRequiredService<HPDBaseInstalledFeatures>().Provider.Should().Be("sqlite");
    }

    [Fact]
    public async Task ConfiguredActivationCeilingsProduceOneExactSelectedAndInstalledProviderAuthority()
    {
        int inMemoryConfigurationCalls = 0;
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddHPDBase(builder => builder
            .ConfigureInMemoryStore(options =>
            {
                inMemoryConfigurationCalls++;
                options.MaxPendingActivationRows = 7;
                options.MaxClaimedActivationRows = 8;
                options.MaxTerminalActivationRows = 9;
            })
            .AddCollection(GeneratedProject.Collection));
        await using ServiceProvider inMemory = inMemoryServices.BuildServiceProvider();
        BaseActivationProviderCapability selectedInMemory = inMemory.GetRequiredService<HPDBaseInstalledFeatures>()
            .StoreProvider.Activations;
        BaseActivationProviderCapability installedInMemory = ((IBaseActivationProvider)inMemory
            .GetRequiredService<InMemoryRecordStore>()).Descriptor.Capability;

        inMemoryConfigurationCalls.Should().Be(1);
        selectedInMemory.MaximumPendingRows.Should().Be(7);
        selectedInMemory.MaximumClaimedRows.Should().Be(8);
        selectedInMemory.MaximumTerminalRows.Should().Be(9);
        BaseActivationCertificationReceiptContract.CapabilityChecksum(selectedInMemory)
            .Should().Equal(BaseActivationCertificationReceiptContract.CapabilityChecksum(installedInMemory));

        string path = Path.Combine(Path.GetTempPath(), "hpd-base-config-authority-" + Guid.NewGuid().ToString("N") + ".db");
        int sqliteConfigurationCalls = 0;
        try
        {
            HPDBaseStoreProvider sqliteProvider = SqliteStore.Configure(options =>
            {
                sqliteConfigurationCalls++;
                options.DataSource = path;
                options.MaxPendingActivationRows = 17;
                options.MaxClaimedActivationRows = 18;
                options.MaxTerminalActivationRows = 19;
                options.AdministrationEnabled = true;
            });
            var sqliteServices = new ServiceCollection().AddLogging();
            sqliteServices.AddHPDBase(builder => builder.UseStore(sqliteProvider).AddCollection(GeneratedProject.Collection));
            await using ServiceProvider sqlite = sqliteServices.BuildServiceProvider();
            BaseActivationProviderCapability selectedSqlite = sqlite.GetRequiredService<HPDBaseInstalledFeatures>()
                .StoreProvider.Activations;
            BaseActivationProviderCapability installedSqlite = ((IBaseActivationProvider)sqlite
                .GetRequiredService<SqliteRecordStore>()).Descriptor.Capability;

            sqliteConfigurationCalls.Should().Be(1);
            selectedSqlite.MaximumPendingRows.Should().Be(17);
            selectedSqlite.MaximumClaimedRows.Should().Be(18);
            selectedSqlite.MaximumTerminalRows.Should().Be(19);
            selectedSqlite.BackupModes.Should().ContainSingle().Which.Should().Be(BaseActivationBackupMode.WholeStoreAtomic);
            installedSqlite.Should().BeEquivalentTo(selectedSqlite);
            BaseActivationCertificationReceiptContract.CapabilityChecksum(selectedSqlite)
                .Should().Equal(BaseActivationCertificationReceiptContract.CapabilityChecksum(installedSqlite));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public void InMemoryConfigurationCannotBeSilentlyIgnoredByExplicitProvider()
    {
        Action register = () => new ServiceCollection().AddHPDBase(builder => builder
            .ConfigureInMemoryStore(options => options.MaxPageSize = 50)
            .UseStore(SqliteStore.Configure())
            .AddCollection(GeneratedProject.Collection));

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConfigureInMemoryStore*explicit*");
    }

    [Fact]
    public void PreRegisteredStoreOptionsCannotCompeteWithTheSelectedAuthoritySnapshot()
    {
        var inMemory = new ServiceCollection();
        inMemory.AddSingleton(new HPDBaseInMemoryStoreOptions());
        Action registerInMemory = () => inMemory.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));

        var sqlite = new ServiceCollection();
        sqlite.AddSingleton(new HPDBaseSqliteOptions());
        Action registerSqlite = () => sqlite.AddHPDBase(builder => builder
            .UseStore(SqliteStore.Configure())
            .AddCollection(GeneratedProject.Collection));

        registerInMemory.Should().Throw<InvalidOperationException>().WithMessage("base.store.authorityAmbiguous");
        registerSqlite.Should().Throw<InvalidOperationException>().WithMessage("base.store.authorityAmbiguous");
    }

    [Fact]
    public async Task ExtraAuthoritativeServiceRegistrationFailsReadiness()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));
        services.AddSingleton<IRecordStore>(provider => provider.GetRequiredService<InMemoryRecordStore>());
        await using ServiceProvider provider = services.BuildServiceProvider();

        OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();

        readiness.Status.Should().Be(OperationStatus.StoreError);
        provider.GetServices<IRecordStore>().Should().HaveCount(2);
    }

    [Fact]
    public void InMemoryFileDefaultIsIndependentFromTheRecordProvider()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => builder
            .UseStore(SqliteStore.Configure())
            .AddCollection(GeneratedProject.Collection)
            .AddFiles(options => options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("assets")
            })));

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IFileStorageProvider>()
            .Should().ContainSingle()
            .Which.ProviderRef.Should().Be(new FileProviderRef("inmemory"));
        provider.GetRequiredService<IOptions<HPDBaseFilesOptions>>().Value.Buckets
            .Should().ContainSingle()
            .Which.ProviderRef.Should().Be(new FileProviderRef("inmemory"));
    }

    [Fact]
    public void RequiredPhysicalIndexesInstallOnlyOnCapableProviders()
    {
        var required = HPD.Base.BaseCollection.Define(
            "required.projects",
            Metadata().GeneratedProject,
            schema =>
            {
                schema.String("organization-id", "OrganizationId", ProjectProperty("organizationId")).Required();
                schema.Index("organization", "organization-id").Required();
            });

        Action register = () => new ServiceCollection().AddHPDBase(
            builder => builder.UseStore(SqliteStore.Configure()).AddCollection(required));
        register.Should().NotThrow();

        Action defaultRegister = () => new ServiceCollection().AddHPDBase(
            builder => builder.AddCollection(required));
        defaultRegister.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be installed*InMemory*");
    }

    [Fact]
    public void OptionalModulesNeedNoEmptyConfigurationCallbacks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder

            .AddCollection(GeneratedProject.Collection)
            .AddFiles()
            .AddDependencies(options =>
                options.ProtectionKey = Enumerable.Repeat((byte)0x31, 32).ToArray())
            .AddRealtime());

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true });
        HPDBaseInstalledFeatures manifest =
            provider.GetRequiredService<HPDBaseInstalledFeatures>();

        manifest.Files.Should().BeTrue();
        manifest.Dependencies.Should().BeTrue();
        manifest.Realtime.Should().BeTrue();
        typeof(HPDBaseBuilder)
            .GetMethod(nameof(HPDBaseBuilder.AddDependencies))!
            .GetParameters()[0]
            .IsOptional.Should().BeTrue();
    }

    [Fact]
    public void UnifiedBuilderRejectsDuplicateModuleInstallation()
    {
        Action duplicateRealtime = () => new ServiceCollection().AddHPDBase(
            builder => builder

                .AddRealtime()
                .AddRealtime());
        Action duplicateLiveQuery = () => new ServiceCollection().AddHPDBase(
            builder => builder

                .AddLiveQueries()
                .AddLiveQueries());

        duplicateRealtime.Should().Throw<InvalidOperationException>()
            .WithMessage("*Realtime is already registered*");
        duplicateLiveQuery.Should().Throw<InvalidOperationException>()
            .WithMessage("*Live queries are already registered*");
    }

    [Fact]
    public async Task InitializationIsCoalescedAndCallerCancellationOnlyStopsThatWait()
    {
        var extension = new BlockingProviderExtension();
        var services = new ServiceCollection();
        services.AddHPDBase(builder => builder.Use(extension));
        await using ServiceProvider provider = services.BuildServiceProvider();
        IHPDBaseApplication application = provider.GetRequiredService<IHPDBaseApplication>();
        using var cancelledCaller = new CancellationTokenSource();

        Task<OperationResult<BaseApplicationReadiness>> cancelled = application.InitializeAsync(cancelledCaller.Token).AsTask();
        await extension.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task<OperationResult<BaseApplicationReadiness>> surviving = application.InitializeAsync().AsTask();
        cancelledCaller.Cancel();
        await FluentActions.Awaiting(() => cancelled).Should().ThrowAsync<OperationCanceledException>();
        extension.Release.TrySetResult();

        (await surviving.WaitAsync(TimeSpan.FromSeconds(1))).Value!.State.Should().Be(BaseApplicationReadinessState.Ready);
        extension.InitializationCount.Should().Be(1);
        (await application.InitializeAsync()).Value!.State.Should().Be(BaseApplicationReadinessState.Ready);
        extension.InitializationCount.Should().Be(1);
    }

    [Fact]
    public async Task HostStoppingOwnsAndCancelsSharedInitialization()
    {
        var extension = new BlockingProviderExtension();
        var lifetime = new TestBaseApplicationLifetime();
        var services = new ServiceCollection();
        services.AddSingleton<IBaseApplicationLifetime>(lifetime);
        services.AddHPDBase(builder => builder.Use(extension));
        await using ServiceProvider provider = services.BuildServiceProvider();

        Task<OperationResult<BaseApplicationReadiness>> initialization = provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync().AsTask();
        await extension.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        lifetime.Stop.Cancel();

        OperationResult<BaseApplicationReadiness> result = await initialization.WaitAsync(TimeSpan.FromSeconds(1));
        result.Error!.Code.Should().Be("base.application.initializationTimeout");
        provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness.State.Should().Be(BaseApplicationReadinessState.Failed);
    }
}

file sealed class TestProviderExtension(string id) : IHPDBaseBuilderExtension
{
    public string Id { get; } = id;
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) { }
}

file sealed class BlockingProviderExtension : IHPDBaseBuilderExtension
{
    private int _initializationCount;
    public string Id => "blocking";
    public int InitializationCount => System.Threading.Volatile.Read(ref _initializationCount);
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) { }
    public async ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _initializationCount);
        Started.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
    }
}

file sealed class TestBaseApplicationLifetime : IBaseApplicationLifetime
{
    public CancellationTokenSource Stop { get; } = new();
    public CancellationToken Stopping => Stop.Token;
}

file sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

file sealed class AdministrationAllowPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(PolicyDecision.Allow());
    }
}
