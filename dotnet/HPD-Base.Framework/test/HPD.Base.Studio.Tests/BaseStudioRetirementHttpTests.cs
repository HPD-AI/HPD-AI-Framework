using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using HPD.Base.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Studio.Tests;

public sealed class BaseStudioRetirementHttpTests
{
    [Fact]
    public async Task Sqlite_tombstone_publishes_the_exact_pending_retirement_barrier()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-studio-retirement-{Guid.NewGuid():N}.db");
        try
        {
            await using WebApplication app = Build(BaseStudioMode.Operate, database);
            IBaseSchemaManager schemas = app.Services.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "retirement-http" });
            Assert.True(planned.IsSuccess(), planned.Error?.Code);
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
                new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact });
            Assert.True(applied.IsSuccess(), applied.Error?.Code);
            OperationResult<BaseApplicationReadiness> readiness = await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
            Assert.True(readiness.IsSuccess(), $"{readiness.Error?.Code}: {readiness.Error?.Message} {readiness.Error?.Detail}");

            BarrierFixture fixture = await CreateBarrierAsync(app);
            Assert.Equal(BaseSubjectRetirementBarrierState.Pending, fixture.Barrier.State);
            IBaseSubjectRetirementStore store = Assert.IsAssignableFrom<IBaseSubjectRetirementStore>(
                app.Services.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(RetirementPrivateRecord.Collection.Id));
            OperationResult<BaseSubjectRetirementPublicationPage> publications = await store.ReadPublicationsAsync(new() { Take = 32 });
            Assert.Contains(publications.Value!.Rows,
                row => row.Fact.Kind == BaseSubjectRetirementPublicationKind.BarrierCreated
                    && row.Fact.Barrier?.SubjectId.Equals(fixture.Fact.SubjectId) == true);

            SqliteRecordStore sqlite = Assert.IsType<SqliteRecordStore>(store);
            await using var artifact = new MemoryStream();
            OperationResult<BaseBackupManifest> backup = await sqlite.CreateBackupAsync(artifact,
                new BaseBackupRequest { StoreId = "retirement-http", Principal = FixturePrincipal() });
            Assert.True(backup.IsSuccess(), backup.Error?.Code);
            OperationResult<BaseRestoreResult> restored = await sqlite.RestoreAsync(new MemoryStream(artifact.ToArray()), new()
            {
                StoreId = "retirement-http",
                Principal = FixturePrincipal(),
                ExpectedCurrentStoreIdentityDigest = backup.Value!.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = backup.Value.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
                ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
            });
            Assert.True(restored.IsSuccess(), restored.Error?.Code);
            OperationResult<BaseSubjectRetirementBarrierPage> restoredBarriers = await store.ReadBarriersAsync(new()
            {
                ApplicationId = ApplicationId,
                ContractId = ContractId,
                ContractVersion = 1,
                ScopeAuthority = new() { Mode = BaseSubjectScopeQueryMode.ExactScope, ExactScope = new() { Kind = BaseSubjectScopeKind.Global }, InstalledAuthorityDigest = RetirementHttpSubject.HPDBaseSubjectRegistration.Checksum },
                Take = 4,
                MaximumResultBytes = 1_048_576,
                DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            });
            BaseSubjectRetirementBarrier restoredBarrier = Assert.Single(restoredBarriers.Value!.Barriers).Barrier;
            Assert.Equal(BaseSubjectRetirementBarrierState.Pending, restoredBarrier.State);
            Assert.Equal(fixture.Fact.SubjectId, restoredBarrier.SubjectId);
            Assert.NotEqual(fixture.Barrier.AuthorityEpoch, restoredBarrier.AuthorityEpoch);
            OperationResult<BaseSubjectRetirementPublicationPage> restoredPublications = await store.ReadPublicationsAsync(new() { Take = 64 });
            Assert.Contains(restoredPublications.Value!.Rows,
                row => row.Fact.Kind == BaseSubjectRetirementPublicationKind.RestoreTransformed
                    && row.Fact.Restore?.ContractId == ContractId);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(database + suffix)) File.Delete(database + suffix);
        }
    }

    [Fact]
    public async Task Ordinary_administrative_purge_cannot_bypass_subject_retirement()
    {
        await using WebApplication app = Build(BaseStudioMode.Operate);
        Assert.True((await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        await CreateBarrierAsync(app);

        BaseResult<BasePurgeResult> result = await app.Services.GetRequiredService<IHPDBaseAdministration>().PurgeAsync(new()
        {
            CollectionId = RetirementPrivateRecord.Collection.Id,
            RecordIds = [RecordId.Create("subject-1")],
            Principal = FixturePrincipal(),
            ReasonCode = "retirement-bypass",
            AuditReference = "retirement-http-regression",
            EvaluatedAt = app.Services.GetRequiredService<TimeProvider>().GetUtcNow(),
        });

        Assert.False(result.TryGetValue(out _));
        BaseFailure<BasePurgeResult> failure = Assert.IsType<BaseFailure<BasePurgeResult>>(result);
        Assert.NotEqual(OperationStatus.Updated, failure.Status);
    }

    [Fact]
    public async Task Consumer_remove_resolves_indeterminate_execute_from_receipt_without_reentry()
    {
        await using WebApplication app = Build(BaseStudioMode.Operate);
        Assert.True((await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        await CreateBarrierAsync(app);
        BaseSubjectRetirementConsumerDefinition consumer = RetirementConsumer();
        BaseSubjectRetirementRegistry retirementRegistry = app.Services.GetRequiredService<BaseSubjectRetirementRegistry>();
        BaseInstalledSubjectRetirementConsumer installedConsumer = retirementRegistry.FindConsumer(consumer.ConsumerId, consumer.ConsumerVersion)!;
        BaseInstalledSubjectRetirementPolicy installedPolicy = retirementRegistry.FindPolicy(ContractId, 1)!;
        IBaseSubjectLifecycleStore lifecycleStore = Assert.IsAssignableFrom<IBaseSubjectLifecycleStore>(
            app.Services.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(RetirementPrivateRecord.Collection.Id));
        BaseSubjectLifecycleInspectionAuthority authority = app.Services
            .GetRequiredService<BaseSubjectLifecycleInspectionAuthorityRegistry>().Find(ContractId, 1)!;
        OperationResult<BaseSubjectLifecycleProviderInspection> inspected = await lifecycleStore.InspectAsync(new()
        {
            ContractId = ContractId, ContractVersion = 1, ConsumerId = consumer.ConsumerId,
            ScopeAuthority = new() { Mode = BaseSubjectScopeQueryMode.AllAuthorizedScopes, InstalledAuthorityDigest = authority.Digest },
            IncludeTerminalReceipt = false, MaximumResultBytes = 1_048_576,
            DeadlineUtc = app.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddMinutes(1),
        });
        BaseSubjectLifecycleConsumerInspection projection = Assert.Single(inspected.Value!.Consumers);
        var target = new BaseStudioLifecycleConsumerResource(ApplicationId, consumer.ConsumerId, consumer.ConsumerVersion,
            ContractId, 1);
        JsonObject input = BaseInput(target);
        input["consumerId"] = consumer.ConsumerId;
        input["consumerVersion"] = consumer.ConsumerVersion;
        input["expectedConsumerChecksum"] = installedConsumer.Checksum;
        input["expectedAcceptedSetChecksum"] = BaseSubjectRetirementRegistry.AcceptedSetChecksum(installedPolicy.Definition.AcceptedConsumers);
        input["expectedGraphGeneration"] = projection.PublishedGraphGeneration.ToString();

        (Response executed, Response replay) = await ExecuteMappedAsync(app, "retirement.consumer.remove", target, input,
            "consumer-remove-request-1");

        Assert.Equal(StatusCodes.Status409Conflict, executed.Status);
        Assert.Equal(StatusCodes.Status200OK, replay.Status);
        using JsonDocument receipt = JsonDocument.Parse(replay.Body);
        Assert.Equal("execute", receipt.RootElement.GetProperty("mode").GetString());
        Assert.Equal("2", receipt.RootElement.GetProperty("resultingGeneration").GetString());
        IBaseSubjectRetirementStore retirementStore = Assert.IsAssignableFrom<IBaseSubjectRetirementStore>(
            app.Services.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(RetirementPrivateRecord.Collection.Id));
        OperationResult<BaseSubjectRetirementPublicationPage> publications = await retirementStore.ReadPublicationsAsync(new() { Take = 32 });
        Assert.Single(publications.Value!.Rows.Where(static row => row.Fact.Kind == BaseSubjectRetirementPublicationKind.ConsumerSetChanged));
    }

    [Fact]
    public async Task Purge_uses_real_overridden_barrier_and_replays_exact_result()
    {
        await using WebApplication app = Build(BaseStudioMode.Operate);
        Assert.True((await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BarrierFixture fixture = await CreateBarrierAsync(app);
        app.Services.GetRequiredService<MutableClock>().Advance(TimeSpan.FromMinutes(2));
        BaseSession session = ControlSession(app);
        BaseSubjectRetirementTimeoutResult timeout = (await session.SubjectRetirements.ProcessTimeoutAsync(new()
        {
            ContractId = ContractId, ContractVersion = 1, SubjectId = fixture.Fact.SubjectId, AuthorityEpoch = fixture.Fact.AuthorityEpoch,
            Incarnation = fixture.Fact.Incarnation, ExpectedBarrierGeneration = fixture.Barrier.Generation,
            ExpectedBarrierChecksum = fixture.Barrier.BarrierChecksum, Identity = Identity("prepare-purge-timeout")
        })).RequireValue();
        BaseSubjectRetirementOverrideResult overridden = (await session.SubjectRetirements.OverrideAsync(new()
        {
            ContractId = ContractId, ContractVersion = 1, SubjectId = fixture.Fact.SubjectId, AuthorityEpoch = fixture.Fact.AuthorityEpoch,
            Incarnation = fixture.Fact.Incarnation, ExpectedTombstoneSequence = fixture.Barrier.TombstoneSequence,
            ExpectedBarrierGeneration = timeout.Generation, ExpectedBarrierChecksum = timeout.BarrierChecksum,
            Intent = "override-subject-retirement-barrier", ChangeReference = "purge-change", Identity = Identity("prepare-purge-override")
        })).RequireValue();
        BaseStudioRetirementBarrierResource target = BarrierResource(fixture);
        JsonObject input = BaseInput(target);
        input["expectedBarrierChecksum"] = overridden.BarrierChecksum;
        input["expectedBarrierGeneration"] = overridden.Generation.ToString();
        input["expectedPrivateRevision"] = fixture.PrivateRevision.Value;
        input["expectedTombstoneSequence"] = fixture.Barrier.TombstoneSequence.ToString();
        (Response executed, Response replay) = await ExecuteMappedAsync(app, "retirement.purge", target, input, "purge-request-1");
        if (executed.Status != StatusCodes.Status200OK)
        {
            BaseResult<BaseSubjectFinalPurgeResult> diagnostic = await session.SubjectRetirements.PurgeAsync(new()
            {
                ContractId = ContractId, ContractVersion = 1, SubjectId = fixture.Fact.SubjectId,
                AuthorityEpoch = fixture.Fact.AuthorityEpoch, Incarnation = fixture.Fact.Incarnation,
                ExpectedTombstoneSequence = fixture.Barrier.TombstoneSequence, ExpectedPrivateRevision = fixture.PrivateRevision,
                ExpectedBarrierGeneration = overridden.Generation, ExpectedBarrierChecksum = overridden.BarrierChecksum,
                Identity = Identity("diagnose-purge")
            });
            Assert.True(diagnostic.TryGetValue(out _), diagnostic is BaseFailure<BaseSubjectFinalPurgeResult> failure ? failure.Error.Code : "missing");
        }
        Assert.True(executed.Status == StatusCodes.Status200OK, $"purge execute: {executed.Status} {executed.Body}");
        Assert.Equal(executed.Body, replay.Body);
    }

    [Fact]
    public async Task Override_uses_real_timed_out_barrier_and_replays_exact_result()
    {
        await using WebApplication app = Build(BaseStudioMode.Operate);
        Assert.True((await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BarrierFixture fixture = await CreateBarrierAsync(app);
        app.Services.GetRequiredService<MutableClock>().Advance(TimeSpan.FromMinutes(2));
        BaseSession session = ControlSession(app);
        BaseResult<BaseSubjectRetirementTimeoutResult> timedOut = await session.SubjectRetirements.ProcessTimeoutAsync(new()
        {
            ContractId = ContractId,
            ContractVersion = 1,
            SubjectId = fixture.Fact.SubjectId,
            AuthorityEpoch = fixture.Fact.AuthorityEpoch,
            Incarnation = fixture.Fact.Incarnation,
            ExpectedBarrierGeneration = fixture.Barrier.Generation,
            ExpectedBarrierChecksum = fixture.Barrier.BarrierChecksum,
            Identity = Identity("prepare-override"),
        });
        BaseSubjectRetirementTimeoutResult timeout = timedOut.RequireValue();
        BaseStudioRetirementBarrierResource target = BarrierResource(fixture);
        JsonObject input = BaseInput(target);
        input["changeReference"] = "change-42";
        input["expectedBarrierChecksum"] = timeout.BarrierChecksum;
        input["expectedBarrierGeneration"] = timeout.Generation.ToString();
        input["expectedTombstoneSequence"] = fixture.Barrier.TombstoneSequence.ToString();
        input["intent"] = "override-subject-retirement-barrier";
        (Response executed, Response replay) = await ExecuteMappedAsync(app, "retirement.override", target, input, "override-request-1");
        if (executed.Status != StatusCodes.Status200OK)
        {
            BaseResult<BaseSubjectRetirementOverrideResult> diagnostic = await session.SubjectRetirements.OverrideAsync(new()
            {
                ContractId = ContractId, ContractVersion = 1, SubjectId = fixture.Fact.SubjectId,
                AuthorityEpoch = fixture.Fact.AuthorityEpoch, Incarnation = fixture.Fact.Incarnation,
                ExpectedTombstoneSequence = fixture.Barrier.TombstoneSequence, ExpectedBarrierGeneration = timeout.Generation,
                ExpectedBarrierChecksum = timeout.BarrierChecksum, Intent = "override-subject-retirement-barrier", ChangeReference = "change-42",
                Identity = Identity("diagnose-override"),
            });
            Assert.True(diagnostic.TryGetValue(out _), diagnostic is BaseFailure<BaseSubjectRetirementOverrideResult> failure ? failure.Error.Code : "missing");
        }
        Assert.True(executed.Status == StatusCodes.Status200OK, $"override execute: {executed.Status} {executed.Body}");
        Assert.Equal(executed.Body, replay.Body);
    }

    [Theory]
    [InlineData("retirement.timeout")]
    [InlineData("retirement.override")]
    [InlineData("retirement.purge")]
    [InlineData("retirement.consumer.remove")]
    public async Task Inspect_mode_returns_404_for_retirement_command_endpoints(string command)
    {
        await using WebApplication app = Build(BaseStudioMode.Inspect);
        Assert.True((await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        Response response = await InvokeAsync(app,
            Endpoint(app, $"/studio/base/studio/commands/{command}/preview"), "{}", $"base.studio.preview.{command}", null);
        Assert.Equal(StatusCodes.Status404NotFound, response.Status);
    }

    [Theory]
    [InlineData("cas")]
    [InlineData("resource-token")]
    [InlineData("target")]
    [InlineData("authority")]
    public async Task Timeout_preview_rejects_exact_substitutions_with_404(string substitution)
    {
        await using WebApplication app = Build(BaseStudioMode.Operate);
        Assert.True((await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BarrierFixture fixture = await CreateBarrierAsync(app);
        app.Services.GetRequiredService<MutableClock>().Advance(TimeSpan.FromMinutes(2));
        Bootstrap lease = await BootstrapAsync(app);
        var resource = new BaseStudioRetirementBarrierResource(ApplicationId, ContractId, 1, "subject-1",
            fixture.Fact.AuthorityEpoch.ToBase64Url(), fixture.Fact.Incarnation.ToBase64Url());
        JsonObject target = JsonNode.Parse(ResourceJson(resource))!.AsObject();
        var input = new JsonObject
        {
            ["expectedBarrierChecksum"] = fixture.Barrier.BarrierChecksum,
            ["expectedBarrierGeneration"] = fixture.Barrier.Generation.ToString(),
            ["mode"] = "preview",
            ["previewChecksum"] = null,
            ["resourceToken"] = BaseStudioResourceRouteToken.Encode(resource),
        };
        string authority = lease.Body.GetProperty("authority").GetProperty("checksum").GetString()!;
        switch (substitution)
        {
            case "cas": input["expectedBarrierChecksum"] = Hex('f'); break;
            case "resource-token":
                input["resourceToken"] = BaseStudioResourceRouteToken.Encode(new BaseStudioRetirementBarrierResource(
                    ApplicationId, ContractId, 1, "subject-2", fixture.Fact.AuthorityEpoch.ToBase64Url(), fixture.Fact.Incarnation.ToBase64Url())); break;
            case "target": target["protectedSubjectIdentity"] = "subject-2"; break;
            case "authority": authority = Hex('f'); break;
        }
        string body = new JsonObject
        {
            ["commandId"] = "retirement.timeout",
            ["input"] = input,
            ["pageId"] = "base.retirementBarrier.detail",
            ["responseAuthorityChecksum"] = authority,
            ["target"] = target,
        }.ToJsonString();
        Response response = await PostAsync(app, "/studio/base/studio/commands/retirement.timeout/preview",
            "base.studio.preview.retirement.timeout", lease.Snapshot, body);
        int expected = substitution is "target" or "authority"
            ? StatusCodes.Status400BadRequest : StatusCodes.Status404NotFound;
        Assert.Equal(expected, response.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_preview_and_execute_use_the_real_tombstone_barrier_and_replay_exact_result(bool expirePreview)
    {
        await using WebApplication app = Build(BaseStudioMode.Operate);
        Assert.True((await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BarrierFixture fixture = await CreateBarrierAsync(app);
        app.Services.GetRequiredService<MutableClock>().Advance(TimeSpan.FromMinutes(2));
        Bootstrap lease = await BootstrapAsync(app);
        string authority = lease.Body.GetProperty("authority").GetProperty("checksum").GetString()!;
        string target = ResourceJson(new BaseStudioRetirementBarrierResource(ApplicationId, ContractId, 1, "subject-1",
            fixture.Fact.AuthorityEpoch.ToBase64Url(), fixture.Fact.Incarnation.ToBase64Url()));
        string resourceToken = BaseStudioResourceRouteToken.Encode(new BaseStudioRetirementBarrierResource(ApplicationId, ContractId, 1, "subject-1",
            fixture.Fact.AuthorityEpoch.ToBase64Url(), fixture.Fact.Incarnation.ToBase64Url()));
        string input = JsonSerializer.Serialize(new
        {
            expectedBarrierChecksum = fixture.Barrier.BarrierChecksum,
            expectedBarrierGeneration = fixture.Barrier.Generation.ToString(),
            mode = "preview",
            previewChecksum = (string?)null,
            resourceToken,
        });
        string previewBody = $"{{\"commandId\":\"retirement.timeout\",\"input\":{input},\"pageId\":\"base.retirementBarrier.detail\",\"responseAuthorityChecksum\":\"{authority}\",\"target\":{target}}}";
        Response preview = await PostAsync(app, "/studio/base/studio/commands/retirement.timeout/preview",
            "base.studio.preview.retirement.timeout", lease.Snapshot, previewBody);
        Assert.Equal(StatusCodes.Status200OK, preview.Status);
        using JsonDocument previewJson = JsonDocument.Parse(preview.Body);
        JsonElement previewValue = previewJson.RootElement.Clone();
        string previewChecksum = previewValue.GetProperty("previewChecksum").GetString()!;
        string executeBody = JsonSerializer.Serialize(new
        {
            acknowledgements = new[] { new { impactId = "impact.retirement.timeout", previewChecksum, purposeId = "confirm.retirement.timeout" } },
            commandId = "retirement.timeout",
            freshAuthentication = (string?)null,
            pageId = "base.retirementBarrier.detail",
            preview = previewValue,
            requestIdentity = "timeout-request-1",
            responseAuthorityChecksum = authority,
            target = JsonDocument.Parse(target).RootElement,
        });
        if (expirePreview)
            app.Services.GetRequiredService<MutableClock>().Advance(TimeSpan.FromMinutes(6));
        Response executed = await PostAsync(app, "/studio/base/studio/commands/retirement.timeout/execute",
            "base.studio.execute.retirement.timeout", lease.Snapshot, executeBody);
        if (expirePreview)
        {
            Assert.Equal(StatusCodes.Status404NotFound, executed.Status);
            return;
        }
        Assert.Equal(StatusCodes.Status200OK, executed.Status);
        using JsonDocument result = JsonDocument.Parse(executed.Body);
        Assert.Equal("execute", result.RootElement.GetProperty("mode").GetString());
        Assert.Equal((fixture.Barrier.Generation + 1).ToString(), result.RootElement.GetProperty("resultingGeneration").GetString());

        Response replay = await PostAsync(app, "/studio/base/studio/commands/retirement.timeout/execute",
            "base.studio.execute.retirement.timeout", lease.Snapshot, executeBody);
        Assert.Equal(StatusCodes.Status200OK, replay.Status);
        Assert.Equal(executed.Body, replay.Body);
    }

    private const string ApplicationId = "retirement.http.application";
    private const string ContractId = "retirement.http.subject";

    private static BaseStudioRetirementBarrierResource BarrierResource(BarrierFixture fixture) =>
        new(ApplicationId, ContractId, 1, "subject-1", fixture.Fact.AuthorityEpoch.ToBase64Url(), fixture.Fact.Incarnation.ToBase64Url());
    private static JsonObject BaseInput(BaseStudioResourceIdentity target) => new()
    {
        ["mode"] = "preview",
        ["previewChecksum"] = null,
        ["resourceToken"] = BaseStudioResourceRouteToken.Encode(target),
    };
    private static BaseSession ControlSession(WebApplication app) => app.Services.GetRequiredService<IBaseSessionFactory>().For(
        FixturePrincipal(), options => { options.Audience = HPDBaseEndpointAudience.ControlPlane; options.Mode = OperationMode.User; });
    private static async Task<(Response Executed, Response Replay)> ExecuteMappedAsync(WebApplication app, string command,
        BaseStudioResourceIdentity target, JsonObject input, string requestIdentity)
    {
        Bootstrap lease = await BootstrapAsync(app);
        input = new JsonObject(input.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
        string authority = lease.Body.GetProperty("authority").GetProperty("checksum").GetString()!;
        JsonNode targetNode = JsonNode.Parse(target switch
        {
            BaseStudioRetirementBarrierResource barrier => ResourceJson(barrier),
            BaseStudioLifecycleConsumerResource consumer => ResourceJson(consumer),
            _ => throw new NotSupportedException(),
        })!;
        string page = command == "retirement.consumer.remove" ? "base.lifecycleConsumer.detail" : "base.retirementBarrier.detail";
        string previewBody = new JsonObject
        {
            ["commandId"] = command,
            ["input"] = input,
            ["pageId"] = page,
            ["responseAuthorityChecksum"] = authority,
            ["target"] = targetNode.DeepClone()
        }.ToJsonString();
        Response preview = await PostAsync(app, $"/studio/base/studio/commands/{command}/preview",
            $"base.studio.preview.{command}", lease.Snapshot, previewBody);
        Assert.True(preview.Status == StatusCodes.Status200OK, $"preview {command}: {preview.Status} {preview.Body}");
        JsonNode previewNode = JsonNode.Parse(preview.Body)!;
        string checksum = previewNode["previewChecksum"]!.GetValue<string>();
        string? freshAuthority = null;
        if (command != "retirement.timeout")
        {
            string freshBody = new JsonObject
            {
                ["commandId"] = command,
                ["previewChecksum"] = checksum,
                ["requestIdentity"] = requestIdentity,
                ["targetToken"] = BaseStudioResourceRouteToken.Encode(target)
            }.ToJsonString();
            Response fresh = await InvokeAsync(app, Endpoint(app, "/studio/base/studio/auth/fresh"), freshBody, null, null);
            Assert.True(fresh.Status == StatusCodes.Status200OK, $"fresh {command}: {fresh.Status} {fresh.Body}");
            freshAuthority = JsonNode.Parse(fresh.Body)!["authority"]!.GetValue<string>();
        }
        JsonObject executeNode = new()
        {
            ["acknowledgements"] = new JsonArray(new JsonObject
            {
                ["impactId"] = $"impact.{command}",
                ["previewChecksum"] = checksum,
                ["purposeId"] = $"confirm.{command}"
            }),
            ["commandId"] = command,
            ["freshAuthentication"] = freshAuthority,
            ["pageId"] = page,
            ["preview"] = previewNode,
            ["requestIdentity"] = requestIdentity,
            ["responseAuthorityChecksum"] = authority,
            ["target"] = targetNode.DeepClone(),
        };
        string executeBody = executeNode.ToJsonString();
        Response executed = await PostAsync(app, $"/studio/base/studio/commands/{command}/execute",
            $"base.studio.execute.{command}", lease.Snapshot, executeBody);
        Response replay = await PostAsync(app, $"/studio/base/studio/commands/{command}/execute",
            $"base.studio.execute.{command}", lease.Snapshot, executeBody);
        return (executed, replay);
    }

    private static WebApplication Build(BaseStudioMode mode, string? sqliteDataSource = null)
    {
        WebApplicationBuilder host = WebApplication.CreateBuilder(); host.Services.AddLogging();
        var clock = new MutableClock(DateTimeOffset.UtcNow.AddMinutes(-2));
        host.Services.AddSingleton(clock); host.Services.AddSingleton<TimeProvider>(clock);
        host.Services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(static options =>
            {
                options.ApplicationId = ApplicationId;
                options.PlanProtectionKey = SHA256.HashData("retirement-http-schema-plan"u8);
            });
            if (sqliteDataSource is null)
                builder.ConfigureInMemoryStore(options => { options.StoreId = "retirement-http"; options.Collections = [RetirementPrivateRecord.Collection.Definition]; });
            builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = SHA256.HashData("retirement-http-key"u8), IssueNotBefore = DateTimeOffset.UnixEpoch })
                .AddCollection(RetirementPrivateRecord.Collection)
                .AddExportedSubject(RetirementHttpSubject.HPDBaseSubjectRegistration)
                .AddSubjectLifecycleConsumer(LifecycleConsumer())
                .AddSubjectRetirementConsumer(RetirementConsumer())
                .AddSubjectRetirementPolicy(RetirementPolicy());
            builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition { Id = "retirement.http.policy", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "retirement.http.allow", EvaluatorContractVersion = 1, CompositionOrder = 0 }, new AllowPolicy());
            foreach (string operation in new[] { "base.studio.action.discover", "base.studio.action.execute", "base.studio.action.preview", "base.studio.bootstrap.read", "base.studio.diagnostics.inspect", "base.studio.invalidation.subscribe", "base.studio.receipt.discover", "base.studio.receipt.inspect", "base.studio.resource.discover", "base.studio.resource.inspect", "base.studio.resource.links", "base.studio.resource.search" })
                Grant(builder, operation, operation, HPDBaseEndpointAudience.ControlPlane, new ResourceScope { Kind = ResourceScopeKind.Runtime });
            SubjectGrant(builder, "base.subjectLifecycle.tombstone", "base.subjectLifecycle.tombstone");
            SubjectGrant(builder, "subject.admin", "subject.admin", HPDBaseEndpointAudience.ControlPlane);
            string purgeSource = RetirementHttpSubject.HPDBaseSubjectRegistration.Definition.Id + ".retirement.purge.source";
            Grant(builder, purgeSource, RetirementPrivateRecord.Collection.Id,
                HPDBaseEndpointAudience.ControlPlane, new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = RetirementPrivateRecord.Collection.Id });
            SubjectGrant(builder, "retirement.consumer.read", "retirement.consumer.read");
            SubjectGrant(builder, "base.subjectLifecycle.feed.read", "base.subjectLifecycle.feed.read");
            SubjectGrant(builder, "base.subjectRetirement.barrier.inspect", "base.subjectRetirement.barrier.inspect", HPDBaseEndpointAudience.ControlPlane);
            SubjectGrant(builder, "base.subjectRetirement.timeout.process", "base.subjectRetirement.timeout.process", HPDBaseEndpointAudience.ControlPlane);
            SubjectGrant(builder, "base.subjectRetirement.override", "base.subjectRetirement.override", HPDBaseEndpointAudience.ControlPlane);
            SubjectGrant(builder, "base.subjectRetirement.purge", "base.subjectRetirement.purge", HPDBaseEndpointAudience.ControlPlane);
            SubjectGrant(builder, "base.subjectRetirement.consumerRemoval", "base.subjectRetirement.consumerRemoval", HPDBaseEndpointAudience.ControlPlane);
            builder.AddStaticGrantAuthority(new() { Id = "system.private", Version = 1, OwningModuleId = "tests", SourceContractId = "retirement.http.grant", SourceContractVersion = 1 },
                new() { Id = "system.private", Subject = new() { Kind = AccessSubjectKind.System, Id = "fixture-worker" }, Action = "*", Effect = GrantEffect.Allow, Scope = new() { Kind = ResourceScopeKind.Runtime } });
            if (sqliteDataSource is not null)
                builder.UseStore(SqliteStore.Configure(options => { options.StoreId = "retirement-http"; options.DataSource = sqliteDataSource; options.AdministrationEnabled = true; }));
        });
        host.Services.AddSingleton(static services => Assert.IsAssignableFrom<IBaseStudioDynamicStoreAuthoritySource>(services.GetRequiredService<IAtomicRecordStore>()));
        host.Services.AddHPDAIPlatform().AddStudioAuthentication(static _ => new Authentication()).AddBaseStudio(static _ => new PrincipalResolver(), options => options.Mode = mode);
        WebApplication app = host.Build(); app.MapHPDAIPlatform(); return app;
    }

    private static async Task<BarrierFixture> CreateBarrierAsync(WebApplication app)
    {
        Assert.NotNull(app.Services.GetRequiredService<BaseSubjectRetirementRegistry>().FindPolicy(ContractId, 1));
        BaseGeneratedSubjectRegistration registration = app.Services.GetRequiredService<BaseSubjectContractRegistry>().Find(ContractId, 1)!;
        Assert.Equal(RetirementPrivateRecord.Collection.Id, registration.Definition.ValidationPlan.PrivateCollectionId);
        PrincipalContext principal = FixturePrincipal();
        BaseSubjectId subjectId = BaseSubjectId.Create("subject-1", BaseSubjectIdKind.OrdinalString);
        IBaseSubjectPublicationStore publicationStore = Assert.IsAssignableFrom<IBaseSubjectPublicationStore>(
            app.Services.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(RetirementPrivateRecord.Collection.Id));
        BaseSubjectCurrentPublicationState publication = Assert.Single(
            (await publicationStore.ReadCurrentSubjectPublicationsAsync()).Value!,
            value => value.ContractId == ContractId && value.ContractVersion == 1);
        OperationResult<BaseRecordBatchResult> created = await app.Services.GetRequiredService<IBaseMutationCoordinator>().ExecuteBatchAsync(new()
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            RequestIdentity = Identity("create"),
            Operations = [new()
            {
                ItemId = "create-subject", CollectionId = RetirementPrivateRecord.Collection.Id, Kind = BaseRecordMutationKind.Create,
                Create = new()
                {
                    RequestedId = RecordId.Create("subject-1"),
                    Payload = Payload(("active", true), ("tombstoned", false), ("tenant", "tenant-a")),
                },
            }],
        }, principal, Operation(BaseOperationKind.Create), CancellationToken.None);
        Assert.True(created.IsSuccess(), created.Error?.Code);
        BaseRecordBatchItemResult createdItem = Assert.Single(created.Value!.Items);
        Assert.True(createdItem.Status == OperationStatus.Created,
            $"{createdItem.Status}: {createdItem.Error?.Code}: {createdItem.Error?.Message}");
        BaseSubjectLifecycleCommitEvidence createdLifecycle = Assert.IsType<BaseSubjectLifecycleCommitEvidence>(createdItem.SubjectLifecycle);
        RevisionToken createdRevision = new(createdItem.Revision?.Revision
            ?? throw new InvalidOperationException("The committed subject revision is missing."));
        BaseSession session = app.Services.GetRequiredService<IBaseSessionFactory>().For(principal, options => { options.Audience = HPDBaseEndpointAudience.Application; options.Mode = OperationMode.System; });
        var subject = new BaseSubjectReference<RetirementHttpSubject>(subjectId, createdLifecycle.AuthorityEpoch, createdLifecycle.Incarnation);
        BaseMutationRequestIdentity identity = Identity("tombstone");
        BaseResult<BaseSubjectLifecycleFact<RetirementHttpSubject>> tombstoned = await app.Services.GetRequiredService<IBaseSubjectLifecycleExporterRuntime>().TombstoneAsync<RetirementHttpSubject>(session,
            RetirementHttpSubject.HPDBaseSubjectRegistration, new() { Subject = subject, ExpectedPrivateRevision = createdRevision, Identity = identity }, CancellationToken.None);
        Assert.True(tombstoned.TryGetValue(out BaseSubjectLifecycleFact<RetirementHttpSubject>? tombstoneFact), tombstoned is BaseFailure<BaseSubjectLifecycleFact<RetirementHttpSubject>> failure ? failure.Error.Code : "missing tombstone value");
        Assert.Equal(BaseSubjectLifecycleState.Tombstoned, tombstoneFact!.Fact.Transitioned!.CurrentState);
        IBaseSubjectRetirementStore retirementStore = Assert.IsAssignableFrom<IBaseSubjectRetirementStore>(app.Services.GetRequiredService<IRecordStoreRegistry>().GetStoreForCollection(RetirementPrivateRecord.Collection.Id));
        OperationResult<BaseSubjectRetirementPublicationPage> publications = await retirementStore.ReadPublicationsAsync(new() { Take = 16 });
        Assert.Contains(publications.Value!.Rows, row => row.Fact.Kind == BaseSubjectRetirementPublicationKind.BarrierCreated);
        await app.Services.GetRequiredService<BaseSubjectRetirementControlDispatcher>().ReconcileAsync(CancellationToken.None);
        OperationResult<BaseSubjectRetirementBarrierPage> barriers = await retirementStore.ReadBarriersAsync(new()
        {
            ApplicationId = ApplicationId,
            ContractId = ContractId,
            ContractVersion = 1,
            ScopeAuthority = new() { Mode = BaseSubjectScopeQueryMode.ExactScope, ExactScope = new() { Kind = BaseSubjectScopeKind.Global }, InstalledAuthorityDigest = RetirementHttpSubject.HPDBaseSubjectRegistration.Checksum },
            Take = 1,
            MaximumResultBytes = 1_048_576,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        BaseSubjectRetirementBarrier actualBarrier = Assert.Single(barriers.Value!.Barriers).Barrier;
        Assert.Equal(actualBarrier.SubjectId, tombstoneFact.Fact.SubjectId);
        Assert.Equal(actualBarrier.AuthorityEpoch.ToBase64Url(), tombstoneFact.Fact.AuthorityEpoch.ToBase64Url());
        Assert.Equal(actualBarrier.Incarnation, tombstoneFact.Fact.Incarnation);
        BaseSession controlSession = app.Services.GetRequiredService<IBaseSessionFactory>().For(principal, options => { options.Audience = HPDBaseEndpointAudience.ControlPlane; options.Mode = OperationMode.User; });
        BaseResult<BaseSubjectRetirementInspection> inspected = await controlSession.SubjectRetirements.InspectAsync(new()
        {
            ContractId = ContractId,
            ContractVersion = 1,
            SubjectId = tombstoneFact!.Fact.SubjectId,
            AuthorityEpoch = tombstoneFact.Fact.AuthorityEpoch,
            Incarnation = tombstoneFact.Fact.Incarnation,
            ScopeAuthority = new() { Mode = BaseSubjectScopeQueryMode.ExactScope, ExactScope = new() { Kind = BaseSubjectScopeKind.Global }, InstalledAuthorityDigest = "test" },
            IncludeTerminalSummary = false,
            MaximumResultBytes = 1_048_576,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        Assert.Equal(actualBarrier, inspected.RequireValue().CurrentBarrier);
        OperationResult<RecordEnvelope> privateRecord = await app.Services.GetRequiredService<IBaseRecordRuntime>().GetAsync(
            RetirementPrivateRecord.Collection.Id, RecordId.Create("subject-1"), principal,
            Operation(BaseOperationKind.Get) with { RecordId = "subject-1" });
        Assert.True(privateRecord.IsSuccess(), privateRecord.Error?.Code);
        return new(tombstoneFact.Fact, actualBarrier, privateRecord.Value!.Metadata.Revision!.Value);
    }

    private static BaseSubjectLifecycleConsumerDefinition LifecycleConsumer() => new() { Id = "retirement.consumer", Version = 1, OwningModuleId = "tests", Audience = BaseSubjectLifecycleConsumerAudience.Service, ContractId = ContractId, ContractVersion = 1, ObservedStates = [BaseSubjectLifecycleState.Tombstoned], DeliveryGrantId = "retirement.consumer.read", Limits = new() { MaximumFactsPerPage = 16, MaximumResultBytes = 65_536, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(1) } };
    private static BaseSubjectRetirementConsumerDefinition RetirementConsumer()
    {
        BaseSubjectLifecycleRegistry registry = new([LifecycleConsumer()], new BaseSubjectContractRegistry([RetirementHttpSubject.HPDBaseSubjectRegistration]));
        return new() { ConsumerId = "retirement.consumer", ConsumerVersion = 1, OwningModuleId = "tests", Audience = BaseSubjectLifecycleConsumerAudience.Service, LifecycleConsumerChecksum = registry.All.Single().Checksum, RetirementProfileId = "retirement.profile", RetirementProfileVersion = 1, RetirementProfileChecksum = Hex('2'), Participation = BaseSubjectRetirementParticipation.RequiredBeforePurge, AcknowledgementGrantId = "retirement.ack", Limits = new() { MaximumAcknowledgementsPerCommit = 16, MaximumAcknowledgementRequestBytes = 65_536, MaximumReceiptBytes = 65_536, AcknowledgementTimeout = TimeSpan.FromSeconds(1), ReceiptResolutionTimeout = TimeSpan.FromSeconds(1) } };
    }
    private static BaseSubjectRetirementPolicy RetirementPolicy()
    {
        BaseSubjectRetirementConsumerDefinition consumer = RetirementConsumer();
        string checksum = BaseSubjectRetirementRegistry.ConsumerChecksum(BaseSubjectRetirementRegistry.Normalize(consumer));
        BaseSubjectRetirementPolicy policy = new() { ContractId = ContractId, ContractVersion = 1, AcceptedConsumers = [new() { ConsumerId = consumer.ConsumerId, ConsumerVersion = consumer.ConsumerVersion, OwningModuleId = consumer.OwningModuleId, Audience = consumer.Audience, LifecycleConsumerChecksum = consumer.LifecycleConsumerChecksum, RetirementProfileId = consumer.RetirementProfileId, RetirementProfileVersion = consumer.RetirementProfileVersion, RetirementProfileChecksum = consumer.RetirementProfileChecksum, Participation = consumer.Participation, AcknowledgementGrantId = consumer.AcknowledgementGrantId, Limits = consumer.Limits, RetirementConsumerChecksum = checksum }], CoordinationWindow = TimeSpan.FromMinutes(1), TimeoutBehavior = BaseSubjectRetirementTimeoutBehavior.Quarantine, PurgeRetention = new() { MinimumTombstoneAge = TimeSpan.Zero }, PolicyChecksum = Hex('0') };
        return policy with { PolicyChecksum = BaseSubjectRetirementRegistry.PolicyChecksum(policy with { PolicyChecksum = string.Empty }) };
    }

    private static void Grant(HPDBaseBuilder builder, string id, string action, HPDBaseEndpointAudience audience, ResourceScope scope)
    {
        bool studio = id.StartsWith("base.studio.", StringComparison.Ordinal);
        builder.AddStaticGrantAuthority(new() { Id = id, Version = 1, OwningModuleId = studio ? "base" : "tests", SourceContractId = studio ? "base.studio.fixed-grant" : "retirement.http.grant", SourceContractVersion = 1 },
            new() { Id = id, ApplicationId = ApplicationId, ModuleId = studio ? "base" : "tests", Audience = audience, Subject = new() { Kind = AccessSubjectKind.System, Id = "fixture-worker" }, Action = action, Effect = GrantEffect.Allow, Scope = scope });
    }
    private static void SubjectGrant(HPDBaseBuilder builder, string id, string action, HPDBaseEndpointAudience audience = HPDBaseEndpointAudience.Application) => builder.AddStaticGrantAuthority(
        new() { Id = id, Version = 1, OwningModuleId = "tests", SourceContractId = "retirement.http.grant", SourceContractVersion = 1 },
        new() { Id = id, ApplicationId = ApplicationId, ModuleId = "tests", Audience = audience, Subject = new() { Kind = AccessSubjectKind.System, Id = "fixture-worker" }, Action = action, Effect = GrantEffect.Allow, Scope = new() { Kind = ResourceScopeKind.SubjectContract, SubjectContractId = ContractId, SubjectContractVersion = 1 } });
    private static PrincipalContext Principal() => FixturePrincipal();
    private static PrincipalContext FixturePrincipal() => new() { AuthenticationState = PrincipalAuthenticationState.Service, SubjectKind = AccessSubjectKind.System, SubjectId = "fixture-worker" };
    private static OperationContext Operation(BaseOperationKind kind) => new() { ApplicationId = ApplicationId, Operation = kind, CollectionId = RetirementPrivateRecord.Collection.Id, Audience = HPDBaseEndpointAudience.Application, Mode = OperationMode.System };
    private static BaseMutationRequestIdentity Identity(string operation) { byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(operation)); return BaseMutationRequestIdentity.Create("retirement-http", operation, Convert.ToHexStringLower(digest), BaseMutationRequestFingerprint.Create(digest)); }
    private static RecordPayload Payload(params (string Name, object Value)[] fields) => new() { Kind = RecordPayloadKind.FieldMap, Fields = fields.ToDictionary(static value => value.Name, static value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal) };
    private static string ResourceJson(BaseStudioRetirementBarrierResource value) => JsonSerializer.Serialize(new { applicationId = value.ApplicationId, authorityChecksum = Convert.ToHexStringLower(value.AuthorityChecksum.ToArray()), authorityEpoch = value.AuthorityEpoch, contractId = value.ContractId, contractVersion = value.ContractVersion, incarnation = value.Incarnation, kind = "retirementBarrier", protectedSubjectIdentity = value.ProtectedSubjectIdentity });
    private static string ResourceJson(BaseStudioLifecycleConsumerResource value) => JsonSerializer.Serialize(new { applicationId = value.ApplicationId, authorityChecksum = Convert.ToHexStringLower(value.AuthorityChecksum.ToArray()), consumerId = value.ConsumerId, contractId = value.ContractId, contractVersion = value.ContractVersion, kind = "lifecycleConsumer", version = value.Version });
    private static string Hex(char value) => new(value, 64);

    private static async Task<Bootstrap> BootstrapAsync(WebApplication app) { Response shell = await InvokeAsync(app, Endpoint(app, "/studio/control/shell"), "{}", null, null); using JsonDocument descriptor = JsonDocument.Parse(shell.Body); JsonElement root = descriptor.RootElement; string body = JsonSerializer.Serialize(new { shellContractChecksum = root.GetProperty("shellContractChecksum").GetString(), editionAssetGraphChecksum = root.GetProperty("editionAssetGraphChecksum").GetString(), runtimeClientChecksum = root.GetProperty("runtimeClientChecksum").GetString(), locale = "en-US", clientCapabilities = new[] { 1, 2 } }); Response response = await InvokeAsync(app, Endpoint(app, "/studio/control/bootstrap"), body, null, null); Assert.Equal(200, response.Status); using JsonDocument document = JsonDocument.Parse(response.Body); JsonElement owned = document.RootElement.Clone(); return new(owned, owned.GetProperty("snapshotChecksum").GetString()!); }
    private static Task<Response> PostAsync(WebApplication app, string route, string method, string snapshot, string body) => InvokeAsync(app, Endpoint(app, route), body, method, snapshot);
    private static RouteEndpoint Endpoint(WebApplication app, string route) => Assert.IsType<RouteEndpoint>(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints).Single(value => value is RouteEndpoint endpoint && endpoint.RoutePattern.RawText == route));
    private static async Task<Response> InvokeAsync(WebApplication app, RouteEndpoint endpoint, string body, string? method, string? snapshot) { var context = new DefaultHttpContext { RequestServices = app.Services }; context.Request.ContentType = "application/json"; byte[] bytes = Encoding.UTF8.GetBytes(body); context.Request.Body = new MemoryStream(bytes); context.Request.ContentLength = bytes.Length; context.Response.Body = new MemoryStream(); if (method is not null) context.Request.Headers["X-HPD-Studio-Method"] = method; if (snapshot is not null) context.Request.Headers["X-HPD-Studio-Snapshot"] = snapshot; await endpoint.RequestDelegate!(context); return new(context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())); }
    private sealed record Bootstrap(JsonElement Body, string Snapshot); private sealed record Response(int Status, string Body); private sealed record BarrierFixture(BaseSubjectLifecycleFact Fact, BaseSubjectRetirementBarrier Barrier, RevisionToken PrivateRevision);
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider { private DateTimeOffset _now = now; public override DateTimeOffset GetUtcNow() => _now; internal void Advance(TimeSpan duration) => _now = _now.Add(duration); }
    private sealed class PrincipalResolver : IBaseStudioPrincipalContextResolver { public ValueTask<PrincipalContext?> ResolveAsync(HttpContext context, BaseStudioSessionObservation session, CancellationToken token) => ValueTask.FromResult<PrincipalContext?>(Principal()); public ValueTask<BaseOwnedSubjectScopeEvidence?> ResolveScopeAsync(HttpContext context, BaseStudioSessionObservation session, CancellationToken token) => ValueTask.FromResult<BaseOwnedSubjectScopeEvidence?>(new() { Kind = BaseSubjectScopeKind.Global }); }
    private sealed class AllowPolicy : IPolicyEvaluator { public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken token = default) => ValueTask.FromResult(PolicyDecision.Allow()); }
    private sealed class Authentication : IBaseStudioAuthenticationIntegration { public BaseStudioAuthenticationDescriptor Descriptor { get; } = BaseStudioAuthenticationDescriptor.Create("tests.auth", 1, BaseStudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session", ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, [BaseStudioFreshAuthenticationClass.MultiFactor]); public ValueTask<BaseStudioAuthenticationResult<BaseStudioSessionObservation>> ObserveSessionAsync(HttpContext context, CancellationToken token) => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioSessionObservation>.Success(Session())); public ValueTask<BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>> ProtectReturnTargetAsync(HttpContext context, string? target, CancellationToken token) => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>.Failed(BaseStudioAuthenticationFailure.IntegrationUnavailable)); public ValueTask BeginSignInAsync(HttpContext context, BaseStudioProtectedReturnTarget target, CancellationToken token) => ValueTask.CompletedTask; public ValueTask CompleteCallbackAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask; public ValueTask BeginSignOutAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask; public ValueTask<BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>> AuthorizeRequestAsync(HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token) { BaseStudioSessionObservation session = Session(); return ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>.Success(BaseStudioTransportAuthorization.Create(session, purpose, session.IssuedAtUtc.AddMinutes(10)))); } public async ValueTask<BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token) { var result = await AuthorizeRequestAsync(context, purpose, token); return BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>.Success(BaseStudioBrowserAuthorization.Create("X-HPD-Test", "authority", result.Value!)); } public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(HttpContext context, BaseStudioFreshAuthenticationRequest request, CancellationToken token) { DateTimeOffset now = request.IssuedAtUtc; BaseStudioFreshAuthenticationBinding binding = BaseStudioFreshAuthenticationBinding.Create(request, Descriptor.IntegrationId, Descriptor.Checksum, now); BaseStudioFreshAuthenticationAuthority authority = BaseStudioFreshAuthenticationAuthority.Create(new string('F', 32), binding, now, request.RequiredAssurance, "tests-key"); return ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(new BaseStudioFreshAuthenticationResult.Satisfied(authority))); } public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(HttpContext context, BaseStudioFreshAuthenticationContinuation continuation, CancellationToken token) => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(new BaseStudioFreshAuthenticationResult.Unsupported())); private BaseStudioSessionObservation Session() { DateTimeOffset now = DateTimeOffset.UtcNow; return BaseStudioSessionObservation.Create(1, BaseStudioSha256.FromDigest(new byte[32]), "control-plane", BaseStudioSha256.FromDigest(Enumerable.Repeat((byte)1, 32).ToArray()), now, now.AddHours(5), Descriptor.Checksum); } }
}

[BaseCollection("retirement.http.private", typeof(RetirementHttpJsonContext), SystemOwnerModuleId = "tests")]
internal sealed partial record RetirementPrivateRecord { [BaseField("retirement.http.active")] public required bool Active { get; init; } [BaseField("retirement.http.tombstoned")] public required bool Tombstoned { get; init; } [BaseField("retirement.http.tenant")] public required string Tenant { get; init; } }
[BaseExportedSubject(ContractId, OwningModuleId = "tests", PrivateRecordType = typeof(RetirementPrivateRecord), AcquisitionGrantId = "subject.acquire", ValidationGrantId = "subject.validate", AdministrationGrantId = "subject.admin", ValidationPlanId = "retirement.http.validation", ActiveFieldId = "retirement.http.active", TombstoneFieldId = "retirement.http.tombstoned", SupportsCoordinatedRetirement = true)]
internal sealed partial class RetirementHttpSubject { private const string ContractId = "retirement.http.subject"; }
[JsonSerializable(typeof(RetirementPrivateRecord))][JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)] internal sealed partial class RetirementHttpJsonContext : JsonSerializerContext;
