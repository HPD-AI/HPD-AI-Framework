using System.Collections.Concurrent;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Tests;

public sealed class ProviderAuthenticationCoordinatorConcurrencyTests
{
    [Theory]
    [InlineData(DeviceFault.ExtendedExpiry, "TransactionInvalid")]
    [InlineData(DeviceFault.InvalidTerminal, "AuthorizationProgressInvalid")]
    public async Task DeviceAuthorization_RejectsInvalidStrategyProgress(DeviceFault fault, string diagnosticCode)
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        var strategy = new DeviceStrategy(fault);
        await using var store = new InMemoryProviderAuthorizationStore();
        await using var transactions = new InMemoryProviderAuthorizationTransactionStore();
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory", Store = store, Protector = new SessionProtector(), IsDefault = true
            }]),
            transactions: transactions,
            transactionProtector: new TransactionProtector(),
            timeProvider: time,
            expirationSkew: TimeSpan.Zero);
        var plan = await coordinator.PrepareAsync(Request());
        var challenge = await coordinator.BeginAuthorizationAsync(new BeginProviderAuthorizationRequest
        {
            Plan = plan,
            Flow = ProviderAuthorizationFlow.DeviceAuthorization
        });

        var error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() =>
            coordinator.AdvanceDeviceAuthorizationAsync(plan, challenge.TransactionId).AsTask());
        Assert.Equal(diagnosticCode, error.DiagnosticCode);
        Assert.Equal(ProviderDeviceAuthorizationStatusKind.Pending,
            (await coordinator.GetDeviceAuthorizationStatusAsync(plan, challenge.TransactionId)).Status);
    }

    [Fact]
    public async Task DeviceAuthorization_PersistedCommitResumesWithoutRepeatingProviderExchange()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        var strategy = new DeviceStrategy();
        await using var store = new InMemoryProviderAuthorizationStore();
        await using var innerTransactions = new InMemoryProviderAuthorizationTransactionStore();
        var transactions = new CancelAfterSaveTransactionStore(innerTransactions);
        var protector = new TransactionProtector();
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory", Store = store, Protector = new SessionProtector(), IsDefault = true
            }]),
            transactions: transactions,
            transactionProtector: protector,
            timeProvider: time,
            expirationSkew: TimeSpan.Zero);
        var plan = await coordinator.PrepareAsync(Request());
        var challenge = await coordinator.BeginAuthorizationAsync(new BeginProviderAuthorizationRequest
        {
            Plan = plan,
            Flow = ProviderAuthorizationFlow.DeviceAuthorization
        });
        await coordinator.AdvanceDeviceAuthorizationAsync(plan, challenge.TransactionId);
        time.Advance(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        transactions.CancelAfterNextSave = cancellation;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.AdvanceDeviceAuthorizationAsync(plan, challenge.TransactionId, cancellation.Token).AsTask());
        Assert.Equal(2, strategy.AdvanceCount);

        var resumed = await coordinator.AdvanceDeviceAuthorizationAsync(plan, challenge.TransactionId);
        Assert.Equal(ProviderDeviceAuthorizationStatusKind.Authorized, resumed.Status);
        Assert.Equal(2, strategy.AdvanceCount);
    }

    [Fact]
    public async Task DeviceAuthorization_PendingRevisionCanResumeWithoutRedirectResolver()
    {
        var strategy = new DeviceStrategy();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        await using var store = new InMemoryProviderAuthorizationStore();
        await using var transactions = new InMemoryProviderAuthorizationTransactionStore();
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory", Store = store, Protector = new SessionProtector(), IsDefault = true
            }]),
            transactions: transactions,
            transactionProtector: new TransactionProtector(),
            timeProvider: time,
            expirationSkew: TimeSpan.Zero);
        var plan = await coordinator.PrepareAsync(Request());
        var challenge = await coordinator.BeginAuthorizationAsync(new BeginProviderAuthorizationRequest
        {
            Plan = plan,
            Flow = ProviderAuthorizationFlow.DeviceAuthorization
        });

        Assert.IsType<DeviceAuthorizationChallenge>(challenge);
        Assert.Equal(ProviderDeviceAuthorizationStatusKind.Pending,
            (await coordinator.GetDeviceAuthorizationStatusAsync(plan, challenge.TransactionId)).Status);
        var pending = await coordinator.AdvanceDeviceAuthorizationAsync(plan, challenge.TransactionId);
        Assert.Equal(ProviderDeviceAuthorizationStatusKind.Pending, pending.Status);
        time.Advance(TimeSpan.FromSeconds(1));
        var authorized = await coordinator.AdvanceDeviceAuthorizationAsync(plan, challenge.TransactionId);
        Assert.Equal(ProviderDeviceAuthorizationStatusKind.Authorized, authorized.Status);
        await using var lease = await coordinator.AcquireAsync(plan);
        Assert.Equal("device-access",
            Assert.IsType<ProviderCredential.BearerToken>(lease.Credential).Value.Value.ToString());

        var canceled = await coordinator.BeginAuthorizationAsync(new BeginProviderAuthorizationRequest
        {
            Plan = plan,
            Flow = ProviderAuthorizationFlow.DeviceAuthorization
        });
        await coordinator.CancelDeviceAuthorizationAsync(plan, canceled.TransactionId);
        var missing = await Assert.ThrowsAsync<ProviderAuthenticationException>(() =>
            coordinator.GetDeviceAuthorizationStatusAsync(plan, canceled.TransactionId).AsTask());
        Assert.Equal("TransactionMissing", missing.DiagnosticCode);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("tenant")]
    [InlineData("principal")]
    [InlineData("audience")]
    [InlineData("scope")]
    public async Task Prepare_RejectsStrategyChangesToProtectedIsolationBoundary(string boundary)
    {
        var strategy = new RefreshStrategy(boundary);
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory",
                Store = new InMemoryProviderAuthorizationStore(),
                Protector = new SessionProtector(),
                IsDefault = true
            }]));

        var error = await Assert.ThrowsAsync<ProviderAuthenticationException>(
            () => coordinator.PrepareAsync(Request()).AsTask());

        Assert.Equal("NormalizationInvalid", error.DiagnosticCode);
    }

    [Fact]
    public async Task InvalidSplitCallback_DoesNotConsumeTheLegitimateTransaction()
    {
        var strategy = new RefreshStrategy();
        var protector = new SessionProtector();
        await using var store = new InMemoryProviderAuthorizationStore();
        await using var transactions = new InMemoryProviderAuthorizationTransactionStore();
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory", Store = store, Protector = protector, IsDefault = true
            }]),
            transactions: transactions,
            transactionProtector: new TransactionProtector(),
            redirectUriResolver: _ => new Uri("http://127.0.0.1/callback"),
            expirationSkew: TimeSpan.Zero);
        var plan = await coordinator.PrepareAsync(Request());
        var challenge = await coordinator.BeginAuthorizationAsync(new BeginProviderAuthorizationRequest
        {
            Plan = plan,
            Flow = ProviderAuthorizationFlow.AuthorizationCodePkce
        });

        var invalid = await Assert.ThrowsAsync<ProviderAuthenticationException>(() =>
            coordinator.CompleteBrowserAuthorizationAsync(plan, new BrowserAuthorizationResponse
            {
                TransactionId = "wrong-transaction",
                CallbackUri = new Uri("http://127.0.0.1/callback?code=redacted")
            }).AsTask());
        Assert.Equal("TransactionMissing", invalid.DiagnosticCode);

        await coordinator.CompleteBrowserAuthorizationAsync(plan, new BrowserAuthorizationResponse
        {
            TransactionId = challenge.TransactionId,
            CallbackUri = new Uri("http://127.0.0.1/callback?code=redacted")
        });
        await using var lease = await coordinator.AcquireAsync(plan);
        Assert.Equal("authorized-access",
            Assert.IsType<ProviderCredential.BearerToken>(lease.Credential).Value.Value.ToString());
    }

    [Fact]
    public async Task ConcurrentColdAcquisition_PerformsOneHostInteractionAndOneAuthorizationCommit()
    {
        var strategy = new RefreshStrategy();
        var protector = new SessionProtector();
        var transactionProtector = new TransactionProtector();
        var interaction = new Interaction();
        await using var store = new InMemoryProviderAuthorizationStore();
        await using var transactions = new InMemoryProviderAuthorizationTransactionStore();
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory", Store = store, Protector = protector, IsDefault = true
            }]),
            transactions: transactions,
            transactionProtector: transactionProtector,
            interaction: interaction,
            redirectUriResolver: _ => new Uri("http://127.0.0.1/callback"),
            activation: ProviderAuthorizationActivation.AllowDuringClientAcquisition,
            expirationSkew: TimeSpan.Zero);
        var plan = await coordinator.PrepareAsync(Request());

        var leases = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => coordinator.AcquireAsync(plan).AsTask()));
        try
        {
            Assert.Equal(1, strategy.AuthorizationCount);
            Assert.Equal(1, interaction.Count);
            Assert.Single(leases.Select(value => value.Generation).Distinct());
        }
        finally
        {
            foreach (var lease in leases)
                await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentExpiredAcquisition_PerformsOneRefreshAndSharesCommittedGeneration()
    {
        var request = Request();
        var strategy = new RefreshStrategy();
        var protector = new SessionProtector();
        await using var store = new InMemoryProviderAuthorizationStore();
        var normalized = await strategy.NormalizeAsync(request);
        await using (var expired = Session("old-access", "refresh-one", DateTimeOffset.UtcNow.AddMinutes(-5)))
        await using (var envelope = await protector.ProtectAsync(normalized.Identity, expired))
        {
            var write = await store.TrySaveAsync(normalized.Identity, null, envelope);
            Assert.IsType<ProviderAuthorizationWriteResult.Written>(write);
        }

        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory",
                Store = store,
                Protector = protector,
                IsDefault = true
            }]),
            expirationSkew: TimeSpan.Zero);
        var plan = await coordinator.PrepareAsync(request);

        var leases = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => coordinator.AcquireAsync(plan).AsTask()));
        try
        {
            Assert.Equal(1, strategy.RefreshCount);
            Assert.Single(leases.Select(value => value.Generation).Distinct());
            Assert.All(leases, lease => Assert.Equal(
                "new-access",
                Assert.IsType<ProviderCredential.BearerToken>(lease.Credential).Value.Value.ToString()));
        }
        finally
        {
            foreach (var lease in leases)
                await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task IndependentCoordinators_ResolveRefreshRaceThroughStoreCas()
    {
        var request = Request();
        var strategy = new RefreshStrategy();
        var protector = new SessionProtector();
        await using var store = new InMemoryProviderAuthorizationStore();
        var normalized = await strategy.NormalizeAsync(request);
        await using (var expired = Session("old-access", "refresh-one", DateTimeOffset.UtcNow.AddMinutes(-5)))
        await using (var envelope = await protector.ProtectAsync(normalized.Identity, expired))
        {
            Assert.IsType<ProviderAuthorizationWriteResult.Written>(
                await store.TrySaveAsync(normalized.Identity, null, envelope));
        }

        ProviderAuthenticationCoordinator CreateCoordinator() => new(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory",
                Store = store,
                Protector = protector,
                IsDefault = true
            }]),
            expirationSkew: TimeSpan.Zero);

        var first = CreateCoordinator();
        var second = CreateCoordinator();
        var firstPlan = await first.PrepareAsync(request);
        var secondPlan = await second.PrepareAsync(request);
        var leases = await Task.WhenAll(
            first.AcquireAsync(firstPlan).AsTask(),
            second.AcquireAsync(secondPlan).AsTask());
        try
        {
            Assert.Equal(leases[0].Generation, leases[1].Generation);
            Assert.All(leases, lease => Assert.Equal(
                "new-access",
                Assert.IsType<ProviderCredential.BearerToken>(lease.Credential).Value.Value.ToString()));
        }
        finally
        {
            foreach (var lease in leases)
                await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task InvalidGrant_DeletesExpiredSessionAndRequiresExplicitReauthorization()
    {
        var request = Request();
        var strategy = new RefreshStrategy(invalidGrant: true);
        var protector = new SessionProtector();
        await using var store = new InMemoryProviderAuthorizationStore();
        var normalized = await strategy.NormalizeAsync(request);
        await using (var expired = Session("old-access", "revoked-refresh", DateTimeOffset.UtcNow.AddMinutes(-5)))
        await using (var envelope = await protector.ProtectAsync(normalized.Identity, expired))
            Assert.IsType<ProviderAuthorizationWriteResult.Written>(await store.TrySaveAsync(normalized.Identity, null, envelope));
        var coordinator = Coordinator(strategy, store, protector, expirationSkew: TimeSpan.Zero);
        var plan = await coordinator.PrepareAsync(request);

        await Assert.ThrowsAsync<ProviderInteractionRequiredException>(() => coordinator.AcquireAsync(plan).AsTask());

        Assert.Null(await store.LoadAsync(normalized.Identity));
        Assert.Equal(ProviderAuthorizationStatusKind.Disconnected, (await coordinator.GetStatusAsync(plan)).Status);
    }

    [Fact]
    public async Task StoredSessionWithWrongIssuer_IsRejectedBeforeCredentialCreation()
    {
        var request = Request();
        var strategy = new RefreshStrategy();
        var protector = new SessionProtector();
        await using var store = new InMemoryProviderAuthorizationStore();
        var normalized = await strategy.NormalizeAsync(request);
        await using (var wrongIssuer = Session("secret-access", "refresh", DateTimeOffset.UtcNow.AddHours(1), "https://attacker.invalid"))
        await using (var envelope = await protector.ProtectAsync(normalized.Identity, wrongIssuer))
            Assert.IsType<ProviderAuthorizationWriteResult.Written>(await store.TrySaveAsync(normalized.Identity, null, envelope));
        var coordinator = Coordinator(strategy, store, protector);
        var plan = await coordinator.PrepareAsync(request);

        var error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => coordinator.AcquireAsync(plan).AsTask());

        Assert.Equal("IssuerMismatch", error.DiagnosticCode);
        Assert.DoesNotContain("secret-access", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InjectedTimeProvider_DeterministicallyExpiresAuthorizationTransaction()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        var strategy = new RefreshStrategy();
        await using var store = new InMemoryProviderAuthorizationStore();
        await using var transactions = new InMemoryProviderAuthorizationTransactionStore();
        var coordinator = new ProviderAuthenticationCoordinator(
            new TestDictionarySecretResolver(),
            strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
            stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
            {
                Identity = "memory", Store = store, Protector = new SessionProtector(), IsDefault = true
            }]),
            transactions: transactions,
            transactionProtector: new TransactionProtector(),
            redirectUriResolver: _ => new Uri("http://127.0.0.1/callback"),
            timeProvider: time);
        var plan = await coordinator.PrepareAsync(Request());
        var challenge = await coordinator.BeginAuthorizationAsync(new BeginProviderAuthorizationRequest
        {
            Plan = plan,
            Flow = ProviderAuthorizationFlow.AuthorizationCodePkce
        });
        time.Advance(TimeSpan.FromMinutes(6));

        var error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() =>
            coordinator.CompleteBrowserAuthorizationAsync(plan, new BrowserAuthorizationResponse
            {
                TransactionId = challenge.TransactionId,
                CallbackUri = new Uri("http://127.0.0.1/callback?code=redacted")
            }).AsTask());

        Assert.Equal("TransactionExpired", error.DiagnosticCode);
    }

    private static ProviderAuthenticationCoordinator Coordinator(
        RefreshStrategy strategy, InMemoryProviderAuthorizationStore store, SessionProtector protector,
        TimeSpan? expirationSkew = null) => new(
        new TestDictionarySecretResolver(),
        strategies: new ProviderAuthenticationStrategyRegistry([strategy]),
        stores: new ProviderAuthorizationStoreRegistry([new ProviderAuthorizationStoreRegistration
        {
            Identity = "memory", Store = store, Protector = protector, IsDefault = true
        }]),
        expirationSkew: expirationSkew);

    private static ProviderCredentialRequest Request() => new()
    {
        ProviderKey = "oauth-test",
        BackendKey = "platform",
        Family = ProviderClientFamily.Chat,
        Authentication = new OAuthProviderAuthentication { AccountId = "account", Scopes = ["chat"] },
        AuthorizationScope = new ProviderAuthorizationScope { TrustDomainId = "tenant-a", TenantId = "one" },
        Audience = new ProviderCredentialAudience { Audience = "api", Scopes = ["chat"] }
    };

    private static ProviderAuthorizationSession Session(
        string access, string refresh, DateTimeOffset expiresAt, string issuer = "https://issuer.test") => new()
    {
        SchemaVersion = "test-v1",
        Secrets = new SecretSet(access, refresh),
        TokenType = "Bearer",
        ExpiresAt = expiresAt,
        GrantedScopes = ["chat"],
        ClientId = "client",
        TokenEndpointAuthenticationMethod = "none",
        AuthorizationServer = issuer
    };

    private sealed class RefreshStrategy : IProviderAuthenticationStrategy
    {
        private readonly string? _changedBoundary;
        private readonly bool _invalidGrant;
        private int _refreshCount;
        private int _authorizationCount;
        public RefreshStrategy(string? changedBoundary = null, bool invalidGrant = false)
        {
            _changedBoundary = changedBoundary;
            _invalidGrant = invalidGrant;
        }
        public int RefreshCount => Volatile.Read(ref _refreshCount);
        public int AuthorizationCount => Volatile.Read(ref _authorizationCount);
        public ProviderAuthenticationStrategyDescriptor Descriptor { get; } = new()
        {
            StrategyId = new ProviderAuthenticationStrategyId("oauth-test:v1"),
            ProviderKey = "oauth-test",
            BackendKey = "platform",
            Kind = ProviderAuthenticationKind.OAuth,
            Flows = [ProviderAuthorizationFlow.AuthorizationCodePkce],
            SupportsRefresh = true,
            SupportsRevocation = false
        };

        public ValueTask<NormalizedProviderAuthorizationRequest> NormalizeAsync(
            ProviderCredentialRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new NormalizedProviderAuthorizationRequest
            {
                Original = request,
                Identity = new ProviderAuthorizationIdentity
                {
                    ProviderKey = request.ProviderKey,
                    BackendKey = request.BackendKey,
                    AccountId = _changedBoundary == "account" ? "other-account" : "account",
                    AuthorizationServer = "https://issuer.test",
                    ClientIdentity = "client",
                    TrustDomainId = request.AuthorizationScope.TrustDomainId,
                    TenantId = _changedBoundary == "tenant" ? "other-tenant" : request.AuthorizationScope.TenantId,
                    PrincipalId = _changedBoundary == "principal" ? "other-principal" : request.AuthorizationScope.PrincipalId,
                    Resource = request.Audience.Resource?.AbsoluteUri,
                    Audience = _changedBoundary == "audience" ? "other-api" : request.Audience.Audience
                },
                Grant = new ProviderAuthorizationGrant
                {
                    GrantIdentity = HashGrant(request.Audience.Resource?.AbsoluteUri, request.Audience.Audience, Hash("chat")),
                    RequestedScopes = _changedBoundary == "scope" ? ["other"] : ["chat"],
                    RequestedScopeSetIdentity = _changedBoundary == "scope" ? Hash("other") : Hash("chat"),
                    Audience = new ProviderCredentialAudience { Audience = "api", Scopes = ["chat"] }
                }
            });

        private static string HashGrant(string? resource, string? audience, string scopeIdentity) =>
            Hash($"{resource}|{audience}|{scopeIdentity}");

        private static string Hash(string value) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

        public ValueTask<ProviderAuthorizationRefreshResult> RefreshAsync(
            ProviderAuthorizationIdentity identity, ProviderAuthorizationSession current,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refreshCount);
            if (_invalidGrant)
                throw new ProviderAuthenticationException(
                    ProviderAuthenticationFailureKind.InvalidGrant, identity.ProviderKey, identity.BackendKey,
                    ProviderClientFamily.Chat, "redacted", "The refresh grant is no longer valid.",
                    interactionCanResolve: true, diagnosticCode: "InvalidGrant");
            return ValueTask.FromResult(new ProviderAuthorizationRefreshResult
            {
                Secrets = new RefreshSecrets("new-access"),
                TokenType = "Bearer",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                GrantedScopes = ["chat"],
                RefreshTokenDisposition = ProviderRefreshTokenDisposition.RetainCurrent
            });
        }

        public ValueTask<ProviderCredential> CreateCredentialAsync(
            ProviderAuthorizationIdentity identity, ProviderAuthorizationSession current,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<ProviderCredential>(
                new ProviderCredential.BearerToken(new Buffer(current.Secrets.AccessToken.Value.Span)));

        public ValueTask<ProviderAuthorizationStart> BeginAuthorizationAsync(ProviderAuthorizationBeginContext context, CancellationToken cancellationToken = default)
        {
            var browser = Assert.IsType<BrowserProviderAuthorizationBeginContext>(context);
            Interlocked.Increment(ref _authorizationCount);
            var transactionId = Guid.NewGuid().ToString("N");
            var expiresAt = context.TimeProvider.GetUtcNow().AddMinutes(5);
            return ValueTask.FromResult(new ProviderAuthorizationStart
            {
                TransactionState = new ProviderAuthorizationTransactionState
                {
                    TransactionId = transactionId,
                    Identity = context.Request.Identity,
                    StrategyId = Descriptor.StrategyId,
                    Flow = ProviderAuthorizationFlow.AuthorizationCodePkce,
                    ExpiresAt = expiresAt,
                    ProviderState = new SensitiveBuffer([1, 2, 3])
                },
                Challenge = new BrowserAuthorizationChallenge
                {
                    TransactionId = transactionId,
                    ProviderKey = Descriptor.ProviderKey,
                    BackendKey = Descriptor.BackendKey,
                    AccountId = context.Request.Identity.AccountId,
                    ExpiresAt = expiresAt,
                    AuthorizationUri = new Uri("https://issuer.test/authorize"),
                    RedirectUri = browser.RedirectUri
                }
            });
        }
        public ValueTask ValidateBrowserAuthorizationResponseAsync(ProviderAuthorizationTransactionState transaction, BrowserAuthorizationResponse response, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask<ProviderAuthorizationSession> CompleteBrowserAuthorizationAsync(ProviderAuthorizationTransactionState transaction, BrowserAuthorizationResponse response, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Session("authorized-access", "refresh-one", DateTimeOffset.UtcNow.AddHours(1)));
        public ValueTask<ProviderDeviceAuthorizationProgress> AdvanceDeviceAuthorizationAsync(ProviderAuthorizationTransactionState transaction, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ProviderDeviceAuthorizationProgress>(new NotSupportedException());
        public ValueTask<ProviderRevocationResult> RevokeAsync(ProviderAuthorizationIdentity identity, ProviderAuthorizationSession current, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProviderRevocationResult { Revoked = false });
    }

    public enum DeviceFault { None, ExtendedExpiry, InvalidTerminal }

    private sealed class DeviceStrategy(DeviceFault fault = DeviceFault.None) : IProviderAuthenticationStrategy
    {
        private int _advances;
        public int AdvanceCount => Volatile.Read(ref _advances);
        public ProviderAuthenticationStrategyDescriptor Descriptor { get; } = new()
        {
            StrategyId = new ProviderAuthenticationStrategyId("oauth-device-test:v1"),
            ProviderKey = "oauth-test",
            BackendKey = "platform",
            Kind = ProviderAuthenticationKind.OAuth,
            Flows = [ProviderAuthorizationFlow.DeviceAuthorization],
            SupportsRefresh = false,
            SupportsRevocation = false
        };

        public ValueTask<NormalizedProviderAuthorizationRequest> NormalizeAsync(
            ProviderCredentialRequest request, CancellationToken cancellationToken = default) =>
            new RefreshStrategy().NormalizeAsync(request, cancellationToken);

        public ValueTask<ProviderAuthorizationStart> BeginAuthorizationAsync(
            ProviderAuthorizationBeginContext context, CancellationToken cancellationToken = default)
        {
            Assert.IsType<DeviceProviderAuthorizationBeginContext>(context);
            var id = Guid.NewGuid().ToString("N");
            var expiry = context.TimeProvider.GetUtcNow().AddMinutes(5);
            return ValueTask.FromResult(new ProviderAuthorizationStart
            {
                TransactionState = new ProviderAuthorizationTransactionState
                {
                    TransactionId = id,
                    Identity = context.Request.Identity,
                    StrategyId = Descriptor.StrategyId,
                    Flow = ProviderAuthorizationFlow.DeviceAuthorization,
                    ExpiresAt = expiry,
                    ProviderState = new SensitiveBuffer([1])
                },
                Challenge = new DeviceAuthorizationChallenge
                {
                    TransactionId = id,
                    ProviderKey = Descriptor.ProviderKey,
                    BackendKey = Descriptor.BackendKey,
                    AccountId = context.Request.Identity.AccountId,
                    ExpiresAt = expiry,
                    VerificationUri = new Uri("https://issuer.test/device"),
                    UserCode = "ABCD-EFGH"
                }
            });
        }

        public ValueTask ValidateBrowserAuthorizationResponseAsync(ProviderAuthorizationTransactionState transaction, BrowserAuthorizationResponse response, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new NotSupportedException());
        public ValueTask<ProviderAuthorizationSession> CompleteBrowserAuthorizationAsync(ProviderAuthorizationTransactionState transaction, BrowserAuthorizationResponse response, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ProviderAuthorizationSession>(new NotSupportedException());
        public ValueTask<ProviderDeviceAuthorizationProgress> AdvanceDeviceAuthorizationAsync(ProviderAuthorizationTransactionState transaction, CancellationToken cancellationToken = default)
        {
            if (fault == DeviceFault.ExtendedExpiry)
                return ValueTask.FromResult<ProviderDeviceAuthorizationProgress>(new ProviderDeviceAuthorizationProgress.Pending
                {
                    Transaction = transaction with
                    {
                        ExpiresAt = transaction.ExpiresAt.AddMinutes(1),
                        ProviderState = new SensitiveBuffer([4])
                    }
                });
            if (fault == DeviceFault.InvalidTerminal)
                return ValueTask.FromResult<ProviderDeviceAuthorizationProgress>(new ProviderDeviceAuthorizationProgress.Terminal
                {
                    Status = ProviderDeviceAuthorizationStatusKind.Authorized
                });
            if (Interlocked.Increment(ref _advances) == 1)
                return ValueTask.FromResult<ProviderDeviceAuthorizationProgress>(new ProviderDeviceAuthorizationProgress.Pending
                {
                    Transaction = new ProviderAuthorizationTransactionState
                    {
                        TransactionId = transaction.TransactionId,
                        Identity = transaction.Identity,
                        StrategyId = transaction.StrategyId,
                        Flow = transaction.Flow,
                        ExpiresAt = transaction.ExpiresAt,
                        ProviderState = new SensitiveBuffer([2])
                    }
                });
            return ValueTask.FromResult<ProviderDeviceAuthorizationProgress>(new ProviderDeviceAuthorizationProgress.Authorized
            {
                Transaction = new ProviderAuthorizationTransactionState
                {
                    TransactionId = transaction.TransactionId,
                    Identity = transaction.Identity,
                    StrategyId = transaction.StrategyId,
                    Flow = transaction.Flow,
                    ExpiresAt = transaction.ExpiresAt,
                    ProviderState = new SensitiveBuffer([3])
                },
                Session = Session("device-access", "device-refresh", DateTimeOffset.UtcNow.AddHours(1))
            });
        }
        public ValueTask<ProviderAuthorizationRefreshResult> RefreshAsync(ProviderAuthorizationIdentity identity, ProviderAuthorizationSession current, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ProviderAuthorizationRefreshResult>(new NotSupportedException());
        public ValueTask<ProviderCredential> CreateCredentialAsync(ProviderAuthorizationIdentity identity, ProviderAuthorizationSession current, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ProviderCredential>(new ProviderCredential.BearerToken(new Buffer(current.Secrets.AccessToken.Value.Span)));
        public ValueTask<ProviderRevocationResult> RevokeAsync(ProviderAuthorizationIdentity identity, ProviderAuthorizationSession current, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProviderRevocationResult { Revoked = false });
    }

    private sealed class CancelAfterSaveTransactionStore(IProviderAuthorizationTransactionStore inner)
        : IProviderAuthorizationTransactionStore
    {
        public CancellationTokenSource? CancelAfterNextSave
        {
            get => Volatile.Read(ref _cancelAfterNextSave);
            set => Interlocked.Exchange(ref _cancelAfterNextSave, value);
        }
        public ValueTask<string> CreateAsync(ProviderAuthorizationTransactionEnvelope envelope, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(envelope, cancellationToken);
        public ValueTask<ProviderAuthorizationTransactionRecord?> LoadAsync(string transactionId, string authorizationScopeIdentity, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(transactionId, authorizationScopeIdentity, cancellationToken);
        public async ValueTask<bool> TrySaveAsync(ProviderAuthorizationTransactionEnvelope envelope, string expectedRevision, CancellationToken cancellationToken = default)
        {
            var saved = await inner.TrySaveAsync(envelope, expectedRevision, cancellationToken);
            Interlocked.Exchange(ref _cancelAfterNextSave, null)?.Cancel();
            return saved;
        }
        private CancellationTokenSource? _cancelAfterNextSave;
        public ValueTask<bool> TryConsumeAsync(string transactionId, string authorizationScopeIdentity, string expectedRevision, CancellationToken cancellationToken = default) =>
            inner.TryConsumeAsync(transactionId, authorizationScopeIdentity, expectedRevision, cancellationToken);
        public ValueTask CancelAsync(string transactionId, string authorizationScopeIdentity, CancellationToken cancellationToken = default) =>
            inner.CancelAsync(transactionId, authorizationScopeIdentity, cancellationToken);
    }

    private sealed class Interaction : IProviderAuthorizationInteraction
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public ValueTask<ProviderAuthorizationResponse> AuthorizeAsync(ProviderAuthorizationChallenge challenge, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult<ProviderAuthorizationResponse>(new BrowserAuthorizationResponse
            {
                TransactionId = challenge.TransactionId,
                CallbackUri = new Uri("http://127.0.0.1/callback?code=redacted")
            });
        }
    }

    private sealed class TransactionProtector : IProviderAuthorizationTransactionProtector
    {
        private readonly ConcurrentDictionary<string, TransactionSnapshot> _values = new(StringComparer.Ordinal);
        public ValueTask<ProviderAuthorizationTransactionEnvelope> ProtectAsync(ProviderAuthorizationTransactionState transaction, CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid().ToString("N");
            _values[id] = new TransactionSnapshot(transaction.TransactionId, transaction.Identity,
                transaction.StrategyId, transaction.Flow, transaction.ExpiresAt, transaction.NextPollAt,
                transaction.ProviderState.Value.ToArray(),
                transaction.PendingCommit?.ExpectedAuthorizationRevision,
                transaction.PendingCommit?.Envelope.SchemaVersion,
                transaction.PendingCommit?.Envelope.ProtectedPayload.Value.ToArray());
            return ValueTask.FromResult(new ProviderAuthorizationTransactionEnvelope
            {
                TransactionId = transaction.TransactionId,
                AuthorizationScopeIdentity = ScopeIdentity(transaction.Identity),
                ExpiresAt = transaction.ExpiresAt,
                ProtectedPayload = new ProtectedBuffer(System.Text.Encoding.ASCII.GetBytes(id))
            });
        }
        public ValueTask<ProviderAuthorizationTransactionState> UnprotectAsync(ProviderAuthorizationTransactionEnvelope envelope, ProviderAuthorizationScope scope, CancellationToken cancellationToken = default)
        {
            var id = System.Text.Encoding.ASCII.GetString(envelope.ProtectedPayload.Value.Span);
            var value = _values[id];
            return ValueTask.FromResult(new ProviderAuthorizationTransactionState
            {
                TransactionId = value.TransactionId,
                Identity = value.Identity,
                StrategyId = value.StrategyId,
                Flow = value.Flow,
                ExpiresAt = value.ExpiresAt,
                NextPollAt = value.NextPollAt,
                PendingCommit = value.PendingPayload is null ? null : new ProviderPendingAuthorizationCommit
                {
                    ExpectedAuthorizationRevision = value.ExpectedAuthorizationRevision,
                    Envelope = new ProviderAuthorizationEnvelope
                    {
                        SchemaVersion = value.PendingSchemaVersion!,
                        ProtectedPayload = new ProtectedBuffer(value.PendingPayload.ToArray())
                    }
                },
                ProviderState = new SensitiveBuffer(value.State)
            });
        }
        private static string ScopeIdentity(ProviderAuthorizationIdentity identity)
        {
            var value = $"{identity.TrustDomainId}|{identity.TenantId}|{identity.PrincipalId}";
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
        }
        private sealed record TransactionSnapshot(string TransactionId, ProviderAuthorizationIdentity Identity,
            ProviderAuthenticationStrategyId StrategyId, ProviderAuthorizationFlow Flow,
            DateTimeOffset ExpiresAt, DateTimeOffset? NextPollAt, byte[] State,
            string? ExpectedAuthorizationRevision, string? PendingSchemaVersion, byte[]? PendingPayload);
    }

    private sealed class SessionProtector : IProviderAuthorizationProtector
    {
        private readonly ConcurrentDictionary<string, Snapshot> _values = new(StringComparer.Ordinal);
        public ValueTask<ProviderAuthorizationEnvelope> ProtectAsync(ProviderAuthorizationIdentity identity, ProviderAuthorizationSession session, CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid().ToString("N");
            _values[id] = Snapshot.From(session);
            return ValueTask.FromResult(new ProviderAuthorizationEnvelope
            {
                SchemaVersion = "test",
                ProtectedPayload = new ProtectedBuffer(System.Text.Encoding.ASCII.GetBytes(id))
            });
        }
        public ValueTask<ProviderAuthorizationSession> UnprotectAsync(ProviderAuthorizationIdentity identity, ProviderAuthorizationEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var id = System.Text.Encoding.ASCII.GetString(envelope.ProtectedPayload.Value.Span);
            return ValueTask.FromResult(_values[id].ToSession());
        }
        private sealed record Snapshot(
            string Access, string? Refresh, DateTimeOffset? ExpiresAt, string AuthorizationServer)
        {
            public static Snapshot From(ProviderAuthorizationSession value) => new(
                value.Secrets.AccessToken.Value.ToString(), value.Secrets.RefreshToken?.Value.ToString(),
                value.ExpiresAt, value.AuthorizationServer);
            public ProviderAuthorizationSession ToSession() => Session(Access, Refresh!, ExpiresAt!.Value, AuthorizationServer);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class Buffer : IProviderSecretBuffer
    {
        private char[]? _value;
        public Buffer(string value) => _value = value.ToCharArray();
        public Buffer(ReadOnlySpan<char> value) => _value = value.ToArray();
        public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(Buffer));
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _value, null)?.AsSpan().Clear(); return ValueTask.CompletedTask; }
    }
    private sealed class ProtectedBuffer(byte[] value) : IProviderProtectedBuffer
    {
        private byte[]? _value = value;
        public ReadOnlyMemory<byte> Value => _value ?? throw new ObjectDisposedException(nameof(ProtectedBuffer));
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _value, null)?.AsSpan().Clear(); return ValueTask.CompletedTask; }
    }
    private sealed class SensitiveBuffer(byte[] value) : IProviderSensitiveBuffer
    {
        private byte[]? _value = value.ToArray();
        public ReadOnlyMemory<byte> Value => _value ?? throw new ObjectDisposedException(nameof(SensitiveBuffer));
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _value, null)?.AsSpan().Clear(); return ValueTask.CompletedTask; }
    }
    private sealed class SecretSet(string access, string refresh) : IProviderAuthorizationSecretSet
    {
        private IProviderSecretBuffer? _access = new Buffer(access), _refresh = new Buffer(refresh);
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(SecretSet));
        public IProviderSecretBuffer? RefreshToken => _refresh;
        public IProviderSecretBuffer? ClientSecret => null;
        public async ValueTask DisposeAsync()
        {
            var accessValue = Interlocked.Exchange(ref _access, null);
            var refreshValue = Interlocked.Exchange(ref _refresh, null);
            if (accessValue is not null) await accessValue.DisposeAsync();
            if (refreshValue is not null) await refreshValue.DisposeAsync();
        }
    }
    private sealed class RefreshSecrets(string access) : IProviderRefreshSecretSet
    {
        private IProviderSecretBuffer? _access = new Buffer(access);
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(RefreshSecrets));
        public IProviderSecretBuffer? ReplacementRefreshToken => null;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _access, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
