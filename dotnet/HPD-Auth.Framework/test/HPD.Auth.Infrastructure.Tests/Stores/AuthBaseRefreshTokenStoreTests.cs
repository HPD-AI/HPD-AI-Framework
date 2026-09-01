using System.Collections.Immutable;
using System.Globalization;
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
using HPD.Base.Testing;
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
    public async Task Data_protection_repository_becomes_unready_immediately_after_restore()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        HPDBaseDataProtectionXmlRepository repository = provider
            .GetRequiredService<HPDBaseDataProtectionXmlRepository>();
        await repository.StartAsync(CancellationToken.None);
        try
        {
            repository.StoreElement(XElement.Parse("<key id=\"restore\" />"), "key-restore");
            repository.GetAllElements().Should().ContainSingle();

            var administrator = new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "hpd.auth",
                AuthSource = "hpd.auth.data-protection.restore-test.v1",
            };
            IHPDBaseAdministration administration = provider
                .GetRequiredService<IHPDBaseApplication>().Administration;
            using var artifact = new MemoryStream();
            BaseBackupManifest manifest = (await administration.CreateBackupAsync(
                artifact,
                new BaseBackupRequest
                {
                    StoreId = "auth-refresh-tests",
                    Principal = administrator,
                })).RequireValue();
            artifact.Position = 0;
            BaseRestoreResult restored = (await administration.RestoreAsync(
                artifact,
                new BaseRestoreRequest
                {
                    StoreId = "auth-refresh-tests",
                    Principal = administrator,
                    ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                    ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
                })).RequireValue();
            restored.RestoreEpoch.Should().Be(manifest.RestoreEpoch + 1);

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
    public async Task Data_protection_repository_rebinds_after_provider_replacement()
    {
        await using ServiceProvider sourceProvider = CreateProvider();
        await InitializeAsync(sourceProvider);
        HPDBaseDataProtectionXmlRepository sourceRepository = sourceProvider
            .GetRequiredService<HPDBaseDataProtectionXmlRepository>();
        await sourceRepository.StartAsync(CancellationToken.None);
        try
        {
            sourceRepository.StoreElement(
                XElement.Parse("<key id=\"provider-replacement\" />"),
                "key-provider-replacement");

            var administrator = new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "hpd.auth",
                AuthSource = "hpd.auth.data-protection.provider-replacement-test.v1",
            };
            using var artifact = new MemoryStream();
            BaseBackupManifest sourceManifest = (await sourceProvider
                .GetRequiredService<IHPDBaseApplication>().Administration.CreateBackupAsync(
                    artifact,
                    new BaseBackupRequest
                    {
                        StoreId = "auth-refresh-tests",
                        Principal = administrator,
                    })).RequireValue();

            await using ServiceProvider replacementProvider = CreateProvider();
            await InitializeAsync(replacementProvider);
            HPDBaseDataProtectionXmlRepository replacementRepository = replacementProvider
                .GetRequiredService<HPDBaseDataProtectionXmlRepository>();
            await replacementRepository.StartAsync(CancellationToken.None);
            try
            {
                replacementRepository.GetAllElements().Should().BeEmpty();
                using var replacementSnapshot = new MemoryStream();
                BaseBackupManifest replacementManifest = (await replacementProvider
                    .GetRequiredService<IHPDBaseApplication>().Administration.CreateBackupAsync(
                        replacementSnapshot,
                        new BaseBackupRequest
                        {
                            StoreId = "auth-refresh-tests",
                            Principal = administrator,
                        })).RequireValue();
                replacementManifest.StoreIdentityDigest.Should().NotBe(sourceManifest.StoreIdentityDigest);

                artifact.Position = 0;
                BaseRestoreResult restored = (await replacementProvider
                    .GetRequiredService<IHPDBaseApplication>().Administration.RestoreAsync(
                        artifact,
                        new BaseRestoreRequest
                        {
                            StoreId = "auth-refresh-tests",
                            Principal = administrator,
                            ExpectedCurrentStoreIdentityDigest = replacementManifest.StoreIdentityDigest,
                            ExpectedArtifactStoreIdentityDigest = sourceManifest.StoreIdentityDigest,
                            IdentityMode = BaseRestoreIdentityMode.AdoptArtifactStoreIdentity,
                            RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                            ConfirmDestructiveReplacement = true,
                            ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
                        })).RequireValue();
                restored.InstalledStoreIdentityDigest.Should().Be(sourceManifest.StoreIdentityDigest);

                Action staleRead = () => replacementRepository.GetAllElements();
                staleRead.Should().Throw<InvalidOperationException>().WithMessage("*not ready*");

                (await replacementRepository.RefreshAsync(CancellationToken.None)).Should().BePositive();
                replacementRepository.GetAllElements().Should().ContainSingle(element =>
                    element.Attribute("id")!.Value == "provider-replacement");
            }
            finally
            {
                await replacementRepository.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await sourceRepository.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("before-slot")]
    [InlineData("live")]
    [InlineData("retired")]
    public async Task Auth_cleanup_semantic_identity_survives_each_backup_restore_state(
        string semanticState)
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        var administrator = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.semantic.backup-restore.v1",
        };
        IHPDBaseAdministration administration = provider
            .GetRequiredService<IHPDBaseApplication>().Administration;

        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(administrator);
        if (semanticState is "live" or "retired")
        {
            using IServiceScope scope = provider.CreateScope();
            Guid userId = await CreateUserAsync(scope.ServiceProvider,
                $"semantic-backup-{semanticState}@example.invalid");
            IUserStore<ApplicationUser> store = scope.ServiceProvider
                .GetRequiredService<IUserStore<ApplicationUser>>();
            ApplicationUser user = (await store.FindByIdAsync(
                userId.ToString("D"), CancellationToken.None))!;
            (await store.DeleteAsync(user, CancellationToken.None)).Succeeded.Should().BeTrue();
            OperationResult<BaseActivationDispatchResult> bootstrapped = await system.Activations
                .GetWorker(AuthLifecycleActivationDeclarations.BootstrapUser.Identity).RunOneAsync();
            bootstrapped.IsSuccess().Should().BeTrue(bootstrapped.Error?.Code);
            bootstrapped.Value!.State.Should().Be(BaseActivationState.Succeeded);

            if (semanticState == "retired")
            {
                BaseInstalledActivationWorkerHandle<AuthUserCleanupInputV1, AuthCleanupResultV1>
                    cleanup = system.Activations.GetWorker(
                        AuthCleanupActivationDeclarations.User.Identity);
                BaseActivationState state;
                do
                {
                    OperationResult<BaseActivationDispatchResult> dispatched =
                        await cleanup.RunOneAsync();
                    dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
                    state = dispatched.Value!.State!.Value;
                }
                while (state == BaseActivationState.YieldPending);
                state.Should().Be(BaseActivationState.Succeeded);
                provider.GetRequiredService<MutableTimeProvider>()
                    .Advance(TimeSpan.FromDays(31));
                do
                {
                    OperationResult<BaseActivationDispatchResult> dispatched =
                        await cleanup.RunOneAsync();
                    dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
                    state = dispatched.Value!.State!.Value;
                }
                while (state == BaseActivationState.YieldPending);
                state.Should().Be(BaseActivationState.Succeeded);
                OperationResult<BaseActivationDispatchResult> semanticRetirement =
                    await system.Activations.GetWorker(
                        AuthLifecycleActivationDeclarations.RetireUser.Identity).RunOneAsync();
                semanticRetirement.IsSuccess().Should().BeTrue(semanticRetirement.Error?.Code);
                semanticRetirement.Value!.State.Should().Be(BaseActivationState.Succeeded);
            }
        }

        long expectedLive = semanticState == "live" ? 1 : 0;
        long expectedRetired = semanticState == "retired" ? 1 : 0;
        BaseSemanticActivationControlDescriptor beforeRestore =
            await ReadUserSemanticControlAsync(administration, administrator);
        beforeRestore.LiveCount.Should().Be(expectedLive);
        beforeRestore.RetiredCount.Should().Be(expectedRetired);
        long authorityGeneration = beforeRestore.AuthorityGeneration!.Value;
        using var artifact = new MemoryStream();
        BaseBackupManifest manifest = (await administration.CreateBackupAsync(
            artifact, new BaseBackupRequest
            {
                StoreId = "auth-refresh-tests",
                Principal = administrator,
            })).RequireValue();

        await RestoreAsync(administration, administrator, artifact, manifest);
        BaseSemanticActivationControlDescriptor restored =
            await ReadUserSemanticControlAsync(administration, administrator);
        restored.LiveCount.Should().Be(expectedLive);
        restored.RetiredCount.Should().Be(expectedRetired);
        restored.AuthorityGeneration.Should().BeGreaterThan(authorityGeneration,
            "restore must rebind current authority without changing semantic identity");
    }

    [Fact]
    public async Task Data_protection_repository_timeout_fails_call_and_invalidates_cache()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HPDBaseDataProtectionXmlRepository repository = CreateDataProtectionRepository(
            provider, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(2),
            async (_, _, _) => await release.Task.ConfigureAwait(false));
        await repository.StartAsync(CancellationToken.None);
        try
        {
            Action store = () => repository.StoreElement(
                XElement.Parse("<key id=\"timeout\" />"), "key-timeout");

            store.Should().Throw<InvalidOperationException>()
                .WithMessage("*timed out*");
            repository.Invoking(static value => value.GetAllElements())
                .Should().Throw<InvalidOperationException>()
                .WithMessage("*not ready*");
        }
        finally
        {
            release.TrySetResult();
            await repository.StopAsync(CancellationToken.None);
            repository.Dispose();
        }
    }

    [Fact]
    public async Task Data_protection_receipt_resolution_owns_bytes_independently_of_late_writer()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolverEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ReadOnlyMemory<byte> writerMemory = default;
        byte[]? expected = null;
        int invocation = 0;
        HPDBaseDataProtectionXmlRepository repository = CreateDataProtectionRepository(
            provider, TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(2),
            async (_, canonicalXml, _) =>
            {
                int current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    writerMemory = canonicalXml;
                    expected = canonicalXml.ToArray();
                    writerEntered.TrySetResult();
                    await releaseWriter.Task.ConfigureAwait(false);
                    return;
                }

                resolverEntered.TrySetResult();
                DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
                while (writerMemory.Span.ToArray().Any(static value => value != 0))
                {
                    if (DateTimeOffset.UtcNow >= deadline)
                        throw new TimeoutException("The writer-owned bytes were not cleared.");
                    await Task.Delay(1).ConfigureAwait(false);
                }

                canonicalXml.Span.SequenceEqual(expected).Should().BeTrue(
                    "receipt resolution must not share the writer-owned buffer that is cleared after use");
            });
        await repository.StartAsync(CancellationToken.None);
        try
        {
            Task store = Task.Run(() => repository.StoreElement(
                XElement.Parse("<key id=\"owned-timeout\" />"), "key-owned-timeout"));
            await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await resolverEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            releaseWriter.TrySetResult();
            await store.WaitAsync(TimeSpan.FromSeconds(2));
            invocation.Should().Be(2);
        }
        finally
        {
            releaseWriter.TrySetResult();
            await repository.StopAsync(CancellationToken.None);
            repository.Dispose();
        }
    }

    [Fact]
    public async Task Data_protection_late_resolver_owns_bytes_beyond_caller_timeout()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolverEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResolver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ReadOnlyMemory<byte> resolverMemory = default;
        byte[]? expected = null;
        int invocation = 0;
        HPDBaseDataProtectionXmlRepository repository = CreateDataProtectionRepository(
            provider, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2),
            async (_, canonicalXml, _) =>
            {
                int current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    writerEntered.TrySetResult();
                    await releaseWriter.Task.ConfigureAwait(false);
                    return;
                }

                resolverMemory = canonicalXml;
                expected = canonicalXml.ToArray();
                resolverEntered.TrySetResult();
                await releaseResolver.Task.ConfigureAwait(false);
            });
        await repository.StartAsync(CancellationToken.None);
        try
        {
            Task<Action> store = Task.Run(() => (Action)(() => repository.StoreElement(
                XElement.Parse("<key id=\"late-resolver\" />"), "key-late-resolver")));
            Action storeAction = await store;
            Task assertion = Task.Run(() => storeAction.Should().Throw<InvalidOperationException>()
                .WithMessage("*timed out*"));
            await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await resolverEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await assertion.WaitAsync(TimeSpan.FromSeconds(2));

            resolverMemory.Span.SequenceEqual(expected).Should().BeTrue(
                "the resolver still owns and may read its bytes after the caller stops waiting");
            releaseResolver.TrySetResult();
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (resolverMemory.Span.ToArray().Any(static value => value != 0))
            {
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new TimeoutException("The completed resolver did not clear its owned bytes.");
                await Task.Delay(1);
            }
        }
        finally
        {
            releaseWriter.TrySetResult();
            releaseResolver.TrySetResult();
            await repository.StopAsync(CancellationToken.None);
            repository.Dispose();
        }
    }

    [Fact]
    public async Task Data_protection_repository_queue_is_bounded_and_overflow_fails_immediately()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HPDBaseDataProtectionXmlRepository repository = CreateDataProtectionRepository(
            provider, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2),
            async (_, _, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            });
        await repository.StartAsync(CancellationToken.None);
        try
        {
            Task<Exception?>[] writes = Enumerable.Range(0, 40).Select(index => Task.Run(() =>
            {
                try
                {
                    repository.StoreElement(
                        XElement.Parse($"<key id=\"queue-{index}\" />"), $"key-queue-{index}");
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            })).ToArray();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(100);

            writes.Where(static write => write.IsCompletedSuccessfully)
                .Select(static write => write.Result)
                .Should().Contain(exception => exception is InvalidOperationException
                    && exception.Message.Contains("capacity is exhausted", StringComparison.Ordinal));

            release.TrySetResult();
            Exception?[] outcomes = await Task.WhenAll(writes);
            outcomes.Should().Contain(exception => exception is InvalidOperationException
                && exception.Message.Contains("capacity is exhausted", StringComparison.Ordinal));
        }
        finally
        {
            release.TrySetResult();
            await repository.StopAsync(CancellationToken.None);
            repository.Dispose();
        }
    }

    [Fact]
    public async Task Data_protection_repository_shutdown_fails_when_accepted_write_does_not_drain()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HPDBaseDataProtectionXmlRepository repository = CreateDataProtectionRepository(
            provider, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50),
            async (_, _, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            });
        await repository.StartAsync(CancellationToken.None);
        Task store = Task.Run(() => repository.StoreElement(
            XElement.Parse("<key id=\"shutdown\" />"), "key-shutdown"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Func<Task> stop = () => repository.StopAsync(CancellationToken.None);
            await stop.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*did not drain*");
        }
        finally
        {
            release.TrySetResult();
            await store;
            repository.Dispose();
        }
    }

    [Fact]
    public async Task Data_protection_repository_resolves_response_loss_through_identified_receipt()
    {
        string database = Path.Combine(
            Path.GetTempPath(), $"hpd-auth-dp-receipt-{Guid.NewGuid():N}.db");
        try
        {
            await using BaseTestHost host = await BaseTestHost.CreateAsync(builder =>
            {
                builder.ConfigureSchema(options =>
                {
                    options.ApplicationId = "hpd.auth.identity.v1";
                    options.PlanProtectionKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
                });
                builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1,
                    Key = Enumerable.Repeat((byte)0x41, 32).ToArray(),
                    IssueNotBefore = DateTimeOffset.UnixEpoch,
                });
                builder.UseStore(SqliteStore.Configure(options =>
                {
                    options.StoreId = "sqlite";
                    options.DataSource = database;
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
                    LogicalStoreId = "sqlite",
                    EnabledRestoreMode = BaseActivationRestoreMode.InPlaceRecovery,
                    SelectionGeneration = 1,
                    Identity = BaseMutationRequestIdentity.Create(
                        "hpd.auth.data-protection.receipt-proof", "restore", "v1",
                        BaseMutationRequestFingerprint.Create(SHA256.HashData(
                            "hpd.auth.data-protection.receipt-proof.restore.v1"u8))),
                    Checksum = [],
                });
            });
            var invalidation = new AuthDataProtectionCacheInvalidationState();
            using var repository = new HPDBaseDataProtectionXmlRepository(
                host.GetRequiredService<IBaseSessionFactory>(),
                new HPDAuthOptions { AppName = "HPD Auth Receipt Resolution Tests" },
                host.Time,
                invalidation);
            await repository.StartAsync(CancellationToken.None);
            try
            {
                host.Faults.MakeNextAtomicCommitIndeterminate();

                repository.StoreElement(
                    XElement.Parse("<key id=\"response-loss\" />"), "key-response-loss");

                repository.GetAllElements().Should().ContainSingle()
                    .Which.Attribute("id")!.Value.Should().Be("response-loss");
            }
            finally
            {
                await repository.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            File.Delete(database);
            File.Delete(database + "-shm");
            File.Delete(database + "-wal");
        }
    }

    [Fact]
    public async Task Tombstone_and_cleanup_bootstrap_resolve_response_loss_without_duplicate_lifetimes()
    {
        string database = Path.Combine(
            Path.GetTempPath(), $"hpd-auth-cleanup-response-loss-{Guid.NewGuid():N}.db");
        try
        {
            await using BaseTestHost host = await CreateFaultInjectableAuthHostAsync(database);
            BaseSession service = host.Session(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Service,
                SubjectKind = AccessSubjectKind.ServicePrincipal,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.cleanup.response-loss.service.v1",
            });
            BaseSession system = host.Session(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.cleanup.response-loss.system.v1",
            });
            Guid userId = Guid.NewGuid();
            AuthCreateUserResultV1 created = await CreateUserThroughBaseAsync(service, userId,
                "cleanup-response-loss@example.invalid");
            AuthUserSubjectAcquisitionReadV1.Row acquired = (await service.Reads.FirstAsync(
                AuthUserSubjectAcquisitionReadV1.Handle,
                new AuthUserSubjectAcquisitionReadV1
                {
                    UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
                })).RequireValue()!;
            BaseExportedSubjectContract<AuthUserSubject> subjects = AuthSubjects.Users(service);
            BaseMutationRequestIdentity tombstoneIdentity = AuthBaseRuntime.MutationIdentity(
                "hpd.auth.user-subject.tombstone.v1", TenantId, userId.ToString("D"),
                created.Revision.Value, acquired.Reference.Incarnation.ToBase64Url());
            var tombstoneRequest = new BaseSubjectTombstoneRequest<AuthUserSubject>
            {
                Subject = acquired.Reference,
                ExpectedPrivateRevision = created.Revision,
                Identity = tombstoneIdentity,
            };

            host.Faults.MakeNextAtomicCommitIndeterminate();
            BaseResult<BaseSubjectTombstoneResult<AuthUserSubject>> responseLost =
                await subjects.TombstoneAsync(tombstoneRequest);
            responseLost.Status.Should().Be(OperationStatus.StoreError);
            BaseSubjectTombstoneResult<AuthUserSubject> tombstone =
                (await subjects.TombstoneAsync(tombstoneRequest)).RequireValue();
            BaseSubjectTombstoneResult<AuthUserSubject> duplicate =
                (await subjects.TombstoneAsync(tombstoneRequest)).RequireValue();
            duplicate.Fact.Fact.SubjectSequence.Should().Be(tombstone.Fact.Fact.SubjectSequence);
            duplicate.PrivateRevision.Should().Be(tombstone.PrivateRevision);

            string cleanupWorkId = AuthBaseDeterministicId.CreateCleanupWork(
                TenantId, "user", userId, subjects, acquired.Reference.Incarnation,
                tombstone.Fact.Fact.SubjectSequence);
            var initialize = new AuthUserCleanupInitializeV1
            {
                CleanupWorkId = cleanupWorkId,
                TenantId = TenantId,
                SubjectId = userId,
                Subject = acquired.Reference,
                Incarnation = acquired.Reference.Incarnation,
                TombstoneSequence = tombstone.Fact.Fact.SubjectSequence,
                TombstoneRevision = tombstone.PrivateRevision.Value,
                WorkflowVersion = 1,
                TombstonedAt = tombstone.TombstonedAt,
                RetirementReceiptScope = "auth.cleanup.initialize",
                OperationTime = tombstone.TombstonedAt,
            };
            BaseInstalledActivationHandle<AuthUserCleanupInitializeV1, AuthCleanupInitializeResultV1>
                bootstrap = system.Activations.Get(
                    AuthLifecycleActivationDeclarations.BootstrapUser.Identity);
            BaseMutationRequestIdentity bootstrapIdentity = AuthBaseRuntime.MutationIdentity(
                "hpd.auth.cleanup.bootstrap.user.v1", TenantId, cleanupWorkId,
                tombstone.PrivateRevision.Value,
                tombstone.Fact.Fact.SubjectSequence.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            host.Faults.MakeNextAtomicCommitIndeterminate();
            OperationResult<BaseActivationEnqueueResult> enqueueResponseLost = await bootstrap.EnqueueAsync(
                initialize, bootstrapIdentity);
            enqueueResponseLost.Status.Should().Be(OperationStatus.StoreError);
            OperationResult<BaseActivationEnqueueResult> enqueued = await bootstrap.EnqueueAsync(
                initialize, bootstrapIdentity);
            enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);
            OperationResult<BaseActivationEnqueueResult> duplicateEnqueue = await bootstrap.EnqueueAsync(
                initialize, bootstrapIdentity);
            duplicateEnqueue.IsSuccess().Should().BeTrue(duplicateEnqueue.Error?.Code);
            duplicateEnqueue.Value!.ActivationId.Should().Be(enqueued.Value!.ActivationId);

            OperationResult<BaseActivationDispatchResult> dispatched = await system.Activations
                .GetWorker(AuthLifecycleActivationDeclarations.BootstrapUser.Identity)
                .RunOneAsync();
            dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
            dispatched.Value!.State.Should().Be(BaseActivationState.Succeeded);
            AuthCleanupWorkReadV1.Row work = (await system.Reads.FirstAsync(
                AuthCleanupWorkReadV1.Handle,
                new AuthCleanupWorkReadV1
                {
                    TenantId = TenantId,
                    SubjectKind = AuthCleanupSubjectKindV1.user,
                    SubjectId = userId,
                    Incarnation = BaseBinary.From(acquired.Reference.Incarnation.ToArray()),
                })).RequireValue()!;
            work.Id.Should().Be(cleanupWorkId);
            work.TombstoneSequence.Should().Be(tombstone.Fact.Fact.SubjectSequence);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
            File.Delete(database + "-shm");
            File.Delete(database + "-wal");
        }
    }

    [Fact]
    public async Task Retention_transition_resolves_response_loss_without_duplicate_continuations()
    {
        string database = Path.Combine(
            Path.GetTempPath(), $"hpd-auth-retention-response-loss-{Guid.NewGuid():N}.db");
        try
        {
            await using BaseTestHost host = await CreateFaultInjectableAuthHostAsync(database);
            BaseSession service = host.Session(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Service,
                SubjectKind = AccessSubjectKind.ServicePrincipal,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.cleanup.retention-response-loss.service.v1",
            });
            BaseSession system = host.Session(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.cleanup.retention-response-loss.system.v1",
            });
            Guid userId = Guid.NewGuid();
            AuthCreateUserResultV1 created = await CreateUserThroughBaseAsync(
                service, userId, "retention-response-loss@example.invalid");
            AuthUserSubjectAcquisitionReadV1.Row acquired = (await service.Reads.FirstAsync(
                AuthUserSubjectAcquisitionReadV1.Handle,
                new AuthUserSubjectAcquisitionReadV1
                {
                    UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
                })).RequireValue()!;
            BaseExportedSubjectContract<AuthUserSubject> subjects = AuthSubjects.Users(service);
            BaseSubjectTombstoneResult<AuthUserSubject> tombstone = (await subjects.TombstoneAsync(
                new BaseSubjectTombstoneRequest<AuthUserSubject>
                {
                    Subject = acquired.Reference,
                    ExpectedPrivateRevision = created.Revision,
                    Identity = AuthBaseRuntime.MutationIdentity(
                        "hpd.auth.user-subject.tombstone.v1", TenantId, userId.ToString("D"),
                        created.Revision.Value, acquired.Reference.Incarnation.ToBase64Url()),
                })).RequireValue();
            string cleanupWorkId = AuthBaseDeterministicId.CreateCleanupWork(
                TenantId, "user", userId, subjects, acquired.Reference.Incarnation,
                tombstone.Fact.Fact.SubjectSequence);
            var initialize = new AuthUserCleanupInitializeV1
            {
                CleanupWorkId = cleanupWorkId,
                TenantId = TenantId,
                SubjectId = userId,
                Subject = acquired.Reference,
                Incarnation = acquired.Reference.Incarnation,
                TombstoneSequence = tombstone.Fact.Fact.SubjectSequence,
                TombstoneRevision = tombstone.PrivateRevision.Value,
                WorkflowVersion = 1,
                TombstonedAt = tombstone.TombstonedAt,
                RetirementReceiptScope = "auth.cleanup.initialize",
                OperationTime = tombstone.TombstonedAt,
            };
            BaseInstalledActivationHandle<AuthUserCleanupInitializeV1, AuthCleanupInitializeResultV1>
                bootstrap = system.Activations.Get(
                    AuthLifecycleActivationDeclarations.BootstrapUser.Identity);
            OperationResult<BaseActivationEnqueueResult> enqueued = await bootstrap.EnqueueAsync(
                initialize,
                AuthBaseRuntime.MutationIdentity(
                    "hpd.auth.cleanup.bootstrap.user.v1", TenantId, cleanupWorkId,
                    tombstone.PrivateRevision.Value,
                    tombstone.Fact.Fact.SubjectSequence.ToString(CultureInfo.InvariantCulture)));
            enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);
            (await system.Activations.GetWorker(
                AuthLifecycleActivationDeclarations.BootstrapUser.Identity).RunOneAsync())
                .Value!.State.Should().Be(BaseActivationState.Succeeded);

            BaseInstalledActivationWorkerHandle<AuthUserCleanupInputV1, AuthCleanupResultV1> cleanup =
                system.Activations.GetWorker(AuthCleanupActivationDeclarations.User.Identity);
            AuthCleanupWorkReadV1.Row work;
            do
            {
                OperationResult<BaseActivationDispatchResult> dispatched = await cleanup.RunOneAsync();
                dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
                work = (await system.Reads.FirstAsync(
                    AuthCleanupWorkReadV1.Handle,
                    new AuthCleanupWorkReadV1
                    {
                        TenantId = TenantId,
                        SubjectKind = AuthCleanupSubjectKindV1.user,
                        SubjectId = userId,
                        Incarnation = BaseBinary.From(acquired.Reference.Incarnation.ToArray()),
                    })).RequireValue()!;
            }
            while (work.Step != AuthCleanupStepV1.waitSecurityRetention);

            RevisionToken beforeTransition = work.Revision;
            host.Faults.MakeNextAtomicCommitIndeterminate();
            OperationResult<BaseActivationDispatchResult> responseLost = await cleanup.RunOneAsync();
            responseLost.IsSuccess().Should().BeTrue(responseLost.Error?.Code);
            responseLost.Value!.State.Should().Be(BaseActivationState.Exhausted,
                "L51 must not automatically retry an indeterminate handler outcome even when the child commit is later observable");
            AuthCleanupWorkReadV1.Row waiting = (await system.Reads.FirstAsync(
                AuthCleanupWorkReadV1.Handle,
                new AuthCleanupWorkReadV1
                {
                    TenantId = TenantId,
                    SubjectKind = AuthCleanupSubjectKindV1.user,
                    SubjectId = userId,
                    Incarnation = BaseBinary.From(acquired.Reference.Incarnation.ToArray()),
                })).RequireValue()!;
            waiting.State.Should().Be(AuthCleanupStateV1.waitingRetention);
            waiting.Step.Should().Be(AuthCleanupStepV1.waitSecurityRetention);
            waiting.Revision.Should().NotBe(beforeTransition);
            waiting.RetentionEligibleAt.Should().Be(tombstone.TombstonedAt.AddDays(30));

            OperationResult<BaseActivationDispatchResult> noDuplicate = await cleanup.RunOneAsync();
            noDuplicate.Value!.Empty.Should().BeTrue(
                "the parent activation is terminal and only its one identified continuation remains due");
            host.Time.Advance(TimeSpan.FromDays(31));
            BaseActivationState continuationState;
            do
            {
                OperationResult<BaseActivationDispatchResult> continuation = await cleanup.RunOneAsync();
                continuation.IsSuccess().Should().BeTrue(continuation.Error?.Code);
                continuation.Value!.Empty.Should().BeFalse();
                continuationState = continuation.Value.State!.Value;
            }
            while (continuationState == BaseActivationState.YieldPending);
            continuationState.Should().Be(BaseActivationState.Succeeded);

            host.Faults.MakeNextAtomicCommitIndeterminate();
            BaseInstalledActivationWorkerHandle<AuthUserCleanupInitializeV1,
                AuthCleanupRetirementResultV1> retirement = system.Activations.GetWorker(
                    AuthLifecycleActivationDeclarations.RetireUser.Identity);
            OperationResult<BaseActivationDispatchResult> retirementResponseLost =
                await retirement.RunOneAsync();
            retirementResponseLost.IsSuccess().Should().BeTrue(retirementResponseLost.Error?.Code);
            retirementResponseLost.Value!.State.Should().Be(BaseActivationState.Succeeded,
                "the handler must resolve the exact L50 receipt before continuing physical retirement");

            AuthCleanupWorkReadV1.Row completed = (await system.Reads.FirstAsync(
                AuthCleanupWorkReadV1.Handle,
                new AuthCleanupWorkReadV1
                {
                    TenantId = TenantId,
                    SubjectKind = AuthCleanupSubjectKindV1.user,
                    SubjectId = userId,
                    Incarnation = BaseBinary.From(acquired.Reference.Incarnation.ToArray()),
                })).RequireValue()!;
            completed.State.Should().Be(AuthCleanupStateV1.complete);
            (await system.Collection(AuthUserRecordV1.Collection)
                .GetAsync(RecordId.Create(userId.ToString("D"))))
                .Status.Should().Be(OperationStatus.NotFound);
            (await retirement.RunOneAsync()).Value!.Empty.Should().BeTrue(
                "the identified semantic retirement must not be duplicated after response loss");

            BaseSemanticActivationControlDescriptor semantic = await ReadUserSemanticControlAsync(
                host.GetRequiredService<IHPDBaseApplication>().Administration,
                new PrincipalContext
                {
                    AuthenticationState = PrincipalAuthenticationState.System,
                    SubjectKind = AccessSubjectKind.System,
                    SubjectId = "hpd.auth",
                    CurrentTenantId = TenantId.ToString("D"),
                    AuthSource = "hpd.auth.cleanup.retention-response-loss.control.v1",
                }, "sqlite");
            semantic.LiveCount.Should().Be(0);
            semantic.RetiredCount.Should().Be(1);

            IHPDBaseAdministration administration =
                host.GetRequiredService<IHPDBaseApplication>().Administration;
            PrincipalContext controlPrincipal = new()
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.cleanup.retention-response-loss.control.v1",
            };
            BaseOwnedSubjectScopeEvidence scope = new()
            {
                Kind = BaseSubjectScopeKind.Tenant,
                Value = TenantId.ToString("D"),
            };
            BaseActivationDefinition cleanupDefinition =
                AuthCleanupActivationDeclarations.User.Definition;

            BaseActivationDefinition[] lifecycleDefinitions =
            {
                AuthLifecycleActivationDeclarations.RetireUser.Definition,
                cleanupDefinition,
                AuthLifecycleActivationDeclarations.BootstrapUser.Definition,
            };
            foreach (BaseActivationDefinition definition in lifecycleDefinitions)
            {
                BaseActivationAdministrationBoundary? activationCursor = null;
                do
                {
                    BaseActivationAdministrationPage terminalPage =
                        (await administration.ReadActivationsAsync(
                            new BaseActivationAdministrationReadRequest
                            {
                                StoreId = "sqlite",
                                Principal = controlPrincipal,
                                Scope = scope,
                                DefinitionId = definition.Id,
                                DefinitionVersion = definition.Version,
                                States = BaseActivationStateSelector.Terminal,
                                After = activationCursor,
                                Take = 64,
                            })).RequireValue();
                    foreach (BaseActivationAdministrationItem terminal in terminalPage.Items)
                    {
                        (await administration.DisposeActivationAsync(
                            new BaseActivationAdministrationDisposeRequest
                            {
                                StoreId = "sqlite",
                                Principal = controlPrincipal,
                                DefinitionId = definition.Id,
                                DefinitionVersion = definition.Version,
                                ActivationId = terminal.ActivationId,
                                ExpectedGeneration = terminal.Generation,
                                Identity = AuthBaseRuntime.MutationIdentity(
                                    "hpd.auth.cleanup.activation.dispose.v1", TenantId,
                                    cleanupWorkId, definition.Id, terminal.ActivationId,
                                    terminal.Generation.ToString(CultureInfo.InvariantCulture)),
                            })).RequireValue();
                    }
                    activationCursor = terminalPage.Next;
                }
                while (activationCursor is not null);
            }

            host.Time.Advance(TimeSpan.FromHours(25));

            foreach (BaseActivationDefinition definition in lifecycleDefinitions)
            {
                BaseActivationReceiptCompactionCursor? receiptCursor = null;
                int receiptPage = 0;
                do
                {
                    BaseResult<BaseActivationReceiptCompactionResult> compactedReceiptResult =
                        await administration.CompactActivationReceiptsAsync(
                            new BaseActivationAdministrationReceiptCompactionRequest
                            {
                                StoreId = "sqlite",
                                Principal = controlPrincipal,
                                Scope = scope,
                                DefinitionId = definition.Id,
                                DefinitionVersion = definition.Version,
                                AfterActivationId = receiptCursor?.ActivationId,
                                AfterReceiptSequence = receiptCursor?.ReceiptSequence,
                                Take = definition.Limits.Provider.MaximumCandidates,
                                Identity = AuthBaseRuntime.MutationIdentity(
                                    "hpd.auth.cleanup.receipts.compact.v1", TenantId,
                                    cleanupWorkId, definition.Id,
                                    (receiptPage++).ToString(CultureInfo.InvariantCulture)),
                            });
                    compactedReceiptResult.Should().BeOfType<BaseSuccess<BaseActivationReceiptCompactionResult>>(
                        compactedReceiptResult is BaseFailure<BaseActivationReceiptCompactionResult> failure
                            ? $"{failure.Status}:{failure.Error.Code}:{failure.Error.Message}"
                            : compactedReceiptResult.Status.ToString());
                    receiptCursor = compactedReceiptResult.RequireValue().Next;
                }
                while (receiptCursor is not null);
            }

            foreach (BaseActivationDefinition definition in lifecycleDefinitions)
            {
                string? pruneCursor = null;
                int prunePage = 0;
                do
                {
                    BaseResult<BaseActivationPrunePage> pruneResult =
                        await administration.PruneActivationsAsync(
                            new BaseActivationAdministrationPruneRequest
                            {
                                StoreId = "sqlite",
                                Principal = controlPrincipal,
                                Scope = scope,
                                DefinitionId = definition.Id,
                                DefinitionVersion = definition.Version,
                                AfterActivationId = pruneCursor,
                                Take = 64,
                                Identity = AuthBaseRuntime.MutationIdentity(
                                    "hpd.auth.cleanup.activation.prune.v1", TenantId,
                                    cleanupWorkId, definition.Id,
                                    (prunePage++).ToString(CultureInfo.InvariantCulture)),
                            });
                    pruneResult.Should().BeOfType<BaseSuccess<BaseActivationPrunePage>>(
                        pruneResult is BaseFailure<BaseActivationPrunePage> failure
                            ? $"{definition.Id}:{failure.Status}:{failure.Error.Code}:{failure.Error.Message}"
                            : pruneResult.Status.ToString());
                    pruneCursor = pruneResult.RequireValue().NextActivationId;
                }
                while (pruneCursor is not null);
            }

            BaseSemanticActivationControlDescriptor compactable =
                await ReadUserSemanticControlAsync(administration, controlPrincipal, "sqlite");
            BaseSemanticActivationControlToken compactToken = compactable.Compact
                ?? throw new InvalidOperationException(
                    "Retired Auth cleanup semantic authority was not compactable.");
            const string semanticCompactionIdentity =
                "auth-user-cleanup-semantic-compaction-response-loss-proof";
            BaseResult<BaseSemanticActivationControlResult> semanticCompactResult =
                await administration.ExecuteSemanticActivationControlAsync(
                    "sqlite", controlPrincipal,
                    new BaseSemanticActivationControlCommand
                    {
                        Token = compactToken,
                        IdempotencyKey = semanticCompactionIdentity,
                        Confirmation = "compact-retired-semantic-authority",
                    });
            semanticCompactResult.Should().BeOfType<BaseSuccess<BaseSemanticActivationControlResult>>(
                semanticCompactResult is BaseFailure<BaseSemanticActivationControlResult> semanticFailure
                    ? $"{semanticFailure.Status}:{semanticFailure.Error.Code}:{semanticFailure.Error.Message}"
                    : semanticCompactResult.Status.ToString());
            BaseSemanticActivationControlResult compacted = semanticCompactResult.RequireValue();
            while (compacted.Resume is not null)
            {
                compacted = (await administration.ExecuteSemanticActivationControlAsync(
                    "sqlite", controlPrincipal,
                    new BaseSemanticActivationControlCommand
                    {
                        Token = compacted.Resume,
                        IdempotencyKey = semanticCompactionIdentity,
                        Confirmation = "resume-semantic-maintenance",
                    })).RequireValue();
            }
            compacted.Disposition.Should().Be(BaseSemanticActivationMaintenanceDisposition.Completed);
            BaseSemanticActivationControlDescriptor absent =
                await ReadUserSemanticControlAsync(administration, controlPrincipal, "sqlite");
            absent.LiveCount.Should().Be(0);
            absent.RetiredCount.Should().Be(0);
            absent.AbsenceCount.Should().Be(1);

            OperationResult<BaseActivationEnqueueResult> staleBootstrap = await bootstrap.EnqueueAsync(
                initialize,
                AuthBaseRuntime.MutationIdentity(
                    "hpd.auth.cleanup.bootstrap.user.stale.v1", TenantId, cleanupWorkId,
                    "after-semantic-compaction"));
            staleBootstrap.IsSuccess().Should().BeTrue(staleBootstrap.Error?.Code);
            (await system.Activations.GetWorker(
                AuthLifecycleActivationDeclarations.BootstrapUser.Identity).RunOneAsync())
                .Value!.State.Should().Be(BaseActivationState.Succeeded);
            BaseSemanticActivationControlDescriptor stillAbsent =
                await ReadUserSemanticControlAsync(administration, controlPrincipal, "sqlite");
            stillAbsent.LiveCount.Should().Be(0);
            stillAbsent.RetiredCount.Should().Be(0);
            stillAbsent.AbsenceCount.Should().Be(1,
                "stale reconciliation must resolve the terminal semantic absence instead of rematerializing cleanup");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
            File.Delete(database + "-shm");
            File.Delete(database + "-wal");
        }
    }

    [Fact]
    public async Task Direct_semantic_retirement_before_parent_preparation_is_rejected_without_state_change()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.parent-race.service.v1",
        });
        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.parent-race.system.v1",
        });
        Guid userId = Guid.NewGuid();
        AuthCreateUserResultV1 created = await CreateUserThroughBaseAsync(
            service, userId, "parent-race@example.invalid");
        AuthUserSubjectAcquisitionReadV1.Row acquired = (await service.Reads.FirstAsync(
            AuthUserSubjectAcquisitionReadV1.Handle,
            new AuthUserSubjectAcquisitionReadV1
            {
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            })).RequireValue()!;
        BaseExportedSubjectContract<AuthUserSubject> subjects = AuthSubjects.Users(service);
        BaseSubjectTombstoneResult<AuthUserSubject> tombstone = (await subjects.TombstoneAsync(
            new BaseSubjectTombstoneRequest<AuthUserSubject>
            {
                Subject = acquired.Reference,
                ExpectedPrivateRevision = created.Revision,
                Identity = AuthBaseRuntime.MutationIdentity(
                    "hpd.auth.user-subject.tombstone.v1", TenantId, userId.ToString("D"),
                    created.Revision.Value, acquired.Reference.Incarnation.ToBase64Url()),
            })).RequireValue();
        string cleanupWorkId = AuthBaseDeterministicId.CreateCleanupWork(
            TenantId, "user", userId, subjects, acquired.Reference.Incarnation,
            tombstone.Fact.Fact.SubjectSequence);
        var initialize = new AuthUserCleanupInitializeV1
        {
            CleanupWorkId = cleanupWorkId,
            TenantId = TenantId,
            SubjectId = userId,
            Subject = acquired.Reference,
            Incarnation = acquired.Reference.Incarnation,
            TombstoneSequence = tombstone.Fact.Fact.SubjectSequence,
            TombstoneRevision = tombstone.PrivateRevision.Value,
            WorkflowVersion = 1,
            TombstonedAt = tombstone.TombstonedAt,
            RetirementReceiptScope = "auth.cleanup.initialize",
            OperationTime = tombstone.TombstonedAt,
        };
        BaseInstalledActivationHandle<AuthUserCleanupInitializeV1, AuthCleanupInitializeResultV1>
            bootstrap = system.Activations.Get(
                AuthLifecycleActivationDeclarations.BootstrapUser.Identity);
        OperationResult<BaseActivationEnqueueResult> bootstrapped = await bootstrap.EnqueueAsync(
            initialize,
            AuthBaseRuntime.MutationIdentity(
                "hpd.auth.cleanup.bootstrap.user.v1", TenantId, cleanupWorkId,
                tombstone.PrivateRevision.Value,
                tombstone.Fact.Fact.SubjectSequence.ToString(CultureInfo.InvariantCulture)));
        bootstrapped.IsSuccess().Should().BeTrue(bootstrapped.Error?.Code);
        (await system.Activations.GetWorker(
            AuthLifecycleActivationDeclarations.BootstrapUser.Identity).RunOneAsync())
            .Value!.State.Should().Be(BaseActivationState.Succeeded);

        BaseInstalledActivationHandle<AuthUserCleanupInitializeV1, AuthCleanupRetirementResultV1>
            retirement = system.Activations.Get(
                AuthLifecycleActivationDeclarations.RetireUser.Identity);
        OperationResult<BaseActivationEnqueueResult> early = await retirement.EnqueueAsync(
            initialize,
            AuthBaseRuntime.MutationIdentity(
                "hpd.auth.cleanup.semantic-retire.user.v1", TenantId, cleanupWorkId,
                "parent-still-nonterminal"));
        early.IsSuccess().Should().BeTrue(early.Error?.Code);
        OperationResult<BaseActivationDispatchResult> raced = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.RetireUser.Identity)
            .RunOneAsync();

        raced.IsSuccess().Should().BeTrue(raced.Error?.Code);
        raced.Value!.State.Should().Be(BaseActivationState.Exhausted,
            "callers cannot bypass the parent-owned preparation transition that installs the retirement child");
        AuthCleanupWorkReadV1.Row work = (await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(acquired.Reference.Incarnation.ToArray()),
            })).RequireValue()!;
        work.State.Should().Be(AuthCleanupStateV1.draining);
        BaseSemanticActivationControlDescriptor semantic = await ReadUserSemanticControlAsync(
            provider.GetRequiredService<IHPDBaseApplication>().Administration,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.cleanup.parent-race.control.v1",
            });
        semantic.LiveCount.Should().Be(1);
        semantic.RetiredCount.Should().Be(0);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delivery_expiration_deletes_maintenance_runs_in_bounded_repeatable_cohorts(bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.expiration.test.v1",
        });
        DateTimeOffset expiredCutoff = Now.AddDays(-36);
        for (int chunk = 0; chunk < 3; chunk++)
        {
            int start = chunk * 100;
            int count = Math.Min(100, 201 - start);
            BaseBatchBuilder seed = system.Atomic(AuthBaseRuntime.MutationIdentity(
                "hpd.auth.maintenance-runs.seed.v1", TenantId,
                chunk.ToString(CultureInfo.InvariantCulture), expiredCutoff.ToString("O")));
            for (int offset = 0; offset < count; offset++)
            {
                int index = start + offset;
                string id = index.ToString("x64", CultureInfo.InvariantCulture);
                seed.Create(
                    AuthMaintenanceRunRecordV1.Collection,
                    RecordId.Create(id),
                    new AuthMaintenanceRunRecordV1
                    {
                        Id = id,
                        ActivationId = $"expired-maintenance-{index:D3}",
                        Kind = AuthMaintenanceKindV1.deliveryExpiration,
                        Cutoff = expiredCutoff,
                        CreatedAt = expiredCutoff,
                    });
            }
            (await seed.CommitAsync()).RequireValue().RequireCommitted();
        }

        BaseInstalledActivationHandle<AuthExpirationTriggerInputV1, AuthExpirationResultV1> activation =
            system.Activations.Get(AuthLifecycleActivationDeclarations.Deliveries.Identity);
        var input = new AuthExpirationTriggerInputV1
        {
            Kind = AuthMaintenanceKindV1.deliveryExpiration,
            ContractVersion = 1,
        };

        OperationResult<BaseActivationEnqueueResult> first = await activation.EnqueueAsync(
            input,
            BaseMutationRequestIdentity.Create(
                "hpd.auth.expiration.test", "maintenance-cleanup", "first",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("maintenance-cleanup-first"u8))));
        OperationResult<BaseActivationEnqueueResult> firstDuplicate = await activation.EnqueueAsync(
            input,
            BaseMutationRequestIdentity.Create(
                "hpd.auth.expiration.test", "maintenance-cleanup", "first",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("maintenance-cleanup-first"u8))));
        first.IsSuccess().Should().BeTrue(first.Error?.Code);
        firstDuplicate.IsSuccess().Should().BeTrue(firstDuplicate.Error?.Code);
        firstDuplicate.Value!.ActivationId.Should().Be(first.Value!.ActivationId);
        OperationResult<BaseActivationDispatchResult> firstDispatch = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.Deliveries.Identity).RunOneAsync();
        firstDispatch.IsSuccess().Should().BeTrue(firstDispatch.Error?.Code);
        firstDispatch.Value!.State.Should().Be(BaseActivationState.Succeeded);

        (await system.Reads.FirstAsync(
            AuthMaintenanceRunReadV1.Handle,
            new AuthMaintenanceRunReadV1 { ActivationId = "expired-maintenance-000" }))
            .RequireValue().Should().BeNull("the first bounded cohort contains the lowest 200 ordered IDs");
        (await system.Reads.FirstAsync(
            AuthMaintenanceRunReadV1.Handle,
            new AuthMaintenanceRunReadV1 { ActivationId = "expired-maintenance-200" }))
            .RequireValue().Should().NotBeNull("one record must remain after the 200-record cohort");

        await EnqueueAndRunDeliveryExpirationAsync(system, activation, input, "second");
        (await system.Reads.FirstAsync(
            AuthMaintenanceRunReadV1.Handle,
            new AuthMaintenanceRunReadV1 { ActivationId = "expired-maintenance-200" }))
            .RequireValue().Should().BeNull("the second cohort drains the remaining expired record");

        await EnqueueAndRunDeliveryExpirationAsync(system, activation, input, "zero");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Issue_replays_the_same_bearer_for_the_same_identified_attempt(bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Core_identity_membership_password_and_session_receipts_are_provider_consistent(
        bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "auth.tests",
        });
        Guid userId = Guid.NewGuid();
        var request = new AuthCreateUserV1
        {
            TenantId = TenantId,
            UserId = userId,
            UserName = "receipt@test.invalid",
            NormalizedUserName = "RECEIPT@TEST.INVALID",
            Email = "receipt@test.invalid",
            NormalizedEmail = "RECEIPT@TEST.INVALID",
            SecurityStamp = "stamp-v1",
            ConcurrencyStamp = "concurrency-v1",
            LockoutEnabled = true,
            EmailConfirmed = false,
            PhoneNumberConfirmed = false,
            TwoFactorEnabled = false,
            AccessFailedCount = 0,
            UserMetadata = CanonicalJson("{}"u8),
            AppMetadata = CanonicalJson("{}"u8),
            RequiredActions = CanonicalJson("[]"u8),
            IsActive = true,
            SubscriptionTier = "free",
            OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthCreateUserV1, AuthCreateUserResultV1> operation =
            session.ModuleMutations.Get(AuthCreateUserOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{userId:D}:receipt-proof");

        BaseModuleMutationExecutionResult<AuthCreateUserResultV1> committed =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthCreateUserResultV1> duplicate =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthCreateUserResultV1> resolved =
            (await operation.ResolveAsync(identity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthCreateUserResultV1>> collision =
            await operation.ExecuteAsync(request, identity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0xA7, 32).ToArray()),
            });

        committed.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.Result.Should().Be(committed.Result);
        resolved.Result.Should().Be(committed.Result);
        collision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthCreateUserResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");

        Guid roleId = Guid.NewGuid();
        var roleRequest = new AuthRoleCreateV1
        {
            TenantId = TenantId,
            RoleId = roleId,
            Name = "Operators",
            NormalizedName = "OPERATORS",
            ConcurrencyStamp = "role-concurrency-v1",
            Description = "Receipt consistency proof",
            OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthRoleCreateV1, AuthRoleCreateResultV1> roleOperation =
            session.ModuleMutations.Get(AuthCreateRoleOperationV1.Identity);
        BaseMutationRequestIdentity roleIdentity = roleOperation.CreateRequestIdentity(
            roleRequest, $"role:{roleId:D}:receipt-proof");

        BaseModuleMutationExecutionResult<AuthRoleCreateResultV1> roleCommitted =
            (await roleOperation.ExecuteAsync(roleRequest, roleIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthRoleCreateResultV1> roleDuplicate =
            (await roleOperation.ExecuteAsync(roleRequest, roleIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthRoleCreateResultV1> roleResolved =
            (await roleOperation.ResolveAsync(roleIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthRoleCreateResultV1>> roleCollision =
            await roleOperation.ExecuteAsync(roleRequest, roleIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0xB8, 32).ToArray()),
            });

        roleCommitted.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        roleDuplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        roleResolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        roleDuplicate.Result.Should().Be(roleCommitted.Result);
        roleResolved.Result.Should().Be(roleCommitted.Result);
        roleCollision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthRoleCreateResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");

        string membershipId = Convert.ToHexStringLower(SHA256.HashData("receipt-membership"u8));
        var membershipRequest = new AuthMembershipAddV1
        {
            TenantId = TenantId,
            UserId = userId,
            RoleId = roleId,
            MembershipId = membershipId,
            ExpectedUserRevision = committed.Result.Revision,
            ExpectedRoleRevision = roleCommitted.Result.Revision,
            CreatedAt = Now,
        };
        BaseInstalledModuleMutationHandle<AuthMembershipAddV1, AuthMembershipAddResultV1> membershipOperation =
            session.ModuleMutations.Get(AuthMembershipAddOperationV1.Identity);
        BaseMutationRequestIdentity membershipIdentity = membershipOperation.CreateRequestIdentity(
            membershipRequest, $"membership:{membershipId}:receipt-proof");

        BaseModuleMutationExecutionResult<AuthMembershipAddResultV1> membershipCommitted =
            (await membershipOperation.ExecuteAsync(membershipRequest, membershipIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthMembershipAddResultV1> membershipDuplicate =
            (await membershipOperation.ExecuteAsync(membershipRequest, membershipIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthMembershipAddResultV1> membershipResolved =
            (await membershipOperation.ResolveAsync(membershipIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthMembershipAddResultV1>> membershipCollision =
            await membershipOperation.ExecuteAsync(membershipRequest, membershipIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0xC9, 32).ToArray()),
            });

        membershipCommitted.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        membershipDuplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        membershipResolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        membershipDuplicate.Result.Should().Be(membershipCommitted.Result);
        membershipResolved.Result.Should().Be(membershipCommitted.Result);
        membershipCollision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthMembershipAddResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");

        var passwordRequest = new AuthChangePasswordV1
        {
            TenantId = TenantId,
            UserId = userId,
            ExpectedRevision = committed.Result.Revision,
            PasswordHash = "argon2id$receipt-proof",
            SecurityStamp = "stamp-v2",
            ConcurrencyStamp = "concurrency-v2",
            OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthChangePasswordV1, AuthSecurityMutationResultV1> passwordOperation =
            session.ModuleMutations.Get(AuthChangePasswordOperationV1.Identity);
        BaseMutationRequestIdentity passwordIdentity = passwordOperation.CreateRequestIdentity(
            passwordRequest, $"user:{userId:D}:password-receipt-proof");

        BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1> passwordCommitted =
            (await passwordOperation.ExecuteAsync(passwordRequest, passwordIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1> passwordDuplicate =
            (await passwordOperation.ExecuteAsync(passwordRequest, passwordIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1> passwordResolved =
            (await passwordOperation.ResolveAsync(passwordIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> passwordCollision =
            await passwordOperation.ExecuteAsync(passwordRequest, passwordIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0xDA, 32).ToArray()),
            });

        passwordCommitted.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        passwordDuplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        passwordResolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        passwordDuplicate.Result.Should().Be(passwordCommitted.Result);
        passwordResolved.Result.Should().Be(passwordCommitted.Result);
        passwordCollision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");

        Guid sessionId = Guid.NewGuid();
        var sessionRequest = new AuthSessionCreateV1
        {
            SessionId = sessionId,
            TenantId = TenantId,
            UserId = userId,
            ExpectedUserRevision = passwordCommitted.Result.Revision,
            Aal = AuthSessionAssuranceLevelV1.aal2,
            BrokerSessionId = null,
            BrokerUserId = null,
            SsoProviderId = null,
            NotBefore = Now,
            NotAfter = Now.AddHours(1),
            OauthClientId = null,
            Scopes = "openid profile",
            ClientSessions = CanonicalJson("{}"u8),
            State = AuthSessionStateV1.active,
            IpAddress = "127.0.0.1",
            UserAgent = "receipt-proof",
            DeviceInfo = "test",
            CreatedAt = Now,
            LastActiveAt = Now,
            ExpiresAt = Now.AddHours(1),
            Revoked = false,
            RevokedAt = null,
            RetentionEligibleAt = null,
            OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthSessionCreateV1, AuthSessionCreateResultV1> sessionOperation =
            session.ModuleMutations.Get(AuthSessionCreateOperationV1.Identity);
        BaseMutationRequestIdentity sessionIdentity = sessionOperation.CreateRequestIdentity(
            sessionRequest, $"session:{sessionId:D}:receipt-proof");

        BaseModuleMutationExecutionResult<AuthSessionCreateResultV1> sessionCommitted =
            (await sessionOperation.ExecuteAsync(sessionRequest, sessionIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthSessionCreateResultV1> sessionDuplicate =
            (await sessionOperation.ExecuteAsync(sessionRequest, sessionIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthSessionCreateResultV1> sessionResolved =
            (await sessionOperation.ResolveAsync(sessionIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthSessionCreateResultV1>> sessionCollision =
            await sessionOperation.ExecuteAsync(sessionRequest, sessionIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0xEB, 32).ToArray()),
            });

        sessionCommitted.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        sessionDuplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        sessionResolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        sessionDuplicate.Result.Should().Be(sessionCommitted.Result);
        sessionResolved.Result.Should().Be(sessionCommitted.Result);
        sessionCollision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthSessionCreateResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_password_atomically_clears_lockout_rotates_authority_and_resolves_response_loss(
        bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "auth.tests.reset-password",
        });
        Guid userId = Guid.NewGuid();
        AuthCreateUserResultV1 created = await CreateUserThroughBaseAsync(
            session,
            userId,
            $"reset-{inMemory}@test.invalid");

        DateTimeOffset lockoutEnd = Now.AddMinutes(30);
        var lockedRequest = new AuthSetSecurityStateV1
        {
            TenantId = TenantId,
            UserId = userId,
            ExpectedRevision = created.Revision,
            TwoFactorEnabled = false,
            AuthenticatorKey = null,
            ClearLockoutEnd = false,
            LockoutEnd = lockoutEnd,
            LockoutEnabled = true,
            AccessFailedCount = 7,
            SecurityStamp = "stamp-before-reset",
            ConcurrencyStamp = "concurrency-before-reset",
            OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthSetSecurityStateV1, AuthSecurityMutationResultV1> lockOperation =
            session.ModuleMutations.Get(AuthSetSecurityStateOperationV1.Identity);
        AuthSecurityMutationResultV1 locked = (await lockOperation.ExecuteAsync(
            lockedRequest,
            lockOperation.CreateRequestIdentity(
                lockedRequest,
                $"user:{userId:D}:lock-before-reset"))).RequireValue().Result;

        var resetRequest = new AuthResetPasswordV1
        {
            TenantId = TenantId,
            UserId = userId,
            ExpectedRevision = locked.Revision,
            PasswordHash = "argon2id$atomic-reset-proof",
            SecurityStamp = "stamp-after-reset",
            ConcurrencyStamp = "concurrency-after-reset",
            LockoutEnabled = true,
            OperationTime = Now.AddSeconds(1),
        };
        BaseInstalledModuleMutationHandle<AuthResetPasswordV1, AuthSecurityMutationResultV1> resetOperation =
            session.ModuleMutations.Get(AuthResetPasswordOperationV1.Identity);
        BaseMutationRequestIdentity resetIdentity = resetOperation.CreateRequestIdentity(
            resetRequest,
            $"user:{userId:D}:atomic-reset-proof");

        BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1> committed =
            (await resetOperation.ExecuteAsync(resetRequest, resetIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1> duplicate =
            (await resetOperation.ExecuteAsync(resetRequest, resetIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1> resolvedAfterResponseLoss =
            (await resetOperation.ResolveAsync(resetIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> collision =
            await resetOperation.ExecuteAsync(resetRequest, resetIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0xFC, 32).ToArray()),
            });

        committed.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolvedAfterResponseLoss.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.Result.Should().Be(committed.Result);
        resolvedAfterResponseLoss.Result.Should().Be(committed.Result);
        committed.Result.Revision.Should().NotBe(locked.Revision);
        long.Parse(committed.Result.UserGeneration.ToCanonicalString(), CultureInfo.InvariantCulture)
            .Should().Be(long.Parse(locked.UserGeneration.ToCanonicalString(), CultureInfo.InvariantCulture) + 1);
        long.Parse(committed.Result.SecurityGeneration.ToCanonicalString(), CultureInfo.InvariantCulture)
            .Should().Be(long.Parse(locked.SecurityGeneration.ToCanonicalString(), CultureInfo.InvariantCulture) + 1);
        collision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");

        AuthUserByIdReadV1.Row user = (await session.Reads.FirstAsync(
            AuthUserByIdReadV1.Handle,
            new AuthUserByIdReadV1 { TenantId = TenantId, UserId = userId })).RequireValue()!;
        user.Revision.Should().Be(committed.Result.Revision);
        user.AccessFailedCount.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
        user.LockoutEnabled.Should().BeTrue();
        user.ConcurrencyStamp.Should().Be("concurrency-after-reset");

        AuthUserPasswordReadV1.Row secret = (await session.Reads.FirstAsync(
            AuthUserPasswordReadV1.Handle,
            new AuthUserPasswordReadV1 { TenantId = TenantId, UserId = userId })).RequireValue()!;
        secret.PasswordHash.Should().Be("argon2id$atomic-reset-proof");
        secret.SecurityStamp.Should().Be("stamp-after-reset");
        secret.Revision.Should().Be(committed.Result.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_passkey_adapter_routes_existing_authority_to_assertion(
        bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider,
            $"passkey-adapter-{inMemory}@example.invalid");
        IUserStore<ApplicationUser> rawStore = scope.ServiceProvider
            .GetRequiredService<IUserStore<ApplicationUser>>();
        var passkeyStore = (IUserPasskeyStore<ApplicationUser>)rawStore;
        ApplicationUser user = (await rawStore.FindByIdAsync(
            userId.ToString("D"), CancellationToken.None))!;

        byte[] credentialId = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"credential-{inMemory}"));
        byte[] publicKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"public-key-{inMemory}"));
        byte[] attestation = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"attestation-{inMemory}"));
        byte[] clientData = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"client-data-{inMemory}"));
        DateTimeOffset createdAt = Now.AddMinutes(-5);
        var registered = new UserPasskeyInfo(
            credentialId, publicKey, createdAt, 0, ["internal"],
            isUserVerified: false, isBackupEligible: true, isBackedUp: false,
            attestation, clientData)
        { Name = "Primary key" };

        await passkeyStore.AddOrUpdatePasskeyAsync(user, registered, CancellationToken.None);
        string securityStampAfterRegistration = user.SecurityStamp!;
        UserPasskeyInfo stored = (await passkeyStore.FindPasskeyAsync(
            user, credentialId, CancellationToken.None))!;
        stored.SignCount.Should().Be(0);
        stored.IsBackedUp.Should().BeFalse();
        stored.Name.Should().Be("Primary key");
        stored.IsBackupEligible.Should().BeTrue();
        stored.PublicKey.Should().Equal(publicKey);
        stored.AttestationObject.Should().Equal(attestation);
        stored.ClientDataJson.Should().Equal(clientData);
        stored.Transports.Should().Equal("internal");

        var asserted = new UserPasskeyInfo(
            credentialId, publicKey, stored.CreatedAt, 7, ["internal"],
            isUserVerified: true, isBackupEligible: true, isBackedUp: true,
            attestation, clientData)
        { Name = "Primary key" };
        await passkeyStore.AddOrUpdatePasskeyAsync(user, asserted, CancellationToken.None);

        stored = (await passkeyStore.FindPasskeyAsync(
            user, credentialId, CancellationToken.None))!;
        stored.SignCount.Should().Be(7);
        stored.IsUserVerified.Should().BeTrue();
        stored.IsBackedUp.Should().BeTrue();
        user.SecurityStamp.Should().Be(securityStampAfterRegistration,
            "an assertion must not execute the registration operation or rotate security authority");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Session_touch_adapter_uses_revision_bound_operation_and_updates_activity(
        bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider,
            $"session-touch-{inMemory}@example.invalid");
        ISessionManager sessions = scope.ServiceProvider.GetRequiredService<ISessionManager>();
        UserSession created = await sessions.CreateSessionAsync(
            userId, new SessionContext("192.0.2.1", "initial-agent"));
        provider.GetRequiredService<MutableTimeProvider>().Advance(TimeSpan.FromMinutes(1));

        UserSession touched = await sessions.TouchSessionAsync(
            userId, created.Id, new SessionContext("192.0.2.2", "current-agent"));

        touched.Id.Should().Be(created.Id);
        touched.LastActiveAt.Should().BeAfter(created.LastActiveAt);
        touched.IpAddress.Should().Be("192.0.2.2");
        touched.UserAgent.Should().Be("current-agent");
        UserSession persisted = (await sessions.GetActiveSessionsAsync(userId))
            .Single(candidate => candidate.Id == created.Id);
        persisted.LastActiveAt.Should().Be(touched.LastActiveAt);
        persisted.IpAddress.Should().Be("192.0.2.2");
        persisted.UserAgent.Should().Be("current-agent");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Passkey_receipt_replay_and_fingerprint_conflict_are_provider_consistent(
        bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        (Guid userId, AuthCreateUserResultV1 createdUser) = await CreateUserWithAuthorityAsync(
            provider, "passkey-receipt@test.invalid");
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "auth.tests",
        });
        byte[] credentialId = SHA256.HashData("passkey-receipt-credential"u8);
        string passkeyId = Convert.ToHexStringLower(SHA256.HashData(credentialId));
        var request = new AuthPasskeyRegisterV1
        {
            TenantId = TenantId,
            UserId = userId,
            PasskeyId = passkeyId,
            ExpectedUserRevision = createdUser.Revision,
            CredentialDigest = BaseBinary.From(SHA256.HashData(credentialId)),
            CredentialId = BaseBinary.From(credentialId),
            PublicKey = BaseBinary.From(SHA256.HashData("passkey-public-key"u8)),
            SignatureCounter = 0,
            AaGuid = null,
            Name = "Receipt proof passkey",
            Transports = CanonicalJson("[]"u8),
            UserVerified = true,
            BackupEligible = true,
            BackedUp = false,
            IsDiscoverable = true,
            AttestationObject = BaseBinary.From([1, 2, 3]),
            ClientDataJson = BaseBinary.From([4, 5, 6]),
            SecurityStamp = "stamp-passkey-v2",
            ConcurrencyStamp = "concurrency-passkey-v2",
            OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthPasskeyRegisterV1, AuthPasskeyRegisterResultV1> operation =
            service.ModuleMutations.Get(AuthPasskeyRegisterOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"passkey:{passkeyId}:receipt-proof");

        BaseModuleMutationExecutionResult<AuthPasskeyRegisterResultV1> committed =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthPasskeyRegisterResultV1> duplicate =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthPasskeyRegisterResultV1> resolved =
            (await operation.ResolveAsync(identity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthPasskeyRegisterResultV1>> collision =
            await operation.ExecuteAsync(request, identity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0xFC, 32).ToArray()),
            });

        committed.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.Result.Should().Be(committed.Result);
        resolved.Result.Should().Be(committed.Result);
        collision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthPasskeyRegisterResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Passkey_counter_boundaries_and_regression_are_provider_consistent(
        bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        (Guid userId, AuthCreateUserResultV1 createdUser) = await CreateUserWithAuthorityAsync(
            provider, "passkey-counter@test.invalid");
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "auth.tests",
        });
        byte[] credentialId = SHA256.HashData("passkey-counter-credential"u8);
        string passkeyId = Convert.ToHexStringLower(SHA256.HashData(credentialId));
        BaseInstalledModuleMutationHandle<AuthPasskeyRegisterV1, AuthPasskeyRegisterResultV1> register =
            service.ModuleMutations.Get(AuthPasskeyRegisterOperationV1.Identity);
        var registration = new AuthPasskeyRegisterV1
        {
            TenantId = TenantId,
            UserId = userId,
            PasskeyId = passkeyId,
            ExpectedUserRevision = createdUser.Revision,
            CredentialDigest = BaseBinary.From(SHA256.HashData(credentialId)),
            CredentialId = BaseBinary.From(credentialId),
            PublicKey = BaseBinary.From(SHA256.HashData("passkey-counter-public-key"u8)),
            SignatureCounter = 0,
            AaGuid = null,
            Name = "Counter boundary passkey",
            Transports = CanonicalJson("[]"u8),
            UserVerified = true,
            BackupEligible = false,
            BackedUp = false,
            IsDiscoverable = true,
            AttestationObject = BaseBinary.From([1]),
            ClientDataJson = BaseBinary.From([2]),
            SecurityStamp = "stamp-passkey-counter",
            ConcurrencyStamp = "concurrency-passkey-counter",
            OperationTime = Now,
        };
        AuthPasskeyRegisterResultV1 registered = (await register.ExecuteAsync(
            registration,
            register.CreateRequestIdentity(registration, $"passkey:{passkeyId}:register-boundary")))
            .RequireValue().Result;
        BaseInstalledModuleMutationHandle<AuthPasskeyRecordAssertionV1, AuthPasskeyAssertionResultV1> assert =
            service.ModuleMutations.Get(AuthPasskeyRecordAssertionOperationV1.Identity);

        var zero = new AuthPasskeyRecordAssertionV1
        {
            TenantId = TenantId,
            UserId = userId,
            PasskeyId = passkeyId,
            ExpectedUserRevision = registered.UserRevision,
            ExpectedPasskeyRevision = registered.PasskeyRevision,
            PresentedCounter = 0,
            BackedUp = false,
            CounterSupported = false,
            UserVerified = true,
            OperationTime = Now.AddSeconds(1),
        };
        BaseResult<BaseModuleMutationExecutionResult<AuthPasskeyAssertionResultV1>> zeroExecution =
            await assert.ExecuteAsync(
            zero,
            assert.CreateRequestIdentity(zero, $"passkey:{passkeyId}:assert-zero"));
        zeroExecution.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<AuthPasskeyAssertionResultV1>>>(
            zeroExecution is BaseFailure<BaseModuleMutationExecutionResult<AuthPasskeyAssertionResultV1>> zeroFailure
                ? $"{zeroFailure.Error.Code}:{zeroFailure.Error.Category}:{zeroFailure.Status}"
                : string.Empty);
        AuthPasskeyAssertionResultV1 zeroResult = zeroExecution.RequireValue().Result;

        var maximum = zero with
        {
            ExpectedPasskeyRevision = zeroResult.Revision,
            PresentedCounter = 4_294_967_295,
            CounterSupported = true,
            OperationTime = Now.AddSeconds(2),
        };
        AuthPasskeyAssertionResultV1 maximumResult = (await assert.ExecuteAsync(
            maximum,
            assert.CreateRequestIdentity(maximum, $"passkey:{passkeyId}:assert-maximum")))
            .RequireValue().Result;

        var regressed = maximum with
        {
            ExpectedPasskeyRevision = maximumResult.Revision,
            PresentedCounter = 4_294_967_294,
            OperationTime = Now.AddSeconds(3),
        };
        BaseResult<BaseModuleMutationExecutionResult<AuthPasskeyAssertionResultV1>> regression =
            await assert.ExecuteAsync(
                regressed,
                assert.CreateRequestIdentity(regressed, $"passkey:{passkeyId}:assert-regression"));
        regression.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthPasskeyAssertionResultV1>>>()
            .Which.Error.Code.Should().Be("base.moduleMutation.requirementFailed",
                "the installed closed template owns auth.passkey.counterRegression as its requirement identity");

        var aboveMaximum = maximum with
        {
            ExpectedPasskeyRevision = maximumResult.Revision,
            PresentedCounter = 4_294_967_296,
            OperationTime = Now.AddSeconds(4),
        };
        Action createAboveMaximumIdentity = () => assert.CreateRequestIdentity(
            aboveMaximum, $"passkey:{passkeyId}:assert-above-maximum");
        createAboveMaximumIdentity.Should().Throw<Exception>()
            .Where(static exception =>
                exception.GetType().Name == "BaseModuleScalarContractException"
                || exception.GetType().Name == nameof(InvalidOperationException));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_code_replacement_receipt_is_provider_consistent(bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        (Guid userId, AuthCreateUserResultV1 createdUser) = await CreateUserWithAuthorityAsync(
            provider, "recovery-receipt@test.invalid");
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "auth.tests",
        });
        AuthRecoveryPriorSlotV1[] prior = Enumerable.Range(0, 64).Select(static index =>
            new AuthRecoveryPriorSlotV1
            {
                Active = false,
                Id = new string('0', 64),
            }).ToArray();
        AuthRecoveryNewSlotV1[] replacements = Enumerable.Range(0, 64).Select(static index =>
            new AuthRecoveryNewSlotV1
            {
                Active = index == 0,
                Id = index == 0
                    ? Convert.ToHexStringLower(SHA256.HashData([(byte)index, 0x20]))
                    : new string('0', 64),
                CodeDigest = index == 0
                    ? BaseBinary.From(SHA256.HashData([(byte)index, 0x30]))
                    : BaseBinary.From([]),
                DigestKeyVersion = 1,
            }).ToArray();
        AuthRecoveryCodesReplaceV1 request = AuthRecoveryCodesReplaceRequestFactory.Create(
            TenantId,
            userId,
            createdUser.Revision,
            prior,
            replacements,
            "stamp-recovery-v2",
            "concurrency-recovery-v2",
            Now);
        BaseInstalledModuleMutationHandle<AuthRecoveryCodesReplaceV1, AuthRecoveryCodeMutationResultV1> operation =
            service.ModuleMutations.Get(AuthRecoveryCodesReplaceOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{userId:D}:recovery-replace-receipt-proof");

        BaseResult<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> committedResult =
            await operation.ExecuteAsync(request, identity);
        committedResult.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>>>(
            committedResult is BaseFailure<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> failure
                ? $"{failure.Error.Code}:{failure.Error.Category}:{failure.Status}"
                : string.Empty);
        BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1> committed =
            committedResult.RequireValue();
        BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1> duplicate =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1> resolved =
            (await operation.ResolveAsync(identity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> collision =
            await operation.ExecuteAsync(request, identity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0x8D, 32).ToArray()),
            });

        committed.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.Result.Should().Be(committed.Result);
        resolved.Result.Should().Be(committed.Result);
        collision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");

        AuthRecoveryCodesForUserReadV1.Row code = (await service.Reads.ToArrayAsync(
            AuthRecoveryCodesForUserReadV1.Handle,
            new AuthRecoveryCodesForUserReadV1
            {
                TenantId = TenantId,
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            })).RequireValue().Should().ContainSingle().Subject;
        var consumeRequest = new AuthRecoveryCodeConsumeV1
        {
            TenantId = TenantId,
            UserId = userId,
            CodeId = replacements[0].Id,
            CodeDigest = replacements[0].CodeDigest,
            ExpectedCodeRevision = code.Revision,
            ExpectedUserRevision = committed.Result.UserRevision,
            SecurityStamp = "stamp-recovery-v3",
            ConcurrencyStamp = "concurrency-recovery-v3",
            OperationTime = Now.AddMinutes(1),
        };
        BaseInstalledModuleMutationHandle<AuthRecoveryCodeConsumeV1, AuthRecoveryCodeMutationResultV1>
            consumeOperation = service.ModuleMutations.Get(AuthRecoveryCodeConsumeOperationV1.Identity);
        BaseMutationRequestIdentity consumeIdentity = consumeOperation.CreateRequestIdentity(
            consumeRequest, $"user:{userId:D}:recovery-code:{code.Id}:consume-receipt-proof");

        BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1> consumed =
            (await consumeOperation.ExecuteAsync(consumeRequest, consumeIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1> consumedDuplicate =
            (await consumeOperation.ExecuteAsync(consumeRequest, consumeIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1> consumedResolved =
            (await consumeOperation.ResolveAsync(consumeIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>> consumeCollision =
            await consumeOperation.ExecuteAsync(consumeRequest, consumeIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0x8E, 32).ToArray()),
            });

        consumed.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        consumedDuplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        consumedResolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        consumedDuplicate.Result.Should().Be(consumed.Result);
        consumedResolved.Result.Should().Be(consumed.Result);
        consumeCollision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthRecoveryCodeMutationResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Audit_append_receipt_is_provider_consistent(bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "auth.tests",
        });
        var request = new AuthAuditAppendV1
        {
            AuditId = Guid.NewGuid(),
            TenantId = TenantId,
            OccurredAt = Now,
            Action = "receipt-proof",
            Category = "security",
            Success = true,
            SubjectUserId = null,
            SubjectSessionId = null,
            IpAddress = null,
            UserAgent = "auth-tests",
            FailureCode = null,
            CorrelationId = "audit-receipt-proof",
            Facts = CanonicalJson("{}"u8),
        };
        BaseInstalledModuleMutationHandle<AuthAuditAppendV1, AuthAuditAppendResultV1> operation =
            service.ModuleMutations.Get(AuthAuditAppendOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"audit:{request.AuditId:D}:receipt-proof");

        BaseModuleMutationExecutionResult<AuthAuditAppendResultV1> committed =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthAuditAppendResultV1> duplicate =
            (await operation.ExecuteAsync(request, identity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthAuditAppendResultV1> resolved =
            (await operation.ResolveAsync(identity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthAuditAppendResultV1>> collision =
            await operation.ExecuteAsync(request, identity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0x8F, 32).ToArray()),
            });

        committed.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        duplicate.Result.Should().Be(committed.Result);
        resolved.Result.Should().Be(committed.Result);
        collision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthAuditAppendResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_link_and_unlink_receipts_are_provider_consistent(bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        (Guid userId, AuthCreateUserResultV1 createdUser) = await CreateUserWithAuthorityAsync(
            provider, "login-receipt@test.invalid");
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "auth.tests",
        });
        Guid identityId = Guid.NewGuid();
        string loginId = Convert.ToHexStringLower(SHA256.HashData("login-receipt-proof"u8));
        var linkRequest = new AuthLoginLinkV1
        {
            TenantId = TenantId,
            UserId = userId,
            LoginId = loginId,
            IdentityId = identityId,
            ExpectedUserRevision = createdUser.Revision,
            LoginProvider = "receipt-provider",
            ProviderKey = "receipt-provider-key",
            ProviderDisplayName = "Receipt Provider",
            ProviderId = "receipt-provider-id",
            IdentityData = CanonicalJson("{}"u8),
            FederationSourceId = null,
            OperationTime = Now,
        };
        BaseInstalledModuleMutationHandle<AuthLoginLinkV1, AuthLoginLinkResultV1> linkOperation =
            service.ModuleMutations.Get(AuthLoginLinkOperationV1.Identity);
        BaseMutationRequestIdentity linkIdentity = linkOperation.CreateRequestIdentity(
            linkRequest, $"login:{loginId}:link-receipt-proof");

        BaseModuleMutationExecutionResult<AuthLoginLinkResultV1> linked =
            (await linkOperation.ExecuteAsync(linkRequest, linkIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthLoginLinkResultV1> linkedDuplicate =
            (await linkOperation.ExecuteAsync(linkRequest, linkIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthLoginLinkResultV1> linkedResolved =
            (await linkOperation.ResolveAsync(linkIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthLoginLinkResultV1>> linkCollision =
            await linkOperation.ExecuteAsync(linkRequest, linkIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0x90, 32).ToArray()),
            });

        linked.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        linkedDuplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        linkedResolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        linkedDuplicate.Result.Should().Be(linked.Result);
        linkedResolved.Result.Should().Be(linked.Result);
        linkCollision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthLoginLinkResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");

        var unlinkRequest = new AuthLoginUnlinkV1
        {
            TenantId = TenantId,
            UserId = userId,
            LoginId = loginId,
            IdentityId = identityId,
            ExpectedUserRevision = createdUser.Revision,
            ExpectedLoginRevision = linked.Result.LoginRevision,
            ExpectedIdentityRevision = linked.Result.IdentityRevision,
            OperationTime = Now.AddMinutes(1),
        };
        BaseInstalledModuleMutationHandle<AuthLoginUnlinkV1, AuthLoginUnlinkResultV1> unlinkOperation =
            service.ModuleMutations.Get(AuthLoginUnlinkOperationV1.Identity);
        BaseMutationRequestIdentity unlinkIdentity = unlinkOperation.CreateRequestIdentity(
            unlinkRequest, $"login:{loginId}:unlink-receipt-proof");

        BaseModuleMutationExecutionResult<AuthLoginUnlinkResultV1> unlinked =
            (await unlinkOperation.ExecuteAsync(unlinkRequest, unlinkIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthLoginUnlinkResultV1> unlinkedDuplicate =
            (await unlinkOperation.ExecuteAsync(unlinkRequest, unlinkIdentity)).RequireValue();
        BaseModuleMutationExecutionResult<AuthLoginUnlinkResultV1> unlinkedResolved =
            (await unlinkOperation.ResolveAsync(unlinkIdentity)).RequireValue();
        BaseResult<BaseModuleMutationExecutionResult<AuthLoginUnlinkResultV1>> unlinkCollision =
            await unlinkOperation.ExecuteAsync(unlinkRequest, unlinkIdentity with
            {
                Fingerprint = BaseMutationRequestFingerprint.Create(
                    Enumerable.Repeat((byte)0x91, 32).ToArray()),
            });

        unlinked.Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        unlinkedDuplicate.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        unlinkedResolved.Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        unlinkedDuplicate.Result.Should().Be(unlinked.Result);
        unlinkedResolved.Result.Should().Be(unlinked.Result);
        unlinkCollision.Should().BeOfType<BaseFailure<BaseModuleMutationExecutionResult<AuthLoginUnlinkResultV1>>>()
            .Which.Error.Code.Should().Be("base.runtime.request.fingerprintConflict");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rotation_consumes_the_predecessor_and_replays_the_replacement(bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
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
    public async Task Auth_secrets_are_available_only_through_registered_secret_projections()
    {
        const string passwordHash = "auth-secret-password-hash-do-not-disclose";
        const string securityStamp = "auth-secret-security-stamp-do-not-disclose";
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider,
            "confidentiality-proof@example.invalid");
        IUserStore<ApplicationUser> rawStore = scope.ServiceProvider
            .GetRequiredService<IUserStore<ApplicationUser>>();
        var passwordStore = (IUserPasswordStore<ApplicationUser>)rawStore;
        var securityStore = (IUserSecurityStampStore<ApplicationUser>)rawStore;
        ApplicationUser user = (await rawStore.FindByIdAsync(
            userId.ToString("D"), CancellationToken.None))!;
        await passwordStore.SetPasswordHashAsync(user, passwordHash, CancellationToken.None);
        await securityStore.SetSecurityStampAsync(user, securityStamp, CancellationToken.None);
        (await rawStore.UpdateAsync(user, CancellationToken.None)).Succeeded.Should().BeTrue();

        BaseSession owner = provider.GetRequiredService<IBaseSessionFactory>().For(
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Service,
                SubjectKind = AccessSubjectKind.ServicePrincipal,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.confidentiality.owner.v1",
            });
        AuthUserPasswordReadV1.Row secret = (await owner.Reads.FirstAsync(
            AuthUserPasswordReadV1.Handle,
            new AuthUserPasswordReadV1
            {
                TenantId = TenantId,
                UserId = userId,
            })).RequireValue()!;
        secret.PasswordHash.Should().Be(passwordHash);
        secret.SecurityStamp.Should().Be(securityStamp);

        BaseSession ordinary = provider.GetRequiredService<IBaseSessionFactory>().For(
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectKind = AccessSubjectKind.User,
                SubjectId = userId.ToString("D"),
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "browser",
            });
        BaseResult<AuthUserPasswordReadV1.Row?> denied = await ordinary.Reads.FirstAsync(
            AuthUserPasswordReadV1.Handle,
            new AuthUserPasswordReadV1
            {
                TenantId = TenantId,
                UserId = userId,
            });
        denied.Status.Should().Be(OperationStatus.NotFound,
            "an unauthorized secret projection must not disclose whether the user exists");
        var failure = denied.Should().BeOfType<BaseFailure<AuthUserPasswordReadV1.Row?>>()
            .Which;
        string outwardFailure = $"{failure.Error.Code}:{failure.Error.Message}";
        outwardFailure.Should().NotContain(passwordHash);
        outwardFailure.Should().NotContain(securityStamp);

        AuthUserByIdReadV1.Row ordinaryProjection = (await owner.Reads.FirstAsync(
            AuthUserByIdReadV1.Handle,
            new AuthUserByIdReadV1
            {
                TenantId = TenantId,
                UserId = userId,
            })).RequireValue()!;
        string outwardJson = System.Text.Json.JsonSerializer.Serialize(
            ordinaryProjection,
            AuthIdentityByIdReadJsonContext.Default.AuthUserByIdReadV1Row);
        outwardJson.Should().NotContain(passwordHash);
        outwardJson.Should().NotContain(securityStamp);
        Assert.DoesNotContain("passwordHash", outwardJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", outwardJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authoritative_backup_preserves_and_restores_secret_state()
    {
        const string preservedPassword = "auth-backup-secret-preserved-do-not-disclose";
        const string laterPassword = "auth-backup-secret-later-do-not-disclose";
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider,
            "backup-confidentiality@example.invalid");
        IUserStore<ApplicationUser> rawStore = scope.ServiceProvider
            .GetRequiredService<IUserStore<ApplicationUser>>();
        var passwordStore = (IUserPasswordStore<ApplicationUser>)rawStore;
        ApplicationUser user = (await rawStore.FindByIdAsync(
            userId.ToString("D"), CancellationToken.None))!;
        await passwordStore.SetPasswordHashAsync(
            user, preservedPassword, CancellationToken.None);
        (await rawStore.UpdateAsync(user, CancellationToken.None)).Succeeded.Should().BeTrue();

        var administrator = new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.confidentiality.backup.v1",
        };
        IHPDBaseAdministration administration = provider
            .GetRequiredService<IHPDBaseApplication>().Administration;
        using var artifact = new MemoryStream();
        BaseBackupManifest manifest = (await administration.CreateBackupAsync(
            artifact,
            new BaseBackupRequest
            {
                StoreId = "auth-refresh-tests",
                Principal = administrator,
            })).RequireValue();
        user = (await rawStore.FindByIdAsync(
            userId.ToString("D"), CancellationToken.None))!;
        await passwordStore.SetPasswordHashAsync(user, laterPassword, CancellationToken.None);
        (await rawStore.UpdateAsync(user, CancellationToken.None)).Succeeded.Should().BeTrue();

        await RestoreAsync(administration, administrator, artifact, manifest);
        BaseSession owner = provider.GetRequiredService<IBaseSessionFactory>().For(
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Service,
                SubjectKind = AccessSubjectKind.ServicePrincipal,
                SubjectId = "hpd.auth",
                CurrentTenantId = TenantId.ToString("D"),
                AuthSource = "hpd.auth.confidentiality.restore.v1",
            });
        AuthUserPasswordReadV1.Row restored = (await owner.Reads.FirstAsync(
            AuthUserPasswordReadV1.Handle,
            new AuthUserPasswordReadV1 { TenantId = TenantId, UserId = userId }))
            .RequireValue()!;
        restored.PasswordHash.Should().Be(preservedPassword);
        restored.PasswordHash.Should().NotBe(laterPassword);
        restored.ToString().Should().NotContain(preservedPassword);

        AuthUserByIdReadV1.Row ordinary = (await owner.Reads.FirstAsync(
            AuthUserByIdReadV1.Handle,
            new AuthUserByIdReadV1 { TenantId = TenantId, UserId = userId }))
            .RequireValue()!;
        string outwardJson = System.Text.Json.JsonSerializer.Serialize(
            ordinary, AuthIdentityByIdReadJsonContext.Default.AuthUserByIdReadV1Row);
        outwardJson.Should().NotContain(preservedPassword);
        Assert.DoesNotContain("passwordHash", outwardJson, StringComparison.OrdinalIgnoreCase);
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
        (await store.FindByIdAsync(userId.ToString("D"), CancellationToken.None)).Should().BeNull(
            "the committed tombstone must shut down ordinary Identity reads before cleanup bootstrap executes");
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
            Step = AuthCleanupStepV1.proveSubjectReady,
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

    [Fact]
    public async Task Role_cleanup_retires_private_subject_and_semantic_slot()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        IRoleStore<ApplicationRole> store = scope.ServiceProvider.GetRequiredService<IRoleStore<ApplicationRole>>();
        var role = new ApplicationRole("retired-role")
        {
            Id = Guid.NewGuid(),
            NormalizedName = "RETIRED-ROLE",
            ConcurrencyStamp = "retired-role-v1",
        };
        (await store.CreateAsync(role, CancellationToken.None)).Succeeded.Should().BeTrue();

        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.role.retirement.acquire.v1",
        });
        BaseSubjectReference<AuthRoleSubject> subject = (await service.Reads.FirstAsync(
            AuthRoleSubjectAcquisitionReadV1.Handle,
            new AuthRoleSubjectAcquisitionReadV1
            {
                RoleId = BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")),
            })).RequireValue()!.Reference;
        (await store.DeleteAsync(role, CancellationToken.None)).Succeeded.Should().BeTrue();

        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.role.retirement.v1",
        });
        (await system.Activations.GetWorker(AuthLifecycleActivationDeclarations.BootstrapRole.Identity)
            .RunOneAsync()).Value!.State.Should().Be(BaseActivationState.Succeeded);

        BaseInstalledActivationWorkerHandle<AuthRoleCleanupInputV1, AuthCleanupResultV1> cleanup =
            system.Activations.GetWorker(AuthCleanupActivationDeclarations.Role.Identity);
        BaseActivationState state;
        do
        {
            OperationResult<BaseActivationDispatchResult> dispatched = await cleanup.RunOneAsync();
            dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
            dispatched.Value!.Empty.Should().BeFalse();
            dispatched.Value.State.Should().NotBeNull();
            state = dispatched.Value.State!.Value;
        }
        while (state == BaseActivationState.YieldPending);
        state.Should().Be(BaseActivationState.Succeeded);

        OperationResult<BaseActivationDispatchResult> semanticRetirement = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.RetireRole.Identity).RunOneAsync();
        semanticRetirement.IsSuccess().Should().BeTrue(semanticRetirement.Error?.Code);
        semanticRetirement.Value!.State.Should().Be(BaseActivationState.Succeeded);

        BaseResult<AuthCleanupWorkReadV1.Row?> work = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.role,
                SubjectId = role.Id,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        work.RequireValue()!.State.Should().Be(AuthCleanupStateV1.complete);
        BaseResult<BaseRecord<AuthRoleRecordV1>> privateSubject = await system
            .Collection(AuthRoleRecordV1.Collection)
            .GetAsync(RecordId.Create(role.Id.ToString("D")));
        privateSubject.Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task Scheduled_reconciliation_and_immediate_bootstrap_converge_on_one_cleanup_lifetime()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider, "reconcile-user@example.invalid");
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.reconcile.acquire.v1",
        });
        BaseSubjectReference<AuthUserSubject> subject = (await service.Reads.FirstAsync(
            AuthUserSubjectAcquisitionReadV1.Handle,
            new AuthUserSubjectAcquisitionReadV1
            {
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            })).RequireValue()!.Reference;
        IUserStore<ApplicationUser> store = scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();
        ApplicationUser user = (await store.FindByIdAsync(userId.ToString("D"), CancellationToken.None))!;
        (await store.DeleteAsync(user, CancellationToken.None)).Succeeded.Should().BeTrue();

        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.reconcile.test.v1",
        });
        BaseInstalledActivationHandle<AuthCleanupReconcileInputV1, AuthCleanupReconcileResultV1> reconcile =
            system.Activations.Get(AuthLifecycleActivationDeclarations.Reconcile.Identity);
        BaseResult<BaseRecord<AuthMaintenanceCursorRecordV1>> initialCursor = await system
            .Collection(AuthMaintenanceCursorRecordV1.Collection)
            .GetAsync(RecordId.Create("hpd.auth.cleanup-reconcile.cursor.v1"));
        initialCursor.Status.Should().Be(OperationStatus.NotFound,
            initialCursor is BaseFailure<BaseRecord<AuthMaintenanceCursorRecordV1>> cursorFailure
                ? cursorFailure.Error.Code
                : null);
        BaseResult<BasePage<AuthTombstonedUsersForReconciliationReadV1.Row>> initialPage = await system.Reads.ExecuteAsync(
            AuthTombstonedUsersForReconciliationReadV1.Handle,
            new AuthTombstonedUsersForReconciliationReadV1(),
            BaseReadPageRequest.Create(1, 200));
        (initialPage is BaseSuccess<BasePage<AuthTombstonedUsersForReconciliationReadV1.Row>>).Should().BeTrue(
            initialPage is BaseFailure<BasePage<AuthTombstonedUsersForReconciliationReadV1.Row>> pageFailure
                ? pageFailure.Error.Code
                : null);
        initialPage.RequireValue().Items.Should().ContainSingle();
        BaseResult<AuthTombstonedUserSubjectForReconciliationReadV1.Row?> reacquired = await system.Reads.FirstAsync(
            AuthTombstonedUserSubjectForReconciliationReadV1.Handle,
            new AuthTombstonedUserSubjectForReconciliationReadV1
            {
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            });
        (reacquired is BaseSuccess<AuthTombstonedUserSubjectForReconciliationReadV1.Row?>).Should().BeTrue(
            reacquired is BaseFailure<AuthTombstonedUserSubjectForReconciliationReadV1.Row?> acquisitionFailure
                ? acquisitionFailure.Error.Code
                : null);
        reacquired.RequireValue().Should().NotBeNull();
        var reconcileInput = new AuthCleanupReconcileInputV1 { ContractVersion = 1 };
        OperationResult<BaseActivationEnqueueResult> enqueued = await reconcile.EnqueueAsync(
            reconcileInput,
            BaseMutationRequestIdentity.Create(
                "hpd.auth.cleanup.reconcile.tests", "reconcile", "first",
                BaseMutationRequestFingerprint.Create(SHA256.HashData("reconcile-first"u8))));
        enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);
        OperationResult<BaseActivationDispatchResult> reconciliation = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.Reconcile.Identity).RunOneAsync();
        reconciliation.IsSuccess().Should().BeTrue(reconciliation.Error?.Code);

        BaseResult<AuthCleanupWorkReadV1.Row?> work = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        BaseResult<BaseRecord<AuthMaintenanceCursorRecordV1>> cursorAfterReconcile = await system
            .Collection(AuthMaintenanceCursorRecordV1.Collection)
            .GetAsync(RecordId.Create("hpd.auth.cleanup-reconcile.cursor.v1"));
        reconciliation.Value!.State.Should().Be(BaseActivationState.Succeeded,
            cursorAfterReconcile is BaseFailure<BaseRecord<AuthMaintenanceCursorRecordV1>> cursorAfterFailure
                ? $"cursor: {cursorAfterFailure.Error.Code}; cleanup: {(work.RequireValue() is null ? "missing" : "exists")}" :
            work is BaseFailure<AuthCleanupWorkReadV1.Row?> workFailure
                ? workFailure.Error.Code
                : work.RequireValue() is null ? "cleanup work was not created" : "cleanup work exists");
        work.RequireValue().Should().NotBeNull();
        work.RequireValue()!.State.Should().Be(AuthCleanupStateV1.draining);
        RevisionToken cleanupRevision = work.RequireValue()!.Revision;

        OperationResult<BaseActivationDispatchResult> bootstrap = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.BootstrapUser.Identity).RunOneAsync();
        bootstrap.IsSuccess().Should().BeTrue(bootstrap.Error?.Code);
        bootstrap.Value!.State.Should().Be(BaseActivationState.Succeeded);
        BaseResult<BaseRecord<AuthMaintenanceCursorRecordV1>> cursor = await system
            .Collection(AuthMaintenanceCursorRecordV1.Collection)
            .GetAsync(RecordId.Create("hpd.auth.cleanup-reconcile.cursor.v1"));
        cursor.RequireValue().Value.PassGeneration.Should().BeGreaterThanOrEqualTo(1);
        cursor.RequireValue().Redacted.Should().BeTrue(
            "the confidential page digest is intentionally omitted from an ordinary system record read");
        cursor.RequireValue().Revision.Should().NotBeNull();
        cursor.RequireValue().Value.Id.Should().Be("hpd.auth.cleanup-reconcile.cursor.v1");
        cursor.RequireValue().Value.AfterTenantId.Should().BeNull();
        cursor.RequireValue().Value.AfterSubjectKind.Should().BeNull();
        cursor.RequireValue().Value.AfterSubjectId.Should().BeNull();

        provider.GetRequiredService<MutableTimeProvider>().Advance(TimeSpan.FromDays(2));
        BaseResult<AuthTombstonedUserSubjectForReconciliationReadV1.Row?> reacquiredAfterExpiry =
            await system.Reads.FirstAsync(
                AuthTombstonedUserSubjectForReconciliationReadV1.Handle,
                new AuthTombstonedUserSubjectForReconciliationReadV1
                {
                    UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
                });
        AuthTombstonedUserSubjectForReconciliationReadV1.Row subjectAfterExpiry =
            reacquiredAfterExpiry.RequireValue()!;
        subjectAfterExpiry.Reference.Should().Be(subject,
            "receipt expiry must not alter exported-subject lifetime authority");
        AuthCleanupWorkReadV1.Row workBeforeSecondEnsure = (await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            })).RequireValue()!;
        workBeforeSecondEnsure.UserSubject.Should().Be(subjectAfterExpiry.Reference);
        workBeforeSecondEnsure.RoleSubject.Should().BeNull();
        workBeforeSecondEnsure.SubjectId.Should().Be(userId);
        workBeforeSecondEnsure.SubjectKind.Should().Be(AuthCleanupSubjectKindV1.user);
        workBeforeSecondEnsure.TenantId.Should().Be(TenantId);
        workBeforeSecondEnsure.Incarnation.ToArray().Should().Equal(subject.Incarnation.ToArray());
        workBeforeSecondEnsure.WorkflowVersion.Should().Be(1);
        OperationResult<BaseActivationEnqueueResult> laterEnqueue = await reconcile.EnqueueAsync(
            reconcileInput,
            BaseMutationRequestIdentity.Create(
                "hpd.auth.cleanup.reconcile.tests", "reconcile", "after-receipt-expiry",
                BaseMutationRequestFingerprint.Create(SHA256.HashData(
                    "reconcile-after-receipt-expiry"u8))));
        laterEnqueue.IsSuccess().Should().BeTrue(laterEnqueue.Error?.Code);
        OperationResult<BaseActivationDispatchResult> laterReconciliation = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.Reconcile.Identity).RunOneAsync();
        laterReconciliation.IsSuccess().Should().BeTrue(laterReconciliation.Error?.Code);
        laterReconciliation.Value!.State.Should().Be(BaseActivationState.Succeeded,
            $"enqueued={laterEnqueue.Value!.ActivationId}; dispatched={laterReconciliation.Value.ActivationId}");

        BaseResult<AuthCleanupWorkReadV1.Row?> afterReceiptExpiry = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        afterReceiptExpiry.RequireValue().Should().NotBeNull();
        afterReceiptExpiry.RequireValue()!.Revision.Should().Be(cleanupRevision,
            "the AllowEither present branch must validate the lifetime without mutation after the initialization receipt expires");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconciliation_pages_more_than_one_cohort_and_wraps_the_durable_cursor(bool inMemory)
    {
        await using ServiceProvider provider = CreateProvider(inMemory);
        await InitializeAsync(
            provider,
            inMemory ? "hpd.base.inmemory.default" : "auth-refresh-tests",
            applySchema: !inMemory);
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.reconcile.page-seed.v1",
        });
        var subjects = new List<(Guid Id, BaseSubjectReference<AuthUserSubject> Reference)>(201);
        for (int index = 0; index < 201; index++)
        {
            Guid id = Guid.ParseExact((index + 1).ToString("x32", CultureInfo.InvariantCulture), "N");
            (Guid _, AuthCreateUserResultV1 authority) = await CreateUserWithAuthorityAsync(
                provider, $"reconcile-page-{index:D3}@example.invalid", id);
            BaseSubjectReference<AuthUserSubject> reference = (await service.Reads.FirstAsync(
                AuthUserSubjectAcquisitionReadV1.Handle,
                new AuthUserSubjectAcquisitionReadV1
                {
                    UserId = BaseRecordId<AuthUserRecordV1>.Create(id.ToString("D")),
                })).RequireValue()!.Reference;
            BaseResult<BaseSubjectTombstoneResult<AuthUserSubject>> tombstoned = await AuthSubjects.Users(service)
                .TombstoneAsync(new()
                {
                    Subject = reference,
                    ExpectedPrivateRevision = authority.Revision,
                    Identity = AuthBaseRuntime.MutationIdentity(
                        "hpd.auth.user-subject.tombstone.page-proof.v1", TenantId,
                        id.ToString("D"), authority.Revision.Value, reference.Incarnation.ToBase64Url()),
                });
            tombstoned.RequireValue().Fact.Fact.SubjectSequence.Should().Be(2,
                "the active publication is sequence one and its tombstone is sequence two");
            subjects.Add((id, reference));
        }
        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.reconcile.page-proof.v1",
        });
        Guid sharedId = subjects[0].Id;
        using IServiceScope roleScope = provider.CreateScope();
        IRoleStore<ApplicationRole> roleStore = roleScope.ServiceProvider
            .GetRequiredService<IRoleStore<ApplicationRole>>();
        var sameIdRole = new ApplicationRole("reconcile-same-id-role")
        {
            Id = sharedId,
            NormalizedName = "RECONCILE-SAME-ID-ROLE",
            ConcurrencyStamp = "reconcile-same-id-role-v1",
        };
        (await roleStore.CreateAsync(sameIdRole, CancellationToken.None)).Succeeded.Should().BeTrue();
        BaseSubjectReference<AuthRoleSubject> roleReference = (await service.Reads.FirstAsync(
            AuthRoleSubjectAcquisitionReadV1.Handle,
            new AuthRoleSubjectAcquisitionReadV1
            {
                RoleId = BaseRecordId<AuthRoleRecordV1>.Create(sharedId.ToString("D")),
            })).RequireValue()!.Reference;
        (await roleStore.DeleteAsync(sameIdRole, CancellationToken.None)).Succeeded.Should().BeTrue();

        BaseInstalledActivationHandle<AuthCleanupReconcileInputV1, AuthCleanupReconcileResultV1> reconcile =
            system.Activations.Get(AuthLifecycleActivationDeclarations.Reconcile.Identity);
        var input = new AuthCleanupReconcileInputV1 { ContractVersion = 1 };
        OperationResult<BaseActivationEnqueueResult> enqueued = await reconcile.EnqueueAsync(
            input,
            BaseMutationRequestIdentity.Create(
                "hpd.auth.cleanup.reconcile.tests", "reconcile", $"page-proof-{inMemory}",
                BaseMutationRequestFingerprint.Create(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"reconcile-page-proof-{inMemory}")))));
        enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);

        OperationResult<BaseActivationDispatchResult> dispatched = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.Reconcile.Identity).RunOneAsync();
        dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
        BaseResult<BaseRecord<AuthMaintenanceCursorRecordV1>> cursorRead = await system
            .Collection(AuthMaintenanceCursorRecordV1.Collection)
            .GetAsync(RecordId.Create("hpd.auth.cleanup-reconcile.cursor.v1"));
        dispatched.Value!.State.Should().Be(BaseActivationState.Succeeded,
            $"cursor status: {cursorRead.Status}; boundary: " +
            (cursorRead is BaseSuccess<BaseRecord<AuthMaintenanceCursorRecordV1>> cursorSuccess
                ? $"{cursorSuccess.Value.Value.PassGeneration}/" +
                  $"{cursorSuccess.Value.Value.AfterSubjectKind}/" +
                  $"{cursorSuccess.Value.Value.AfterSubjectId}"
                : "missing"));

        BaseRecord<AuthMaintenanceCursorRecordV1> cursor = cursorRead.RequireValue();
        cursor.Value.PassGeneration.Should().Be(2,
            "the first two pages advance pass one and the empty third page durably wraps it");
        cursor.Value.AfterTenantId.Should().BeNull();
        cursor.Value.AfterSubjectKind.Should().BeNull();
        cursor.Value.AfterSubjectId.Should().BeNull();

        foreach ((Guid id, BaseSubjectReference<AuthUserSubject> reference) in
            new[] { subjects[0], subjects[199], subjects[200] })
        {
            AuthCleanupWorkReadV1.Row? work = (await system.Reads.FirstAsync(
                AuthCleanupWorkReadV1.Handle,
                new AuthCleanupWorkReadV1
                {
                    TenantId = TenantId,
                    SubjectKind = AuthCleanupSubjectKindV1.user,
                    SubjectId = id,
                    Incarnation = BaseBinary.From(reference.Incarnation.ToArray()),
                })).RequireValue();
            work.Should().NotBeNull($"subject {id:D} must be repaired exactly once across the page boundary");
            work!.UserSubject.Should().Be(reference);
        }
        AuthCleanupWorkReadV1.Row? roleWork = (await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.role,
                SubjectId = sharedId,
                Incarnation = BaseBinary.From(roleReference.Incarnation.ToArray()),
            })).RequireValue();
        roleWork.Should().NotBeNull("equal user and role GUIDs are distinct reconciliation keys");
        roleWork!.RoleSubject.Should().Be(roleReference);

    }

    [Fact]
    public async Task User_cleanup_retires_private_subject_and_semantic_slot_after_retention()
    {
        await using ServiceProvider provider = CreateProvider();
        await InitializeAsync(provider);
        using IServiceScope scope = provider.CreateScope();
        Guid userId = await CreateUserAsync(scope.ServiceProvider, "retired-user@example.invalid");
        BaseSession service = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.retirement.acquire.v1",
        });
        BaseSubjectReference<AuthUserSubject> subject = (await service.Reads.FirstAsync(
            AuthUserSubjectAcquisitionReadV1.Handle,
            new AuthUserSubjectAcquisitionReadV1
            {
                UserId = BaseRecordId<AuthUserRecordV1>.Create(userId.ToString("D")),
            })).RequireValue()!.Reference;
        IUserStore<ApplicationUser> store = scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>();
        ApplicationUser user = (await store.FindByIdAsync(userId.ToString("D"), CancellationToken.None))!;
        (await store.DeleteAsync(user, CancellationToken.None)).Succeeded.Should().BeTrue();

        BaseSession system = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth",
            CurrentTenantId = TenantId.ToString("D"),
            AuthSource = "hpd.auth.cleanup.retirement.test.v1",
        });
        (await system.Activations.GetWorker(AuthLifecycleActivationDeclarations.BootstrapUser.Identity)
            .RunOneAsync()).Value!.State.Should().Be(BaseActivationState.Succeeded);
        BaseInstalledActivationWorkerHandle<AuthUserCleanupInputV1, AuthCleanupResultV1> cleanup =
            system.Activations.GetWorker(AuthCleanupActivationDeclarations.User.Identity);

        BaseActivationState state;
        do
        {
            OperationResult<BaseActivationDispatchResult> dispatched = await cleanup.RunOneAsync();
            dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
            dispatched.Value!.Empty.Should().BeFalse();
            dispatched.Value.State.Should().NotBeNull();
            state = dispatched.Value.State!.Value;
        }
        while (state == BaseActivationState.YieldPending);
        BaseResult<AuthCleanupWorkReadV1.Row?> afterRetention = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        state.Should().Be(BaseActivationState.Succeeded,
            afterRetention.RequireValue() is { } retained
                ? $"cleanup stopped at {retained.State}/{retained.Step}/{retained.ChunkOrdinal}"
                : "cleanup work disappeared");

        provider.GetRequiredService<MutableTimeProvider>().Advance(TimeSpan.FromDays(31));
        do
        {
            OperationResult<BaseActivationDispatchResult> dispatched = await cleanup.RunOneAsync();
            dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
            dispatched.Value!.Empty.Should().BeFalse();
            dispatched.Value.State.Should().NotBeNull();
            state = dispatched.Value.State!.Value;
        }
        while (state == BaseActivationState.YieldPending);
        BaseResult<AuthCleanupWorkReadV1.Row?> beforeSemanticRetirement = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        state.Should().Be(BaseActivationState.Succeeded,
            beforeSemanticRetirement.RequireValue() is { } finalWork
                ? $"cleanup stopped at {finalWork.State}/{finalWork.Step}/{finalWork.ChunkOrdinal}"
                : "cleanup work disappeared");

        OperationResult<BaseActivationDispatchResult> semanticRetirement = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.RetireUser.Identity).RunOneAsync();
        semanticRetirement.IsSuccess().Should().BeTrue(semanticRetirement.Error?.Code);
        semanticRetirement.Value!.State.Should().Be(BaseActivationState.Succeeded);

        BaseResult<AuthCleanupWorkReadV1.Row?> work = await system.Reads.FirstAsync(
            AuthCleanupWorkReadV1.Handle,
            new AuthCleanupWorkReadV1
            {
                TenantId = TenantId,
                SubjectKind = AuthCleanupSubjectKindV1.user,
                SubjectId = userId,
                Incarnation = BaseBinary.From(subject.Incarnation.ToArray()),
            });
        work.RequireValue()!.State.Should().Be(AuthCleanupStateV1.complete);
        BaseResult<BaseRecord<AuthUserRecordV1>> privateSubject = await system
            .Collection(AuthUserRecordV1.Collection)
            .GetAsync(RecordId.Create(userId.ToString("D")));
        privateSubject.Status.Should().Be(OperationStatus.NotFound);
    }

    private static async Task<Guid> CreateUserAsync(
        IServiceProvider services,
        string email = "refresh@test.invalid") =>
        (await CreateUserWithAuthorityAsync(services, email)).UserId;

    private static async Task<AuthCreateUserResultV1> CreateUserThroughBaseAsync(
        BaseSession session,
        Guid id,
        string email)
    {
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
        return (await operation.ExecuteAsync(
            request, operation.CreateRequestIdentity(request, $"user:{id:D}:create")))
            .RequireValue().Result;
    }

    private static async Task<BaseSemanticActivationControlDescriptor>
        ReadUserSemanticControlAsync(
            IHPDBaseAdministration administration,
            PrincipalContext principal,
            string storeId = "auth-refresh-tests")
    {
        BaseSemanticActivationKeyDefinition definition =
            AuthCleanupSemanticActivations.User.Definition;
        return (await administration.ReadSemanticActivationControlAsync(
            storeId,
            principal,
            new BaseSemanticActivationDefinitionKey
            {
                Id = definition.Id,
                Version = definition.Version,
                Checksum = definition.Checksum,
            })).RequireValue();
    }

    private static async Task RestoreAsync(
        IHPDBaseAdministration administration,
        PrincipalContext principal,
        MemoryStream artifact,
        BaseBackupManifest manifest)
    {
        artifact.Position = 0;
        BaseSemanticActivationControlDescriptor current = await ReadUserSemanticControlAsync(
            administration, principal);
        current.Ready.Should().BeTrue();
        BaseRestoreResult restored = (await administration.RestoreAsync(
            artifact,
            new BaseRestoreRequest
            {
                StoreId = "auth-refresh-tests",
                Principal = principal,
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
                ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
            })).RequireValue();
        restored.InstalledStoreIdentityDigest.Should().Be(manifest.StoreIdentityDigest);
    }

    private static async Task<(Guid UserId, AuthCreateUserResultV1 Authority)> CreateUserWithAuthorityAsync(
        IServiceProvider services,
        string email,
        Guid? requestedId = null)
    {
        Guid id = requestedId ?? Guid.NewGuid();
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
        return (id, result.RequireValue().Result);
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

    private static async Task InitializeAsync(
        IServiceProvider services,
        string storeId = "auth-refresh-tests",
        bool applySchema = true)
    {
        if (applySchema)
        {
            IBaseSchemaManager schemas = services.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest
            {
                StoreId = storeId,
            });
            if (!planned.IsSuccess())
                throw new InvalidOperationException($"{planned.Error?.Code}:{planned.Error?.Category}:{planned.Status}");
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(new BaseSchemaApplyRequest
            {
                ProtectedArtifact = planned.Value!.ProtectedArtifact,
            });
            if (!applied.IsSuccess())
                throw new InvalidOperationException($"{applied.Error?.Code}:{applied.Error?.Category}:{applied.Status}");
        }
        OperationResult<BaseApplicationReadiness> result = await services
            .GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        if (!result.IsSuccess())
            throw new InvalidOperationException($"{result.Error?.Code}:{result.Error?.Category}:{result.Status}");
    }

    private static async Task EnqueueAndRunDeliveryExpirationAsync(
        BaseSession system,
        BaseInstalledActivationHandle<AuthExpirationTriggerInputV1, AuthExpirationResultV1> activation,
        AuthExpirationTriggerInputV1 input,
        string attempt)
    {
        OperationResult<BaseActivationEnqueueResult> enqueued = await activation.EnqueueAsync(
            input,
            BaseMutationRequestIdentity.Create(
                "hpd.auth.expiration.test",
                "maintenance-cleanup",
                attempt,
                BaseMutationRequestFingerprint.Create(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"maintenance-cleanup-{attempt}")))));
        enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);
        OperationResult<BaseActivationDispatchResult> dispatched = await system.Activations
            .GetWorker(AuthLifecycleActivationDeclarations.Deliveries.Identity)
            .RunOneAsync();
        dispatched.IsSuccess().Should().BeTrue(dispatched.Error?.Code);
        dispatched.Value!.State.Should().Be(BaseActivationState.Succeeded);
    }

    private static ServiceProvider CreateProvider(bool inMemory = false)
    {
        var services = new ServiceCollection();
        var database = new TestDatabase();
        services.AddSingleton(database);
        services.AddLogging();
        services.AddSingleton<IdentityErrorDescriber>();
        services.AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>();
        services.AddSingleton(new HPDAuthOptions { AppName = "HPD Auth Infrastructure Tests" });
        var clock = new MutableTimeProvider(Now);
        services.AddSingleton(clock);
        services.AddSingleton<TimeProvider>(clock);
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
            if (inMemory)
                builder.ConfigureInMemoryStore(static _ => { });
            else
                builder.UseStore(SqliteStore.Configure(options =>
                {
                    options.DataSource = database.Path;
                    options.StoreId = "auth-refresh-tests";
                    options.AdministrationEnabled = true;
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
                LogicalStoreId = inMemory
                    ? "hpd.base.inmemory.default"
                    : "auth-refresh-tests",
                EnabledRestoreMode = inMemory
                    ? null
                    : BaseActivationRestoreMode.InPlaceRecovery,
                SelectionGeneration = 1,
                Identity = BaseMutationRequestIdentity.Create("auth.refresh.tests", "restore", "v1",
                    BaseMutationRequestFingerprint.Create(SHA256.HashData("auth.refresh.tests.restore.v1"u8))),
                Checksum = [],
            });
        });
        services.AddHPDAuthBaseStores();
        return services.BuildServiceProvider();
    }

    private static ValueTask<BaseTestHost> CreateFaultInjectableAuthHostAsync(string database) =>
        BaseTestHost.CreateAsync(builder =>
        {
            builder.ConfigureSchema(options =>
            {
                options.ApplicationId = "hpd.auth.identity.v1";
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
            });
            builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1,
                Key = Enumerable.Repeat((byte)0x41, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            });
            builder.UseStore(SqliteStore.Configure(options =>
            {
                options.StoreId = "sqlite";
                options.DataSource = database;
                options.AdministrationEnabled = true;
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
                LogicalStoreId = "sqlite",
                EnabledRestoreMode = BaseActivationRestoreMode.InPlaceRecovery,
                SelectionGeneration = 1,
                Identity = BaseMutationRequestIdentity.Create(
                    "hpd.auth.cleanup.response-loss", "restore", "v1",
                    BaseMutationRequestFingerprint.Create(SHA256.HashData(
                        "hpd.auth.cleanup.response-loss.restore.v1"u8))),
                Checksum = [],
            });
        }, Now);

    private static HPDBaseDataProtectionXmlRepository CreateDataProtectionRepository(
        ServiceProvider provider,
        TimeSpan storeTimeout,
        TimeSpan shutdownTimeout,
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> persistenceOverride) => new(
            provider.GetRequiredService<IBaseSessionFactory>(),
            provider.GetRequiredService<HPDAuthOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<AuthDataProtectionCacheInvalidationState>(),
            storeTimeout,
            shutdownTimeout,
            persistenceOverride);

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
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

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
