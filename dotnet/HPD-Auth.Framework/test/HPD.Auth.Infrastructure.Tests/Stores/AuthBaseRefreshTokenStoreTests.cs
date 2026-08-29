using System.Collections.Immutable;
using System.Security.Cryptography;
using FluentAssertions;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using HPD.Auth.Core.Options;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Xml.Linq;

namespace HPD.Auth.Infrastructure.Tests.Stores;

public sealed class AuthBaseRefreshTokenStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Data_protection_repository_reads_from_owned_cache_and_replays_identical_create()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        HPDBaseDataProtectionXmlRepository repository = provider
            .GetRequiredService<HPDBaseDataProtectionXmlRepository>();
        await repository.StartAsync(CancellationToken.None);
        try
        {
            var key = XElement.Parse("<key id=\"alpha\"><descriptor><value>secret</value></descriptor></key>");
            repository.StoreElement(key, "key-alpha");
            repository.StoreElement(new XElement(key), "key-alpha");

            XElement first = repository.GetAllElements().Single();
            first.SetAttributeValue("id", "mutated");
            XElement second = repository.GetAllElements().Single();

            second.Attribute("id")!.Value.Should().Be("alpha");
            second.Should().NotBeSameAs(first);
        }
        finally
        {
            await repository.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Data_protection_repository_rejects_same_name_with_different_content()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        HPDBaseDataProtectionXmlRepository repository = provider
            .GetRequiredService<HPDBaseDataProtectionXmlRepository>();
        await repository.StartAsync(CancellationToken.None);
        try
        {
            repository.StoreElement(XElement.Parse("<key id=\"alpha\" />"), "key-alpha");

            Action collision = () => repository.StoreElement(
                XElement.Parse("<key id=\"beta\" />"), "key-alpha");

            collision.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            await repository.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Data_protection_repository_invalidates_immediately_after_external_key_commit()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        HPDBaseDataProtectionXmlRepository repository = provider
            .GetRequiredService<HPDBaseDataProtectionXmlRepository>();
        await repository.StartAsync(CancellationToken.None);
        try
        {
            repository.GetAllElements().Should().BeEmpty();
            byte[] xml = "<key id=\"external\" />"u8.ToArray();
            byte[] digest = SHA256.HashData(xml);
            string id = AuthBaseDeterministicId.Create("HPD Auth Infrastructure Tests", "key-external");
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "hpd.auth",
                AuthSource = "hpd.auth.data-protection.external-test.v1",
            });
            BaseBatchBuilder batch = session.Atomic(AuthBaseRuntime.MutationIdentity(
                "hpd.auth.data-protection.external-test.v1", Guid.Empty, id,
                Convert.ToHexStringLower(digest)));
            var record = new AuthDataProtectionKeyRecordV1
            {
                Id = id,
                ApplicationDiscriminator = "HPD Auth Infrastructure Tests",
                FriendlyName = "key-external",
                CanonicalXml = BaseBinary.From(xml),
                ContentDigest = BaseBinary.From(digest),
                CreatedAt = Now,
                FormatVersion = 1,
            };
            batch.Create(AuthDataProtectionKeyRecordV1.Collection, RecordId.Create(id), record);
            (await batch.CommitAsync()).RequireValue().RequireCommitted();

            Action staleRead = () => repository.GetAllElements();
            staleRead.Should().Throw<InvalidOperationException>()
                .WithMessage("*not ready*");

            (await repository.RefreshAsync(CancellationToken.None)).Should().BePositive();
            repository.GetAllElements().Should().ContainSingle();
        }
        finally
        {
            await repository.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Session_expiration_activation_persists_cutoff_and_revokes_one_due_cohort()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider);
        UserSession expired = await scope.ServiceProvider.GetRequiredService<ISessionManager>()
            .CreateSessionAsync(userId, new SessionContext(
                "127.0.0.1", "expiration-test", Lifetime: TimeSpan.FromMinutes(-1)));

        BaseSession workerSession = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.expiration.test.v1",
        });
        BaseInstalledActivationHandle<AuthExpirationTriggerInputV1, AuthExpirationResultV1> activation =
            workerSession.Activations.Get(AuthLifecycleActivationDeclarations.Sessions.Identity);
        BaseMutationRequestFingerprint enqueueFingerprint = BaseMutationRequestFingerprint.Create(
            SHA256.HashData("hpd.auth.expiration.sessions.test.v1"u8));
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            new AuthExpirationTriggerInputV1
            {
                Kind = AuthMaintenanceKindV1.sessionExpiration,
                ContractVersion = 1,
            },
            BaseMutationRequestIdentity.Create(
                "hpd.auth.expiration.test", "session-expiration", "one", enqueueFingerprint));
        enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);

        OperationResult<BaseActivationDispatchResult> dispatched = await workerSession.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.Sessions.Identity)
            .RunOneAsync();
        dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
        dispatched.Value!.State.Should().Be(BaseActivationState.Succeeded);

        IReadOnlyList<UserSession> active = await scope.ServiceProvider
            .GetRequiredService<ISessionManager>()
            .GetActiveSessionsAsync(userId);
        active.Should().NotContain(session => session.Id == expired.Id);

        BaseResult<AuthMaintenanceRunReadV1.Row?> maintenance = await workerSession.Reads.FirstAsync(
            AuthMaintenanceRunReadV1.Handle,
            new AuthMaintenanceRunReadV1 { ActivationId = enqueued.Value!.ActivationId });
        maintenance.RequireValue().Should().NotBeNull();
        maintenance.RequireValue()!.Cutoff.Should().Be(Now);
    }

    [Fact]
    public async Task Issue_replays_the_same_bearer_for_the_same_identified_attempt()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider);
        IRefreshTokenStore store = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var request = new RefreshTokenIssueRequest
        {
            UserId = userId, SecurityStamp = "stamp-v1", ExpiresAt = Now.AddDays(14),
            RequestScope = "auth.test.issue", IdempotencyKey = "attempt-1",
        };

        RefreshTokenPersistenceResult first = await store.IssueAsync(request);
        RefreshTokenPersistenceResult replay = await store.IssueAsync(request);

        replay.Should().Be(first);
        first.Token.Should().StartWith("hpd1.1.");
        first.Token.Split('.')[2].Should().HaveLength(43);
    }

    [Fact]
    public async Task Reusing_identity_with_different_semantics_fails_closed()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider);
        IRefreshTokenStore store = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var first = new RefreshTokenIssueRequest
        {
            UserId = userId, SecurityStamp = "stamp-v1", ExpiresAt = Now.AddDays(14),
            RequestScope = "auth.test.issue", IdempotencyKey = "attempt-2",
        };
        await store.IssueAsync(first);

        Func<Task> act = () => store.IssueAsync(first with { ExpiresAt = Now.AddDays(15) });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rotation_consumes_the_predecessor_and_replays_the_replacement()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider);
        IRefreshTokenStore store = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        RefreshTokenPersistenceResult issued = await store.IssueAsync(new RefreshTokenIssueRequest
        {
            UserId = userId, SecurityStamp = "stamp-v1", ExpiresAt = Now.AddDays(14),
            RequestScope = "auth.test.issue", IdempotencyKey = "attempt-3",
        });
        var rotate = new RefreshTokenRotateRequest
        {
            PredecessorToken = issued.Token, SecurityStamp = "stamp-v1", ExpiresAt = Now.AddDays(14),
        };

        RefreshTokenPersistenceResult? first = await store.RotateAsync(rotate);
        RefreshTokenPersistenceResult? replay = await store.RotateAsync(rotate);

        first.Should().NotBeNull();
        replay.Should().Be(first);
        first!.Token.Should().NotBe(issued.Token);
        (await store.InspectAsync(issued.Token)).Should().BeNull();
        (await store.InspectAsync(first.Token)).Should().NotBeNull();
    }

    [Fact]
    public async Task Administrative_query_executes_bounded_search_and_exact_count_through_sqlite()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid expected = await CreateUserAsync(scope.ServiceProvider, "needle-user@example.invalid");
        _ = await CreateUserAsync(scope.ServiceProvider, "unrelated@example.invalid");
        IAuthAdminUserQuery query = scope.ServiceProvider.GetRequiredService<IAuthAdminUserQuery>();

        AuthAdminUserQueryResult result = await query.ExecuteAsync(new AuthAdminUserQuery
        {
            Search = "needle-user", Offset = 0, Limit = 10,
            Sort = AuthAdminUserSort.Email, Direction = AuthAdminSortDirection.Ascending,
        });

        result.Total.Should().Be(1);
        result.Users.Should().ContainSingle();
        result.Users[0].Id.Should().Be(expected);
        result.Users[0].Email.Should().Be("needle-user@example.invalid");
        result.Users[0].InstanceId.Should().Be(TenantId);
    }

    [Fact]
    public async Task User_cleanup_drains_multiple_real_cohorts_by_yielding_the_same_activation()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider, "cleanup-user@example.invalid");
        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.test.v1",
        });
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.test.acquire.v1",
        });
        BaseResult<AuthUserSubjectAcquisitionReadV1.Row?> acquired = await service.Reads.FirstAsync(
            AuthUserSubjectAcquisitionReadV1.Handle,
            new AuthUserSubjectAcquisitionReadV1
            {
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            });
        BaseSubjectReference<AuthUserSubject> subject = acquired.RequireValue()!.Reference;
        ISessionManager sessions = scope.ServiceProvider.GetRequiredService<ISessionManager>();
        for (int index = 0; index < 201; index++)
            _ = await sessions.CreateSessionAsync(userId, new SessionContext(
                "127.0.0.1", $"cleanup-cohort-{index}", Lifetime: TimeSpan.FromDays(1)));
        IUserStore<ApplicationUser> store = scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();
        ApplicationUser user = (await store.FindByIdAsync(userId.ToString("D"), CancellationToken.None))!;

        IdentityResult deleted = await store.DeleteAsync(user, CancellationToken.None);

        deleted.Succeeded.Should().BeTrue(string.Join(',', deleted.Errors.Select(static error => error.Code)));
        OperationResult<BaseActivationDispatchResult> dispatched = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.BootstrapUser.Identity)
            .RunOneAsync();
        dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
        dispatched.Value!.State.Should().Be(BaseActivationState.Succeeded);

        BaseResult<AuthCleanupWorkReadV1.Row?> work = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        work.RequireValue().Should().NotBeNull();
        work.RequireValue()!.State.Should().Be(AuthCleanupStateV1.draining);
        work.RequireValue()!.Step.Should().Be(AuthCleanupStepV1.revokeSessions);
        work.RequireValue()!.TombstoneSequence.Should().BeGreaterThan(0);

        BaseInstalledActivationWorkerHandle<AuthUserCleanupInputV1, AuthCleanupResultV1> cleanupWorker =
            system.Activations.GetWorker(AuthCleanupActivationDeclarations.User.Identity);
        OperationResult<BaseActivationDispatchResult> firstCleanup = await cleanupWorker.RunOneAsync();
        firstCleanup.IsSuccess().Should().BeTrue(firstCleanup.Error?.Code);
        firstCleanup.Value!.State.Should().Be(BaseActivationState.YieldPending);
        (await sessions.GetActiveSessionsAsync(userId)).Should().ContainSingle();

        OperationResult<BaseActivationDispatchResult> secondCleanup = await cleanupWorker.RunOneAsync();
        secondCleanup.IsSuccess().Should().BeTrue(secondCleanup.Error?.Code);
        secondCleanup.Value!.State.Should().Be(BaseActivationState.YieldPending);
        secondCleanup.Value.ActivationId.Should().Be(firstCleanup.Value.ActivationId);
        (await sessions.GetActiveSessionsAsync(userId)).Should().BeEmpty();

        OperationResult<BaseActivationDispatchResult> zeroDrainCleanup = await cleanupWorker.RunOneAsync();
        zeroDrainCleanup.IsSuccess().Should().BeTrue(zeroDrainCleanup.Error?.Code);
        zeroDrainCleanup.Value!.State.Should().Be(BaseActivationState.YieldPending);
        zeroDrainCleanup.Value.ActivationId.Should().Be(firstCleanup.Value.ActivationId);
        BaseResult<AuthCleanupWorkReadV1.Row?> advanced = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        advanced.RequireValue()!.Step.Should().Be(AuthCleanupStepV1.revokeRefreshTokens);
        advanced.RequireValue()!.CompletedSteps.Should().Be(1);
    }

    [Fact]
    public void Completed_cleanup_advance_resolves_as_terminal_success_instead_of_consuming_another_yield()
    {
        var result = new AuthCleanupResultV1
        {
            Completed = true,
            State = AuthCleanupStateV1.complete,
            Step = AuthCleanupStepV1.finalizeSubject,
            ChunkOrdinal = 42,
            SelectedCount = 0,
        };

        BaseActivationHandlerResult<AuthCleanupResultV1> outcome =
            AuthCleanupActivationHandler.ResolveAdvanceOutcome(
                new string('a', 64), 65_535, new RevisionToken("complete-revision"),
                AuthCleanupChildDispositionV1.allStepsComplete, result);

        outcome.Should().BeOfType<BaseActivationSucceeded<AuthCleanupResultV1>>()
            .Which.Result.Should().BeSameAs(result);
    }

    [Fact]
    public async Task Role_tombstone_bootstrap_creates_cleanup_work_and_semantic_activation()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        IRoleStore<ApplicationRole> store = scope.ServiceProvider.GetRequiredService<IRoleStore<ApplicationRole>>();
        var role = new ApplicationRole("cleanup-role")
        {
            Id = Guid.NewGuid(),
            NormalizedName = "CLEANUP-ROLE",
            ConcurrencyStamp = "cleanup-role-v1",
        };
        (await store.CreateAsync(role, CancellationToken.None)).Succeeded.Should().BeTrue();
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.role.test.acquire.v1",
        });
        BaseResult<AuthRoleSubjectAcquisitionReadV1.Row?> acquired = await service.Reads.FirstAsync(
            AuthRoleSubjectAcquisitionReadV1.Handle,
            new AuthRoleSubjectAcquisitionReadV1
            {
                RoleId = BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")),
            });
        BaseSubjectReference<AuthRoleSubject> subject = acquired.RequireValue()!.Reference;

        IdentityResult deleted = await store.DeleteAsync(role, CancellationToken.None);

        deleted.Succeeded.Should().BeTrue(string.Join(',', deleted.Errors.Select(static error => error.Code)));
        (await store.FindByIdAsync(role.Id.ToString("D"), CancellationToken.None)).Should().BeNull();
        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.role.test.v1",
        });
        OperationResult<BaseActivationDispatchResult> dispatched = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.BootstrapRole.Identity)
            .RunOneAsync();
        dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
        dispatched.Value!.State.Should().Be(BaseActivationState.Succeeded);
        BaseResult<AuthCleanupWorkReadV1.Row?> work = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.role,
                SubjectId = role.Id,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        work.RequireValue().Should().NotBeNull();
        work.RequireValue()!.Step.Should().Be(AuthCleanupStepV1.deleteRoleClaims);

        OperationResult<BaseActivationDispatchResult> cleanup = await system.Activations
            .GetWorker(AuthCleanupActivationDeclarations.Role.Identity)
            .RunOneAsync();
        cleanup.IsSuccess().Should().BeTrue(cleanup.Error?.Code);
        cleanup.Value!.State.Should().Be(BaseActivationState.YieldPending);
        BaseResult<AuthCleanupWorkReadV1.Row?> advanced = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.role,
                SubjectId = role.Id,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        advanced.RequireValue()!.Step.Should().Be(AuthCleanupStepV1.deleteUserRoles);
        advanced.RequireValue()!.CompletedSteps.Should().Be(1L << 14);
    }

    private static async Task<Guid> CreateUserAsync(
        IServiceProvider services,
        string email = "refresh@test.invalid")
    {
        Guid id = Guid.NewGuid();
        BaseSession session = services.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth", CurrentTenantId = TenantId.ToString("D"), AuthSource = "auth.tests",
        });
        var request = new AuthCreateUserV1
        {
            TenantId = TenantId, UserId = id, UserName = email,
            NormalizedUserName = email.ToUpperInvariant(), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = "stamp-v1",
            ConcurrencyStamp = "concurrency-v1", LockoutEnabled = true,
            EmailConfirmed = false, PhoneNumberConfirmed = false, TwoFactorEnabled = false,
            AccessFailedCount = 0, UserMetadata = CanonicalJson("{}"u8),
            AppMetadata = CanonicalJson("{}"u8), RequiredActions = CanonicalJson("[]"u8),
            IsActive = true, SubscriptionTier = "free", OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthCreateUserV1, AuthCreateUserResultV1> operation =
            session.ModuleMutations.Get(AuthCreateUserOperationV1.Identity);
        BaseResult<BaseModuleMutationExecutionResult<AuthCreateUserResultV1>> result = await operation.ExecuteAsync(
            request, operation.CreateRequestIdentity(request, $"user:{id:D}:create"));
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthCreateUserResultV1>> failure)
            throw new InvalidOperationException($"{failure.Error.Code}:{failure.Error.Category}:{failure.Status}");
        return id;
    }

    private static BaseCanonicalJson CanonicalJson(ReadOnlySpan<byte> json) =>
        BaseCanonicalJson.ParseAndValidate(json, new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 32_768,
            MaximumDepth = 16,
            MaximumTotalNodes = 4_096,
            MaximumTotalStringUtf8Bytes = 32_768,
            MaximumTotalNameUtf8Bytes = 32_768,
            MaximumArrayItemsPerContainer = 1_024,
            MaximumObjectPropertiesPerContainer = 1_024,
        });

    private static async Task InitializeAsync(IServiceProvider services)
    {
        IBaseSchemaManager schemas = services.GetRequiredService<IBaseSchemaManager>();
        OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest
        {
            StoreId = "auth-refresh-tests",
        });
        if (!planned.IsSuccess())
            throw new InvalidOperationException($"{planned.Error?.Code}:{planned.Error?.Category}:{planned.Status}");
        OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(new BaseSchemaApplyRequest
        {
            ProtectedArtifact = planned.Value!.ProtectedArtifact,
        });
        if (!applied.IsSuccess())
            throw new InvalidOperationException($"{applied.Error?.Code}:{applied.Error?.Category}:{applied.Status}");
        OperationResult<BaseApplicationReadiness> result = await services
            .GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        if (!result.IsSuccess())
            throw new InvalidOperationException($"{result.Error?.Code}:{result.Error?.Category}:{result.Status}");
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var database = new TestDatabase();
        services.AddSingleton(database);
        services.AddLogging();
        services.AddSingleton<IdentityErrorDescriber>();
        services.AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>();
        services.AddSingleton(new HPDAuthOptions { AppName = "HPD Auth Infrastructure Tests" });
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(TenantId));
        services.AddSingleton<IAuthRefreshTokenDigestKeyRing, TestDigestKeyRing>();
        services.AddSingleton<IAuthRecoveryCodeDigestKeyRing, TestRecoveryKeyRing>();
        services.AddSingleton<IAuthTokenDeliveryProtector, TestDeliveryProtector>();
        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(options =>
            {
                options.ApplicationId = "hpd.auth.identity.v1";
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
            });
            builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1, Key = Enumerable.Repeat((byte)0x41, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            });
            builder.UseStore(SqliteStore.Configure(options =>
            {
                options.DataSource = database.Path;
                options.StoreId = "auth-refresh-tests";
            }));
            builder.Use(new StorageProtectionExtension());
            builder.ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
            {
                HostMaxima = SelectionLimits(), MaximumReceiptIdentityBytes = 4_096,
                MaximumEvidenceTokenBytes = 4_096, MaximumRouteNameBytes = 128,
                MaximumRequestBodyBytes = 1_048_576,
            });
            AuthBaseModule.Install(builder, new AuthBaseModuleOptions
            {
                DataProtectionApplicationDiscriminatorDigest = BaseBinary.From(new byte[32]),
                StorageProtectionRequirement = StorageRequirement(),
            });
            builder.SetSemanticActivationRestoreSelection(new BaseSemanticActivationRestoreSelection
            {
                LogicalStoreId = "auth-refresh-tests",
                EnabledRestoreMode = BaseActivationRestoreMode.InPlaceRecovery,
                SelectionGeneration = 1,
                Identity = BaseMutationRequestIdentity.Create("auth.refresh.tests", "restore", "v1",
                    BaseMutationRequestFingerprint.Create(SHA256.HashData("auth.refresh.tests.restore.v1"u8))),
                Checksum = [],
            });
        });
        services.AddHPDAuthBaseStores();
        return services.BuildServiceProvider();
    }

    private static BaseSelectionOperationLimits SelectionLimits() => new()
    {
        MaximumQueryNodes = 24, MaximumQueryDepth = 8, MaximumLiteralValues = 32,
        MaximumSelectedRecords = 200, MaximumSelectedBytes = 1_048_576,
        MaximumProducedMutations = 200, MaximumQueryExecutions = 1,
        MaximumReadIntervals = 64, MaximumWrittenBytes = 1_048_576,
        MaximumFactBytes = 8_388_608, MaximumJournalBytes = 8_388_608,
        MaximumReceiptBytes = 8_388_608, MaximumRelationChecks = 400,
        MaximumUniqueConstraintChecks = 400, MaximumPreviousStateRequirements = 8,
        MaximumTransientBytes = 16_777_216, MaximumResultBytes = 32_768,
        AcquisitionTimeout = TimeSpan.FromSeconds(2), ExecutionTimeout = TimeSpan.FromSeconds(5),
        CallerCommitObservationTimeout = TimeSpan.FromSeconds(2),
    };

    private static BaseStorageProtectionRequirement StorageRequirement() => new()
    {
        OwningModuleId = "hpd.auth", PermittedGuarantees = [BaseStorageEncryptionGuarantee.ProviderDeclared],
        Coverage = new BaseStorageProtectionCoverageRequirement
        {
            AuthoritativeRecords = [BaseStorageProtectionState.Protected], Journal = [BaseStorageProtectionState.Protected],
            Receipts = [BaseStorageProtectionState.Protected], ProviderState = [BaseStorageProtectionState.Protected],
            Indexes = [BaseStorageProtectionState.Protected], TemporaryFiles = [BaseStorageProtectionState.Protected],
            AuthoritativeBackups = [BaseStorageProtectionState.Protected], AdministrativeExports = [BaseStorageProtectionState.Protected],
            OrdinaryExports = [BaseStorageProtectionState.NotRetained], ExternalFilesAndBlobs = [BaseStorageProtectionState.NotApplicable],
        },
        PermittedKeyOwners = [BaseStorageKeyOwner.Provider], RequiredRotation = BaseStorageRotationSupport.Online,
        MinimumVerification = BaseStorageVerificationStatus.ConfigurationValidated,
    };

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext { public Guid InstanceId => tenantId; }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class TestDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"hpd-auth-refresh-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }

    private sealed class TestDigestKeyRing : IAuthRefreshTokenDigestKeyRing
    {
        private static readonly byte[] Key = Enumerable.Repeat((byte)0x73, 32).ToArray();
        public AuthRefreshDigestKeyRingCapability Capability { get; } = new()
        {
            ModuleId = "auth.refresh.tests.keys", ActiveIssuanceVersion = 1,
            ValidationVersions = [1], Ownership = AuthDigestKeyOwnership.Host,
            IsReady = true, LastVerifiedAt = Now,
        };
        public AuthAuthorityResult<AuthRefreshDigestKey> GetActiveIssuanceKey() => GetValidationKey(1);
        public AuthAuthorityResult<AuthRefreshDigestKey> GetValidationKey(int version) =>
            version == 1
                ? AuthAuthorityResult<AuthRefreshDigestKey>.Available(new AuthRefreshDigestKey
            {
                Version = version, KeyMaterial = AuthOwnedSecretBytes.From(Key),
            })
                : AuthAuthorityResult<AuthRefreshDigestKey>.Unavailable();
    }

    private sealed class TestRecoveryKeyRing : IAuthRecoveryCodeDigestKeyRing
    {
        private static readonly byte[] Key = Enumerable.Repeat((byte)0x37, 32).ToArray();
        public AuthRecoveryCodeDigestKeyRingCapability Capability { get; } = new()
        {
            ModuleId = "auth.recovery.tests.keys", ActiveIssuanceVersion = 1,
            ValidationVersions = [1], Ownership = AuthDigestKeyOwnership.Host,
            IsReady = true, LastVerifiedAt = Now,
        };
        public AuthAuthorityResult<AuthRecoveryCodeDigestKey> GetActiveIssuanceKey() => GetValidationKey(1);
        public AuthAuthorityResult<AuthRecoveryCodeDigestKey> GetValidationKey(int version) =>
            version == 1
                ? AuthAuthorityResult<AuthRecoveryCodeDigestKey>.Available(new AuthRecoveryCodeDigestKey
            {
                Version = version, KeyMaterial = AuthOwnedSecretBytes.From(Key),
            })
                : AuthAuthorityResult<AuthRecoveryCodeDigestKey>.Unavailable();
    }

    private sealed class TestDeliveryProtector : IAuthTokenDeliveryProtector
    {
        private static readonly byte[] Key = Enumerable.Repeat((byte)0x51, 32).ToArray();
        public AuthTokenDeliveryProtectorCapability Capability { get; } = new()
        {
            ModuleId = "auth.refresh.tests.protector", ActiveVersion = 1, ValidationVersions = [1],
            Ownership = AuthDigestKeyOwnership.Host, AuthenticatedEncryption = true,
            SupportsRotation = true, IsReady = true, LastVerifiedAt = Now,
        };
        public AuthAuthorityResult<AuthProtectedTokenEnvelope> Protect(AuthOwnedSecretBytes plaintext, AuthOwnedEnvelopeBytes associatedData)
        {
            byte[] clear = new byte[plaintext.Length];
            plaintext.CopyTo(clear);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[clear.Length];
            byte[] tag = new byte[16];
            try
            {
                using var aes = new AesGcm(Key, tag.Length);
                aes.Encrypt(nonce, clear, ciphertext, tag, associatedData.ToArray());
                return AuthAuthorityResult<AuthProtectedTokenEnvelope>.Available(new AuthProtectedTokenEnvelope
                {
                    ProtectorVersion = 1,
                    Ciphertext = AuthOwnedEnvelopeBytes.From(nonce.Concat(tag).Concat(ciphertext).ToArray()),
                });
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        public AuthAuthorityResult<AuthOwnedSecretBytes> Unprotect(int protectorVersion, AuthOwnedEnvelopeBytes ciphertext, AuthOwnedEnvelopeBytes associatedData)
        {
            byte[] envelope = ciphertext.ToArray();
            if (protectorVersion != 1 || envelope.Length < 29)
                return AuthAuthorityResult<AuthOwnedSecretBytes>.Unavailable();
            byte[] clear = new byte[envelope.Length - 28];
            try
            {
                using var aes = new AesGcm(Key, 16);
                aes.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), clear, associatedData.ToArray());
                return AuthAuthorityResult<AuthOwnedSecretBytes>.Available(AuthOwnedSecretBytes.From(clear));
            }
            catch (CryptographicException)
            {
                return AuthAuthorityResult<AuthOwnedSecretBytes>.Unavailable();
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
    }

    private sealed class StorageProtectionExtension : IHPDBaseBuilderExtension
    {
        public string Id => "auth.refresh.tests.storage";
        public ImmutableArray<BaseStorageProtectionCapability> StorageProtectionCapabilities =>
        [new BaseStorageProtectionCapability
        {
            OwningModuleId = "hpd.auth", Guarantee = BaseStorageEncryptionGuarantee.ProviderDeclared,
            Coverage = new BaseStorageProtectionCoverage
            {
                AuthoritativeRecords = BaseStorageProtectionState.Protected, Journal = BaseStorageProtectionState.Protected,
                Receipts = BaseStorageProtectionState.Protected, ProviderState = BaseStorageProtectionState.Protected,
                Indexes = BaseStorageProtectionState.Protected, TemporaryFiles = BaseStorageProtectionState.Protected,
                AuthoritativeBackups = BaseStorageProtectionState.Protected, AdministrativeExports = BaseStorageProtectionState.Protected,
                OrdinaryExports = BaseStorageProtectionState.NotRetained, ExternalFilesAndBlobs = BaseStorageProtectionState.NotApplicable,
            },
            KeyOwner = BaseStorageKeyOwner.Provider, Rotation = BaseStorageRotationSupport.Online,
            Verification = BaseStorageVerificationStatus.ConfigurationValidated,
        }];
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) { }
    }
}
