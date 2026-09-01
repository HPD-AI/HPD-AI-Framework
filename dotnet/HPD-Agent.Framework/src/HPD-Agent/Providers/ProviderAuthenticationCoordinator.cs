using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers;

/// <summary>Coordinates typed credentials, protected OAuth sessions, refresh, and host interaction.</summary>
public sealed class ProviderAuthenticationCoordinator : IProviderCredentialSource
{
    private readonly ISecretResolver _secrets;
    private readonly IProviderRuntimeSecretRegistry? _runtimeSecrets;
    private readonly IProviderExternalIdentityRegistry? _externalIdentities;
    private readonly IProviderAuthenticationStrategyRegistry? _strategies;
    private readonly IProviderAuthorizationStoreRegistry? _stores;
    private readonly IProviderAuthorizationTransactionStore? _transactions;
    private readonly IProviderAuthorizationTransactionProtector? _transactionProtector;
    private readonly IProviderAuthorizationInteraction? _interaction;
    private readonly Func<NormalizedProviderAuthorizationRequest, Uri>? _redirects;
    private readonly TimeProvider _time;
    private readonly ProviderAuthorizationActivation _activation;
    private readonly TimeSpan _skew;
    private readonly ConditionalWeakTable<ProviderCredentialPlan, Prepared> _prepared = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Initializes a coordinator with host-supplied runtime infrastructure.</summary>
    public ProviderAuthenticationCoordinator(
        ISecretResolver secrets,
        IProviderRuntimeSecretRegistry? runtimeSecrets = null,
        IProviderExternalIdentityRegistry? externalIdentities = null,
        IProviderAuthenticationStrategyRegistry? strategies = null,
        IProviderAuthorizationStoreRegistry? stores = null,
        IProviderAuthorizationTransactionStore? transactions = null,
        IProviderAuthorizationTransactionProtector? transactionProtector = null,
        IProviderAuthorizationInteraction? interaction = null,
        Func<NormalizedProviderAuthorizationRequest, Uri>? redirectUriResolver = null,
        TimeProvider? timeProvider = null,
        ProviderAuthorizationActivation activation = ProviderAuthorizationActivation.ExplicitOnly,
        TimeSpan? expirationSkew = null)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _runtimeSecrets = runtimeSecrets;
        _externalIdentities = externalIdentities;
        _strategies = strategies;
        _stores = stores;
        _transactions = transactions;
        _transactionProtector = transactionProtector;
        _interaction = interaction;
        _redirects = redirectUriResolver;
        _time = timeProvider ?? TimeProvider.System;
        _activation = activation;
        _skew = expirationSkew ?? TimeSpan.FromMinutes(2);
        if (_skew < TimeSpan.Zero || _skew > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(expirationSkew));
    }

    /// <inheritdoc />
    public async ValueTask<ProviderCredentialPlan> PrepareAsync(
        ProviderCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        var authentication = ProviderClientConfigSnapshot.CloneAuthentication(request.Authentication);
        var snapshot = Snapshot(request, authentication);
        NormalizedProviderAuthorizationRequest? normalized = null;
        ProviderAuthorizationStoreRegistration? store = null;
        ProviderCredentialIdentity identity;
        ProviderAuthorizationGrantSnapshot grant;
        string reference;

        if (authentication is OAuthProviderAuthentication oauth)
        {
            var strategy = Strategy(request.ProviderKey, request.BackendKey, request.Family);
            normalized = await strategy.NormalizeAsync(snapshot, cancellationToken).ConfigureAwait(false);
            ValidateNormalized(snapshot, strategy, normalized);
            store = _stores?.Resolve(oauth.StoreKey)
                ?? throw Error("AuthorizationStoreRequired", request, "OAuth requires a protected authorization store.");
            reference = $"oauth:{store.Identity}:{strategy.Descriptor.StrategyId.Value}:{oauth.AccountId}";
            grant = Grant(normalized.Grant);
            identity = new ProviderCredentialIdentity
            {
                ProviderKey = normalized.Identity.ProviderKey,
                BackendKey = normalized.Identity.BackendKey,
                Subject = Hash($"{normalized.Identity.AccountId}|{normalized.Identity.ClientIdentity}"),
                TrustDomainId = normalized.Identity.TrustDomainId,
                TenantId = normalized.Identity.TenantId,
                PrincipalId = normalized.Identity.PrincipalId
            };
        }
        else
        {
            reference = Reference(authentication);
            grant = Grant(snapshot.Audience);
            identity = new ProviderCredentialIdentity
            {
                ProviderKey = snapshot.ProviderKey,
                BackendKey = snapshot.BackendKey,
                Subject = reference,
                TrustDomainId = snapshot.AuthorizationScope.TrustDomainId,
                TenantId = snapshot.AuthorizationScope.TenantId,
                PrincipalId = snapshot.AuthorizationScope.PrincipalId
            };
        }

        var scopeIdentity = Hash($"{identity.TrustDomainId}|{identity.TenantId}|{identity.PrincipalId}");
        var plan = new ProviderCredentialPlan
        {
            Backend = new ProviderBackendIdentity(snapshot.ProviderKey, snapshot.BackendKey),
            Family = snapshot.Family,
            AuthorizationScope = new ProviderAuthorizationScopeSnapshot
            {
                TrustDomainId = identity.TrustDomainId,
                TenantId = identity.TenantId,
                PrincipalId = identity.PrincipalId
            },
            Identity = identity,
            Grant = grant,
            AuthorizationStoreIdentity = store?.Identity,
            StableCredentialIdentity = Hash($"{snapshot.ProviderKey}|{snapshot.BackendKey}|{reference}|{scopeIdentity}|{grant.GrantIdentity}"),
            AuthorizationScopeIdentity = scopeIdentity
        };
        _prepared.Add(plan, new Prepared(authentication, snapshot, normalized, store));
        return plan;
    }

    /// <inheritdoc />
    public async ValueTask<IProviderCredentialLease> AcquireAsync(
        ProviderCredentialPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_prepared.TryGetValue(plan, out var prepared))
            throw new InvalidOperationException("The credential plan was not prepared by this coordinator.");
        return prepared.Authentication switch
        {
            ApiKeyProviderAuthentication value => await ApiKeyAsync(plan, value, cancellationToken).ConfigureAwait(false),
            ExplicitApiKeyProviderAuthentication value => Explicit(plan, value),
            ExternalIdentityProviderAuthentication value => await ExternalAsync(plan, value, cancellationToken).ConfigureAwait(false),
            AnonymousProviderAuthentication => Lease(plan, new ProviderCredential.Anonymous(), "anonymous", null),
            OAuthProviderAuthentication => await OAuthAsync(plan, prepared, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(plan))
        };
    }

    /// <summary>Explicitly authorizes the OAuth selection represented by a prepared plan.</summary>
    public async ValueTask AuthorizeAsync(ProviderCredentialPlan plan, CancellationToken cancellationToken = default)
    {
        if (!_prepared.TryGetValue(plan, out var prepared) || prepared.Normalized is null)
            throw new ArgumentException("The plan must be an OAuth plan prepared by this coordinator.", nameof(plan));
        var gate = _locks.GetOrAdd(plan.StableCredentialIdentity, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await AuthorizeUnlockedAsync(plan, prepared, RequireSingleFlow(plan), cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    /// <summary>Begins an OAuth authorization transaction without performing host interaction.</summary>
    /// <param name="plan">An OAuth credential plan prepared by this coordinator.</param>
    /// <param name="cancellationToken">A token that cancels transaction creation.</param>
    /// <returns>The redacted browser or device challenge for the host to present.</returns>
    public async ValueTask<ProviderAuthorizationChallenge> BeginAuthorizationAsync(
        BeginProviderAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = request.Plan;
        var prepared = RequireOAuthPlan(plan);
        RequireSupportedFlow(plan, request.Flow);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await BeginAuthorizationUnlockedAsync(plan, prepared, request.Flow, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    /// <summary>Completes one previously persisted OAuth authorization transaction.</summary>
    /// <param name="plan">The same immutable OAuth plan used to begin authorization.</param>
    /// <param name="response">The correlated host response.</param>
    /// <param name="cancellationToken">A token that cancels validation or token exchange.</param>
    /// <remarks>
    /// Invalid responses do not consume or cancel the stored transaction. Only the winner of
    /// atomic validation and consumption may exchange an authorization code.
    /// </remarks>
    public async ValueTask CompleteBrowserAuthorizationAsync(
        ProviderCredentialPlan plan,
        BrowserAuthorizationResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        var prepared = RequireOAuthPlan(plan);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await CompleteBrowserAuthorizationUnlockedAsync(plan, prepared, response, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    /// <summary>Advances one protected device authorization transaction by at most one provider step.</summary>
    public async ValueTask<ProviderDeviceAuthorizationStatus> AdvanceDeviceAuthorizationAsync(
        ProviderCredentialPlan plan,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        var prepared = RequireOAuthPlan(plan);
        RequireSupportedFlow(plan, ProviderAuthorizationFlow.DeviceAuthorization);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await AdvanceDeviceAuthorizationUnlockedAsync(plan, prepared, transactionId, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    /// <summary>Reads one protected device transaction without contacting the provider.</summary>
    public async ValueTask<ProviderDeviceAuthorizationStatus> GetDeviceAuthorizationStatusAsync(
        ProviderCredentialPlan plan,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        var prepared = RequireOAuthPlan(plan);
        RequireSupportedFlow(plan, ProviderAuthorizationFlow.DeviceAuthorization);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transactionStore = _transactions ?? throw Error("TransactionStoreRequired", plan, "Authorization requires a transaction store.");
            var protector = _transactionProtector ?? throw Error("TransactionProtectorRequired", plan, "Authorization requires a transaction protector.");
            await using var record = await transactionStore.LoadAsync(
                transactionId, plan.AuthorizationScopeIdentity, cancellationToken).ConfigureAwait(false)
                ?? throw Error("TransactionMissing", plan, "The device authorization transaction is absent or consumed.");
            await using var state = await protector.UnprotectAsync(
                record.Envelope, Scope(plan), cancellationToken).ConfigureAwait(false);
            ValidateDeviceTransaction(plan, prepared, state, transactionId);
            if (state.PendingCommit is not null)
                return DeviceStatus(transactionId, ProviderDeviceAuthorizationStatusKind.TransientFailure,
                    diagnosticCode: "SessionCommitPending");
            if (_time.GetUtcNow() >= record.Envelope.ExpiresAt)
                return DeviceStatus(transactionId, ProviderDeviceAuthorizationStatusKind.Expired);
            return DeviceStatus(transactionId, ProviderDeviceAuthorizationStatusKind.Pending, state.NextPollAt);
        }
        finally { gate.Release(); }
    }

    /// <summary>Cancels one protected device transaction without changing an existing account session.</summary>
    public async ValueTask CancelDeviceAuthorizationAsync(
        ProviderCredentialPlan plan,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        var prepared = RequireOAuthPlan(plan);
        RequireSupportedFlow(plan, ProviderAuthorizationFlow.DeviceAuthorization);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transactionStore = _transactions ?? throw Error("TransactionStoreRequired", plan, "Authorization requires a transaction store.");
            var protector = _transactionProtector ?? throw Error("TransactionProtectorRequired", plan, "Authorization requires a transaction protector.");
            await using var record = await transactionStore.LoadAsync(
                transactionId, plan.AuthorizationScopeIdentity, cancellationToken).ConfigureAwait(false)
                ?? throw Error("TransactionMissing", plan, "The device authorization transaction is absent or consumed.");
            await using var state = await protector.UnprotectAsync(
                record.Envelope, Scope(plan), cancellationToken).ConfigureAwait(false);
            ValidateDeviceTransaction(plan, prepared, state, transactionId);
            await transactionStore.CancelAsync(transactionId, plan.AuthorizationScopeIdentity, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    /// <summary>Reads the redacted durable status of a prepared OAuth account.</summary>
    public async ValueTask<ProviderAuthorizationStatus> GetStatusAsync(
        ProviderCredentialPlan plan, CancellationToken cancellationToken = default)
    {
        var prepared = RequireOAuthPlan(plan);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = prepared.Normalized!;
            var registration = prepared.Store!;
            await using var record = await registration.Store.LoadAsync(normalized.Identity, cancellationToken).ConfigureAwait(false);
            if (record is null)
                return Status(ProviderAuthorizationStatusKind.Disconnected, plan, null);
            await using var session = await registration.Protector.UnprotectAsync(
                normalized.Identity, record.Envelope, cancellationToken).ConfigureAwait(false);
            ValidateSession(plan, normalized, session);
            var expiredWithoutRefresh = session.ExpiresAt is { } expiry && expiry <= _time.GetUtcNow() + _skew &&
                session.Secrets.RefreshToken is null;
            return Status(expiredWithoutRefresh ? ProviderAuthorizationStatusKind.ReauthorizationRequired :
                ProviderAuthorizationStatusKind.Authorized, plan, session.ExpiresAt);
        }
        finally { gate.Release(); }
    }

    /// <summary>Conditionally removes the exact local OAuth session without contacting the provider.</summary>
    public async ValueTask<ProviderDisconnectResult> DisconnectAsync(
        ProviderCredentialPlan plan, CancellationToken cancellationToken = default)
    {
        var prepared = RequireOAuthPlan(plan);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registration = prepared.Store!;
            await using var record = await registration.Store.LoadAsync(
                prepared.Normalized!.Identity, cancellationToken).ConfigureAwait(false);
            var deleted = record is not null && await registration.Store.TryDeleteAsync(
                prepared.Normalized.Identity, record.Revision, cancellationToken).ConfigureAwait(false);
            return new ProviderDisconnectResult { LocalStateDeleted = deleted };
        }
        finally { gate.Release(); }
    }

    /// <summary>Attempts provider-side revocation while retaining local state.</summary>
    public async ValueTask<ProviderRevocationResult> RevokeAsync(
        ProviderCredentialPlan plan, CancellationToken cancellationToken = default)
    {
        var prepared = RequireOAuthPlan(plan);
        var strategy = Strategy(plan);
        if (!strategy.Descriptor.SupportsRevocation)
            return new ProviderRevocationResult { Revoked = false, DiagnosticCode = "RevocationUnsupported" };
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registration = prepared.Store!;
            await using var record = await registration.Store.LoadAsync(
                prepared.Normalized!.Identity, cancellationToken).ConfigureAwait(false)
                ?? throw Error("AuthorizationSessionMissing", plan, "No authorization session exists to revoke.");
            await using var session = await registration.Protector.UnprotectAsync(
                prepared.Normalized.Identity, record.Envelope, cancellationToken).ConfigureAwait(false);
            ValidateSession(plan, prepared.Normalized, session);
            return await strategy.RevokeAsync(prepared.Normalized.Identity, session, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    /// <summary>Attempts remote revocation and then conditionally deletes the same local session revision.</summary>
    public async ValueTask<ProviderDisconnectResult> RevokeAndDisconnectAsync(
        ProviderCredentialPlan plan, CancellationToken cancellationToken = default)
    {
        var prepared = RequireOAuthPlan(plan);
        var gate = Gate(plan);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registration = prepared.Store!;
            await using var record = await registration.Store.LoadAsync(
                prepared.Normalized!.Identity, cancellationToken).ConfigureAwait(false);
            if (record is null)
                return new ProviderDisconnectResult { LocalStateDeleted = false };
            await using var session = await registration.Protector.UnprotectAsync(
                prepared.Normalized.Identity, record.Envelope, cancellationToken).ConfigureAwait(false);
            ValidateSession(plan, prepared.Normalized, session);
            var strategy = Strategy(plan);
            var revoked = strategy.Descriptor.SupportsRevocation
                ? await strategy.RevokeAsync(prepared.Normalized.Identity, session, cancellationToken).ConfigureAwait(false)
                : new ProviderRevocationResult { Revoked = false, DiagnosticCode = "RevocationUnsupported" };
            var deleted = await registration.Store.TryDeleteAsync(
                prepared.Normalized.Identity, record.Revision, cancellationToken).ConfigureAwait(false);
            return new ProviderDisconnectResult { LocalStateDeleted = deleted, RemoteRevocation = revoked };
        }
        finally { gate.Release(); }
    }

    private async ValueTask<IProviderCredentialLease> ApiKeyAsync(
        ProviderCredentialPlan plan, ApiKeyProviderAuthentication authentication, CancellationToken cancellationToken)
    {
        var secret = await _secrets.ResolveAsync(authentication.SecretKey, cancellationToken).ConfigureAwait(false)
            ?? throw Error("SecretNotFound", plan, "The configured provider secret reference was not found.");
        return Lease(plan, new ProviderCredential.ApiKey(new OwnedProviderSecretBuffer(secret.Value)),
            Hash($"{authentication.SecretKey}|{secret.Source}|{secret.ExpiresAt?.UtcTicks}"), secret.ExpiresAt);
    }

    private IProviderCredentialLease Explicit(ProviderCredentialPlan plan, ExplicitApiKeyProviderAuthentication authentication)
    {
        var secret = _runtimeSecrets?.Acquire(authentication.RuntimeRegistrationName)
            ?? throw Error("RuntimeSecretRegistryRequired", plan, "The process-local secret registration is unavailable.");
        return Lease(plan, new ProviderCredential.ApiKey(secret), Hash(authentication.RuntimeRegistrationName), null);
    }

    private async ValueTask<IProviderCredentialLease> ExternalAsync(
        ProviderCredentialPlan plan, ExternalIdentityProviderAuthentication authentication, CancellationToken cancellationToken)
    {
        var registration = _externalIdentities?.Find(authentication.CredentialName)
            ?? throw Error("ExternalIdentityNotFound", plan, "The external identity registration is unavailable.");
        var acquired = await registration.AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (!registration.CredentialType.IsAssignableFrom(acquired.CredentialType) ||
            !acquired.CredentialType.IsInstanceOfType(acquired.Credential))
        {
            await acquired.DisposeAsync().ConfigureAwait(false);
            throw Error("ExternalIdentityTypeMismatch", plan, "The external identity lease has an invalid SDK type.");
        }
        return Lease(plan, new ProviderCredential.ExternalIdentity(acquired),
            Hash($"{authentication.CredentialName}|{registration.CredentialType.AssemblyQualifiedName}"), null);
    }

    private async ValueTask<IProviderCredentialLease> OAuthAsync(
        ProviderCredentialPlan plan, Prepared prepared, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(plan.StableCredentialIdentity, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lease = await StoredAsync(plan, prepared, cancellationToken).ConfigureAwait(false);
            if (lease is not null) return lease;
            if (_activation == ProviderAuthorizationActivation.ExplicitOnly)
                throw new ProviderInteractionRequiredException(plan.Backend.ProviderKey, plan.Backend.BackendKey,
                    plan.Family, plan.Identity.Subject);
            await AuthorizeUnlockedAsync(plan, prepared, RequireSingleFlow(plan), cancellationToken).ConfigureAwait(false);
            return await StoredAsync(plan, prepared, cancellationToken).ConfigureAwait(false)
                ?? throw Error("AuthorizationCommitMissing", plan, "Authorization did not commit a usable session.");
        }
        finally { gate.Release(); }
    }

    private async ValueTask<IProviderCredentialLease?> StoredAsync(
        ProviderCredentialPlan plan, Prepared prepared, CancellationToken cancellationToken)
    {
        var normalized = prepared.Normalized!;
        var registration = prepared.Store!;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var record = await registration.Store.LoadAsync(normalized.Identity, cancellationToken).ConfigureAwait(false);
            if (record is null) return null;
            await using var session = await registration.Protector.UnprotectAsync(
                normalized.Identity, record.Envelope, cancellationToken).ConfigureAwait(false);
            ValidateSession(plan, normalized, session);
            if (session.ExpiresAt is null || session.ExpiresAt > _time.GetUtcNow() + _skew)
                return Lease(plan, await Strategy(plan).CreateCredentialAsync(
                    normalized.Identity, session, cancellationToken).ConfigureAwait(false), record.Revision, session.ExpiresAt);
            if (session.Secrets.RefreshToken is null) return null;

            ProviderAuthorizationRefreshResult refresh;
            try
            {
                refresh = await Strategy(plan).RefreshAsync(
                    normalized.Identity, session, cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderAuthenticationException exception)
                when (exception.FailureKind is ProviderAuthenticationFailureKind.InvalidGrant or ProviderAuthenticationFailureKind.Revoked)
            {
                await registration.Store.TryDeleteAsync(
                    normalized.Identity, record.Revision, cancellationToken).ConfigureAwait(false);
                return null;
            }
            await using (refresh.ConfigureAwait(false))
            {
                await using var replacement = Refreshed(session, refresh);
                await using var envelope = await registration.Protector.ProtectAsync(
                    normalized.Identity, replacement, cancellationToken).ConfigureAwait(false);
                var write = await registration.Store.TrySaveAsync(
                    normalized.Identity, record.Revision, envelope, cancellationToken).ConfigureAwait(false);
                if (write is ProviderAuthorizationWriteResult.Conflict conflict)
                {
                    await conflict.DisposeAsync().ConfigureAwait(false);
                    continue;
                }
                var revision = ((ProviderAuthorizationWriteResult.Written)write).NewRevision;
                return Lease(plan, await Strategy(plan).CreateCredentialAsync(
                    normalized.Identity, replacement, cancellationToken).ConfigureAwait(false), revision, replacement.ExpiresAt);
            }
        }
        throw new ProviderAuthenticationException(
            ProviderAuthenticationFailureKind.StoreUnavailable,
            plan.Backend.ProviderKey,
            plan.Backend.BackendKey,
            plan.Family,
            plan.Identity.Subject,
            "Authorization refresh could not commit because the session changed repeatedly.",
            isRetryable: true,
            diagnosticCode: "RefreshCommitConflict");
    }

    private async ValueTask AuthorizeUnlockedAsync(
        ProviderCredentialPlan plan, Prepared prepared, ProviderAuthorizationFlow flow, CancellationToken cancellationToken)
    {
        var interaction = _interaction ?? throw new ProviderInteractionRequiredException(
            plan.Backend.ProviderKey, plan.Backend.BackendKey, plan.Family, plan.Identity.Subject);
        var challenge = await BeginAuthorizationUnlockedAsync(plan, prepared, flow, cancellationToken).ConfigureAwait(false);
        ProviderAuthorizationResponse response;
        try
        {
            response = await interaction.AuthorizeAsync(challenge, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_transactions is not null)
                await _transactions.CancelAsync(challenge.TransactionId, plan.AuthorizationScopeIdentity, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        if (response is BrowserAuthorizationResponse browser)
        {
            await CompleteBrowserAuthorizationUnlockedAsync(plan, prepared, browser, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (response is DeviceAuthorizationPresentationResponse device)
        {
            if (device.Action == ProviderDeviceAuthorizationAction.Cancel)
            {
                await _transactions!.CancelAsync(challenge.TransactionId, plan.AuthorizationScopeIdentity, cancellationToken).ConfigureAwait(false);
                throw new OperationCanceledException("Provider device authorization was canceled.", cancellationToken);
            }
            var status = await AdvanceDeviceAuthorizationUnlockedAsync(
                plan, prepared, challenge.TransactionId, cancellationToken).ConfigureAwait(false);
            if (status.Status == ProviderDeviceAuthorizationStatusKind.Authorized)
                return;
            throw new ProviderInteractionRequiredException(
                plan.Backend.ProviderKey, plan.Backend.BackendKey, plan.Family, plan.Identity.Subject,
                challenge.TransactionId, status.NextPollAt);
        }
        throw Error("AuthorizationResponseInvalid", plan, "The host returned an unsupported authorization response.");
    }

    private async ValueTask<ProviderAuthorizationChallenge> BeginAuthorizationUnlockedAsync(
        ProviderCredentialPlan plan, Prepared prepared, ProviderAuthorizationFlow flow, CancellationToken cancellationToken)
    {
        var normalized = prepared.Normalized!;
        var strategy = Strategy(plan);
        var transactionStore = _transactions ?? throw Error("TransactionStoreRequired", plan, "Authorization requires a transaction store.");
        var protector = _transactionProtector ?? throw Error("TransactionProtectorRequired", plan, "Authorization requires a transaction protector.");
        ProviderAuthorizationBeginContext context = flow switch
        {
            ProviderAuthorizationFlow.AuthorizationCodePkce => new BrowserProviderAuthorizationBeginContext
            {
                Request = normalized,
                RedirectUri = _redirects?.Invoke(normalized) ?? throw Error(
                    "RedirectUriRequired", plan, "Browser authorization requires a host redirect URI."),
                TimeProvider = _time
            },
            ProviderAuthorizationFlow.DeviceAuthorization => new DeviceProviderAuthorizationBeginContext
            {
                Request = normalized,
                TimeProvider = _time
            },
            _ => throw Error("AuthorizationFlowUnsupported", plan, "The selected authorization flow is unsupported.")
        };
        var start = await strategy.BeginAuthorizationAsync(context, cancellationToken).ConfigureAwait(false);
        await using var transaction = start.TransactionState;
        if (transaction.StrategyId != strategy.Descriptor.StrategyId || transaction.Identity != normalized.Identity ||
            transaction.Flow != flow ||
            !string.Equals(transaction.TransactionId, start.Challenge.TransactionId, StringComparison.Ordinal))
            throw Error("TransactionInvalid", plan, "The strategy returned inconsistent transaction state.");
        if (!string.Equals(start.Challenge.ProviderKey, normalized.Identity.ProviderKey, StringComparison.Ordinal) ||
            !string.Equals(start.Challenge.BackendKey, normalized.Identity.BackendKey, StringComparison.Ordinal) ||
            !string.Equals(start.Challenge.AccountId, normalized.Identity.AccountId, StringComparison.Ordinal) ||
            start.Challenge.ExpiresAt != transaction.ExpiresAt)
            throw Error("AuthorizationChallengeInvalid", plan, "The strategy returned inconsistent challenge identity or expiry.");
        if (flow == ProviderAuthorizationFlow.AuthorizationCodePkce &&
            (start.Challenge is not BrowserAuthorizationChallenge browser ||
             context is not BrowserProviderAuthorizationBeginContext browserContext ||
             browser.RedirectUri != browserContext.RedirectUri) ||
            flow == ProviderAuthorizationFlow.DeviceAuthorization && start.Challenge is not DeviceAuthorizationChallenge)
            throw Error("AuthorizationChallengeInvalid", plan, "The strategy returned a challenge for a different authorization flow.");
        await using var protectedTransaction = await protector.ProtectAsync(transaction, cancellationToken).ConfigureAwait(false);
        await transactionStore.CreateAsync(protectedTransaction, cancellationToken).ConfigureAwait(false);
        return start.Challenge;
    }

    private async ValueTask CompleteBrowserAuthorizationUnlockedAsync(
        ProviderCredentialPlan plan,
        Prepared prepared,
        BrowserAuthorizationResponse response,
        CancellationToken cancellationToken)
    {
        var normalized = prepared.Normalized!;
        var strategy = Strategy(plan);
        var transactionStore = _transactions ?? throw Error("TransactionStoreRequired", plan, "Authorization requires a transaction store.");
        var protector = _transactionProtector ?? throw Error("TransactionProtectorRequired", plan, "Authorization requires a transaction protector.");
        await using var record = await transactionStore.LoadAsync(
            response.TransactionId, plan.AuthorizationScopeIdentity, cancellationToken).ConfigureAwait(false)
            ?? throw Error("TransactionMissing", plan, "The authorization transaction is absent or consumed.");
        if (_time.GetUtcNow() >= record.Envelope.ExpiresAt)
            throw Error("TransactionExpired", plan, "The authorization transaction expired.");
        if (!string.Equals(response.TransactionId, record.Envelope.TransactionId, StringComparison.Ordinal))
            throw Error("CorrelationMismatch", plan, "The host response did not match the transaction.");
        await using var plaintext = await protector.UnprotectAsync(
            record.Envelope, Scope(plan), cancellationToken).ConfigureAwait(false);
        if (plaintext.StrategyId != strategy.Descriptor.StrategyId || plaintext.Identity != normalized.Identity ||
            plaintext.Flow != ProviderAuthorizationFlow.AuthorizationCodePkce ||
            !string.Equals(plaintext.TransactionId, response.TransactionId, StringComparison.Ordinal))
            throw Error("TransactionInvalid", plan, "The protected transaction identity is invalid.");
        await strategy.ValidateBrowserAuthorizationResponseAsync(plaintext, response, cancellationToken).ConfigureAwait(false);
        if (!await transactionStore.TryConsumeAsync(response.TransactionId, plan.AuthorizationScopeIdentity,
                record.Revision, cancellationToken).ConfigureAwait(false))
            throw Error("TransactionConsumed", plan, "Another caller consumed the authorization transaction.");
        await using var session = await strategy.CompleteBrowserAuthorizationAsync(plaintext, response, cancellationToken).ConfigureAwait(false);
        ValidateSession(plan, normalized, session);
        var registration = prepared.Store!;
        await using var current = await registration.Store.LoadAsync(normalized.Identity, cancellationToken).ConfigureAwait(false);
        await using var envelope = await registration.Protector.ProtectAsync(
            normalized.Identity, session, cancellationToken).ConfigureAwait(false);
        var write = await registration.Store.TrySaveAsync(
            normalized.Identity, current?.Revision, envelope, cancellationToken).ConfigureAwait(false);
        if (write is ProviderAuthorizationWriteResult.Conflict conflict)
        {
            await conflict.DisposeAsync().ConfigureAwait(false);
            throw Error("AuthorizationStoreConflict", plan, "Authorization state changed during commit.");
        }
    }

    private async ValueTask<ProviderDeviceAuthorizationStatus> AdvanceDeviceAuthorizationUnlockedAsync(
        ProviderCredentialPlan plan,
        Prepared prepared,
        string transactionId,
        CancellationToken cancellationToken)
    {
        var normalized = prepared.Normalized!;
        var strategy = Strategy(plan);
        var transactionStore = _transactions ?? throw Error("TransactionStoreRequired", plan, "Authorization requires a transaction store.");
        var protector = _transactionProtector ?? throw Error("TransactionProtectorRequired", plan, "Authorization requires a transaction protector.");
        await using var record = await transactionStore.LoadAsync(
            transactionId, plan.AuthorizationScopeIdentity, cancellationToken).ConfigureAwait(false)
            ?? throw Error("TransactionMissing", plan, "The device authorization transaction is absent or consumed.");
        await using var plaintext = await protector.UnprotectAsync(
            record.Envelope, Scope(plan), cancellationToken).ConfigureAwait(false);
        if (plaintext.StrategyId != strategy.Descriptor.StrategyId || plaintext.Identity != normalized.Identity ||
            plaintext.Flow != ProviderAuthorizationFlow.DeviceAuthorization ||
            !string.Equals(plaintext.TransactionId, transactionId, StringComparison.Ordinal))
            throw Error("TransactionInvalid", plan, "The protected device transaction identity is invalid.");
        if (plaintext.PendingCommit is not null)
            return await CommitPendingDeviceAuthorizationAsync(
                plan, prepared, plaintext, record.Revision, cancellationToken).ConfigureAwait(false);
        if (_time.GetUtcNow() >= record.Envelope.ExpiresAt)
        {
            await transactionStore.TryConsumeAsync(
                transactionId, plan.AuthorizationScopeIdentity, record.Revision, cancellationToken).ConfigureAwait(false);
            return DeviceStatus(transactionId, ProviderDeviceAuthorizationStatusKind.Expired);
        }
        if (plaintext.NextPollAt is { } next && _time.GetUtcNow() < next)
            return DeviceStatus(transactionId, ProviderDeviceAuthorizationStatusKind.Pending, next);

        await using var progress = await strategy.AdvanceDeviceAuthorizationAsync(
            plaintext, cancellationToken).ConfigureAwait(false);
        if (progress is ProviderDeviceAuthorizationProgress.Pending pending)
        {
            if (pending.Transaction.Flow != ProviderAuthorizationFlow.DeviceAuthorization ||
                pending.Transaction.Identity != normalized.Identity ||
                pending.Transaction.StrategyId != strategy.Descriptor.StrategyId ||
                !string.Equals(pending.Transaction.TransactionId, transactionId, StringComparison.Ordinal) ||
                pending.Transaction.PendingCommit is not null ||
                pending.Transaction.ExpiresAt != plaintext.ExpiresAt)
                throw Error("TransactionInvalid", plan, "The strategy returned inconsistent device transaction state.");
            var now = _time.GetUtcNow();
            var minimumPoll = now.AddSeconds(1);
            var requestedPoll = pending.Transaction.NextPollAt is { } providerPoll && providerPoll > minimumPoll
                ? providerPoll
                : minimumPoll;
            if (requestedPoll > plaintext.ExpiresAt ||
                plaintext.NextPollAt is { } priorPoll && requestedPoll < priorPoll)
                throw Error("TransactionInvalid", plan, "The strategy returned an invalid device polling schedule.");
            var scheduledTransaction = pending.Transaction with { NextPollAt = requestedPoll };
            await using var replacement = await protector.ProtectAsync(
                scheduledTransaction, cancellationToken).ConfigureAwait(false);
            if (!await transactionStore.TrySaveAsync(replacement, record.Revision, cancellationToken).ConfigureAwait(false))
                throw Error("TransactionConflict", plan, "Another caller advanced the device authorization transaction.");
            return DeviceStatus(
                transactionId,
                pending.IsSlowDown ? ProviderDeviceAuthorizationStatusKind.SlowDown :
                    pending.DiagnosticCode is null ? ProviderDeviceAuthorizationStatusKind.Pending :
                    ProviderDeviceAuthorizationStatusKind.TransientFailure,
                requestedPoll,
                pending.DiagnosticCode);
        }

        if (progress is ProviderDeviceAuthorizationProgress.Terminal invalidTerminal &&
            invalidTerminal.Status is not ProviderDeviceAuthorizationStatusKind.Denied and
                not ProviderDeviceAuthorizationStatusKind.Expired)
            throw Error("AuthorizationProgressInvalid", plan, "The strategy returned a non-terminal device status as terminal.");
        if (progress is not ProviderDeviceAuthorizationProgress.Terminal and
            not ProviderDeviceAuthorizationProgress.Authorized)
            throw Error("AuthorizationProgressInvalid", plan, "The strategy returned an invalid device progression result.");

        if (progress is ProviderDeviceAuthorizationProgress.Terminal terminal)
        {
            if (!await transactionStore.TryConsumeAsync(
                    transactionId, plan.AuthorizationScopeIdentity, record.Revision, cancellationToken).ConfigureAwait(false))
                throw Error("TransactionConsumed", plan, "Another caller completed the device authorization transaction.");
            return DeviceStatus(transactionId, terminal.Status, diagnosticCode: terminal.DiagnosticCode);
        }
        var authorized = (ProviderDeviceAuthorizationProgress.Authorized)progress;
        if (authorized.Transaction.Flow != ProviderAuthorizationFlow.DeviceAuthorization ||
            authorized.Transaction.Identity != normalized.Identity ||
            authorized.Transaction.StrategyId != strategy.Descriptor.StrategyId ||
            !string.Equals(authorized.Transaction.TransactionId, transactionId, StringComparison.Ordinal) ||
            authorized.Transaction.PendingCommit is not null ||
            authorized.Transaction.ExpiresAt != plaintext.ExpiresAt)
            throw Error("TransactionInvalid", plan, "The strategy returned inconsistent completed device transaction state.");
        ValidateSession(plan, normalized, authorized.Session);
        var registration = prepared.Store!;
        await using var current = await registration.Store.LoadAsync(normalized.Identity, cancellationToken).ConfigureAwait(false);
        await using var sessionEnvelope = await registration.Protector.ProtectAsync(
            normalized.Identity, authorized.Session, cancellationToken).ConfigureAwait(false);
        var commitState = authorized.Transaction with
        {
            NextPollAt = null,
            PendingCommit = new ProviderPendingAuthorizationCommit
            {
                ExpectedAuthorizationRevision = current?.Revision,
                Envelope = sessionEnvelope
            }
        };
        await using var commitEnvelope = await protector.ProtectAsync(commitState, cancellationToken).ConfigureAwait(false);
        if (!await transactionStore.TrySaveAsync(commitEnvelope, record.Revision, cancellationToken).ConfigureAwait(false))
            throw Error("TransactionConflict", plan, "Another caller advanced the device authorization transaction.");
        await using var committedRecord = await transactionStore.LoadAsync(
            transactionId, plan.AuthorizationScopeIdentity, cancellationToken).ConfigureAwait(false)
            ?? throw Error("TransactionMissing", plan, "The recoverable device session commit is absent.");
        await using var committedState = await protector.UnprotectAsync(
            committedRecord.Envelope, Scope(plan), cancellationToken).ConfigureAwait(false);
        return await CommitPendingDeviceAuthorizationAsync(
            plan, prepared, committedState, committedRecord.Revision, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ProviderDeviceAuthorizationStatus> CommitPendingDeviceAuthorizationAsync(
        ProviderCredentialPlan plan,
        Prepared prepared,
        ProviderAuthorizationTransactionState transaction,
        string transactionRevision,
        CancellationToken cancellationToken)
    {
        var pending = transaction.PendingCommit
            ?? throw Error("TransactionInvalid", plan, "The device session commit marker is absent.");
        var registration = prepared.Store!;
        var write = await registration.Store.TrySaveAsync(
            transaction.Identity,
            pending.ExpectedAuthorizationRevision,
            pending.Envelope,
            cancellationToken).ConfigureAwait(false);
        if (write is ProviderAuthorizationWriteResult.Conflict conflict)
        {
            await using (conflict.ConfigureAwait(false))
            {
                if (conflict.Current is null || !EnvelopeEqual(conflict.Current.Envelope, pending.Envelope))
                    throw Error("AuthorizationStoreConflict", plan, "Authorization state changed during recoverable session commit.");
            }
        }
        if (!await _transactions!.TryConsumeAsync(
                transaction.TransactionId,
                plan.AuthorizationScopeIdentity,
                transactionRevision,
                cancellationToken).ConfigureAwait(false))
            throw Error("TransactionConsumed", plan, "Another caller completed the device authorization transaction.");
        return DeviceStatus(transaction.TransactionId, ProviderDeviceAuthorizationStatusKind.Authorized);
    }

    private static bool EnvelopeEqual(ProviderAuthorizationEnvelope left, ProviderAuthorizationEnvelope right) =>
        string.Equals(left.SchemaVersion, right.SchemaVersion, StringComparison.Ordinal) &&
        left.ProtectedPayload.Value.Length == right.ProtectedPayload.Value.Length &&
        CryptographicOperations.FixedTimeEquals(left.ProtectedPayload.Value.Span, right.ProtectedPayload.Value.Span);

    private void ValidateDeviceTransaction(
        ProviderCredentialPlan plan,
        Prepared prepared,
        ProviderAuthorizationTransactionState transaction,
        string transactionId)
    {
        var strategy = Strategy(plan);
        if (transaction.StrategyId != strategy.Descriptor.StrategyId ||
            transaction.Identity != prepared.Normalized!.Identity ||
            transaction.Flow != ProviderAuthorizationFlow.DeviceAuthorization ||
            !string.Equals(transaction.TransactionId, transactionId, StringComparison.Ordinal))
            throw Error("TransactionInvalid", plan, "The protected device transaction identity is invalid.");
    }

    private static ProviderDeviceAuthorizationStatus DeviceStatus(
        string transactionId,
        ProviderDeviceAuthorizationStatusKind status,
        DateTimeOffset? nextPollAt = null,
        string? diagnosticCode = null) => new()
        {
            TransactionId = transactionId,
            Status = status,
            NextPollAt = nextPollAt,
            DiagnosticCode = diagnosticCode
        };

    private ProviderAuthorizationFlow RequireSingleFlow(ProviderCredentialPlan plan)
    {
        var flows = Strategy(plan).Descriptor.Flows.Distinct().ToArray();
        if (flows.Length != 1)
            throw Error("AuthorizationFlowRequired", plan, "Authorization requires an explicit flow selection.");
        return flows[0];
    }

    private void RequireSupportedFlow(ProviderCredentialPlan plan, ProviderAuthorizationFlow flow)
    {
        if (!Strategy(plan).Descriptor.Flows.Contains(flow))
            throw Error("AuthorizationFlowUnsupported", plan, $"Authorization flow '{flow}' is not supported.");
    }

    private IProviderAuthenticationStrategy Strategy(ProviderCredentialPlan plan) => Strategy(
        plan.Backend.ProviderKey, plan.Backend.BackendKey, plan.Family);

    private IProviderAuthenticationStrategy Strategy(string provider, string backend, ProviderClientFamily family) =>
        _strategies?.Find(provider, backend, ProviderAuthenticationKind.OAuth)
        ?? throw new ProviderAuthenticationException(ProviderAuthenticationFailureKind.UnsupportedAuthentication,
            provider, backend, family, "unavailable",
            "No authentication strategy is registered.", diagnosticCode: "AuthenticationStrategyMissing");

    private static ProviderAuthorizationSession Refreshed(
        ProviderAuthorizationSession current, ProviderAuthorizationRefreshResult refresh)
    {
        var refreshToken = refresh.RefreshTokenDisposition switch
        {
            ProviderRefreshTokenDisposition.RetainCurrent => Copy(current.Secrets.RefreshToken),
            ProviderRefreshTokenDisposition.Replace => Copy(refresh.Secrets.ReplacementRefreshToken
                ?? throw new InvalidOperationException("Refresh replacement token is missing.")),
            ProviderRefreshTokenDisposition.Remove => null,
            _ => throw new ArgumentOutOfRangeException(nameof(refresh))
        };
        return new ProviderAuthorizationSession
        {
            SchemaVersion = current.SchemaVersion,
            Secrets = new OwnedProviderAuthorizationSecretSet(Copy(refresh.Secrets.AccessToken)!, refreshToken, Copy(current.Secrets.ClientSecret)),
            TokenType = refresh.TokenType,
            ExpiresAt = refresh.ExpiresAt,
            GrantedScopes = refresh.GrantedScopes?.ToArray(),
            ClientId = current.ClientId,
            TokenEndpointAuthenticationMethod = current.TokenEndpointAuthenticationMethod,
            AuthorizationServer = current.AuthorizationServer,
            Subject = current.Subject,
            ProviderState = refresh.ProviderState is null ? null : new Dictionary<string, string>(refresh.ProviderState)
        };
    }

    private static void ValidateSession(ProviderCredentialPlan plan, NormalizedProviderAuthorizationRequest normalized,
        ProviderAuthorizationSession session)
    {
        if (!string.Equals(session.AuthorizationServer, normalized.Identity.AuthorizationServer, StringComparison.Ordinal))
            throw Error("IssuerMismatch", plan, "The authorization issuer does not match.");
        if (!string.Equals(session.ClientId, normalized.Identity.ClientIdentity, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(session.TokenEndpointAuthenticationMethod))
            throw Error("ClientIdentityMismatch", plan, "The authorization client identity or token authentication method does not match.");
        var granted = (session.GrantedScopes ?? []).ToHashSet(StringComparer.Ordinal);
        if (normalized.Grant.RequestedScopes.Any(scope => !granted.Contains(scope)))
            throw Error("InsufficientScope", plan, "The authorization grant is insufficient.");
    }

    private static void ValidateNormalized(ProviderCredentialRequest request, IProviderAuthenticationStrategy strategy,
        NormalizedProviderAuthorizationRequest normalized)
    {
        var oauth = request.Authentication as OAuthProviderAuthentication;
        var requestedScopes = (request.Audience.Scopes ?? [])
            .Concat(oauth?.Scopes ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var normalizedScopes = normalized.Grant.RequestedScopes
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (strategy.Descriptor.Kind != ProviderAuthenticationKind.OAuth ||
            normalized.Identity.ProviderKey != request.ProviderKey || normalized.Identity.BackendKey != request.BackendKey ||
            normalized.Identity.AccountId != oauth?.AccountId ||
            normalized.Identity.TrustDomainId != request.AuthorizationScope.TrustDomainId ||
            normalized.Identity.TenantId != request.AuthorizationScope.TenantId ||
            normalized.Identity.PrincipalId != request.AuthorizationScope.PrincipalId ||
            normalized.Identity.Resource != request.Audience.Resource?.AbsoluteUri ||
            normalized.Identity.Audience != request.Audience.Audience ||
            normalized.Grant.Audience.Resource?.AbsoluteUri != request.Audience.Resource?.AbsoluteUri ||
            normalized.Grant.Audience.Audience != request.Audience.Audience ||
            requestedScopes.Except(normalizedScopes, StringComparer.Ordinal).Any() ||
            normalized.Grant.RequestedScopeSetIdentity != Hash(string.Join('\n', normalizedScopes)) ||
            normalized.Grant.GrantIdentity != Hash($"{request.Audience.Resource?.AbsoluteUri}|{request.Audience.Audience}|{normalized.Grant.RequestedScopeSetIdentity}"))
            throw Error("NormalizationInvalid", request, "The strategy changed a protected identity boundary.");
    }

    private static ProviderCredentialRequest Snapshot(ProviderCredentialRequest source, ProviderAuthentication authentication) => new()
    {
        ProviderKey = source.ProviderKey, BackendKey = source.BackendKey, Family = source.Family, Authentication = authentication,
        AuthorizationScope = Scope(source.AuthorizationScope),
        Audience = new ProviderCredentialAudience
        {
            Resource = source.Audience.Resource is null ? null : new Uri(source.Audience.Resource.AbsoluteUri),
            Audience = source.Audience.Audience, Scopes = source.Audience.Scopes?.ToArray()
        }
    };

    private static ProviderAuthorizationScope Scope(ProviderAuthorizationScope value) => new()
    { TrustDomainId = value.TrustDomainId, TenantId = value.TenantId, PrincipalId = value.PrincipalId };
    private static ProviderAuthorizationScope Scope(ProviderCredentialPlan value) => new()
    { TrustDomainId = value.AuthorizationScope.TrustDomainId, TenantId = value.AuthorizationScope.TenantId, PrincipalId = value.AuthorizationScope.PrincipalId };

    private static ProviderAuthorizationGrantSnapshot Grant(ProviderCredentialAudience audience)
    {
        var scopes = (audience.Scopes ?? []).Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).OrderBy(static x => x, StringComparer.Ordinal).ToImmutableArray();
        var scopeIdentity = Hash(string.Join('\n', scopes));
        return new ProviderAuthorizationGrantSnapshot
        {
            GrantIdentity = Hash($"{audience.Resource?.AbsoluteUri}|{audience.Audience}|{scopeIdentity}"),
            RequestedScopes = scopes, RequestedScopeSetIdentity = scopeIdentity,
            Resource = audience.Resource is null ? null : new Uri(audience.Resource.AbsoluteUri), Audience = audience.Audience
        };
    }

    private static ProviderAuthorizationGrantSnapshot Grant(ProviderAuthorizationGrant grant) => new()
    {
        GrantIdentity = grant.GrantIdentity, RequestedScopes = grant.RequestedScopes.ToImmutableArray(),
        RequestedScopeSetIdentity = grant.RequestedScopeSetIdentity,
        Resource = grant.Audience.Resource is null ? null : new Uri(grant.Audience.Resource.AbsoluteUri),
        Audience = grant.Audience.Audience
    };

    private static string Reference(ProviderAuthentication value) => value switch
    {
        ApiKeyProviderAuthentication x => $"api-key:{x.SecretKey}",
        ExplicitApiKeyProviderAuthentication x => $"explicit:{x.RuntimeRegistrationName}",
        ExternalIdentityProviderAuthentication x => $"external:{x.CredentialName}",
        AnonymousProviderAuthentication => "anonymous",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private Prepared RequireOAuthPlan(ProviderCredentialPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_prepared.TryGetValue(plan, out var prepared) || prepared.Normalized is null || prepared.Store is null)
            throw new ArgumentException("The plan must be an OAuth plan prepared by this coordinator.", nameof(plan));
        return prepared;
    }

    private SemaphoreSlim Gate(ProviderCredentialPlan plan) =>
        _locks.GetOrAdd(plan.StableCredentialIdentity, static _ => new SemaphoreSlim(1, 1));

    private static ProviderAuthorizationStatus Status(
        ProviderAuthorizationStatusKind status,
        ProviderCredentialPlan plan,
        DateTimeOffset? expiresAt) => new()
    {
        Status = status,
        CredentialIdentity = plan.StableCredentialIdentity,
        ExpiresAt = expiresAt
    };

    private static IProviderCredentialLease Lease(ProviderCredentialPlan plan, ProviderCredential credential,
        string revision, DateTimeOffset? expiry) => new OwnedProviderCredentialLease(credential, plan.Identity,
            new ProviderCredentialGeneration(Hash($"{plan.StableCredentialIdentity}|{revision}")), expiry, CancellationToken.None);
    private static OwnedProviderSecretBuffer? Copy(IProviderSecretBuffer? value) =>
        value is null ? null : new OwnedProviderSecretBuffer(value.Value.Span);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void Validate(ProviderCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BackendKey); ArgumentNullException.ThrowIfNull(request.Authentication);
        ArgumentNullException.ThrowIfNull(request.AuthorizationScope); ArgumentException.ThrowIfNullOrWhiteSpace(request.AuthorizationScope.TrustDomainId);
        ArgumentNullException.ThrowIfNull(request.Audience);
    }

    private static ProviderAuthenticationException Error(string code, ProviderCredentialRequest request, string message) =>
        new(ProviderAuthenticationFailureKind.ConfigurationError, request.ProviderKey, request.BackendKey,
            request.Family, "unavailable", message, diagnosticCode: code);
    private static ProviderAuthenticationException Error(string code, ProviderCredentialPlan plan, string message) =>
        new(ProviderAuthenticationFailureKind.ConfigurationError, plan.Backend.ProviderKey, plan.Backend.BackendKey,
            plan.Family, plan.Identity.Subject, message, diagnosticCode: code);

    private sealed record Prepared(ProviderAuthentication Authentication, ProviderCredentialRequest Request,
        NormalizedProviderAuthorizationRequest? Normalized, ProviderAuthorizationStoreRegistration? Store);
}

internal sealed class OwnedProviderSecretBuffer : IProviderSecretBuffer
{
    private char[]? _value;
    internal OwnedProviderSecretBuffer(string value) => _value = value.ToCharArray();
    internal OwnedProviderSecretBuffer(ReadOnlySpan<char> value) => _value = value.ToArray();
    public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(OwnedProviderSecretBuffer));
    public ValueTask DisposeAsync() { var value = Interlocked.Exchange(ref _value, null); if (value is not null) Array.Clear(value); return ValueTask.CompletedTask; }
}

internal sealed class OwnedProviderAuthorizationSecretSet(IProviderSecretBuffer accessToken,
    IProviderSecretBuffer? refreshToken, IProviderSecretBuffer? clientSecret) : IProviderAuthorizationSecretSet
{
    private IProviderSecretBuffer? _access = accessToken, _refresh = refreshToken, _client = clientSecret;
    public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(OwnedProviderAuthorizationSecretSet));
    public IProviderSecretBuffer? RefreshToken => _refresh;
    public IProviderSecretBuffer? ClientSecret => _client;
    public async ValueTask DisposeAsync()
    {
        var buffers = new[] { Interlocked.Exchange(ref _access, null), Interlocked.Exchange(ref _refresh, null), Interlocked.Exchange(ref _client, null) };
        List<Exception>? failures = null;
        var unique = new HashSet<IProviderSecretBuffer>(ReferenceEqualityComparer.Instance);
        foreach (var buffer in buffers)
            if (buffer is not null) unique.Add(buffer);
        foreach (var buffer in unique)
            try { await buffer.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is not null) throw new AggregateException(failures);
    }
}

internal sealed class OwnedProviderCredentialLease(ProviderCredential credential, ProviderCredentialIdentity identity,
    ProviderCredentialGeneration generation, DateTimeOffset? expiresAt, CancellationToken rotationToken) : IProviderCredentialLease
{
    private ProviderCredential? _credential = credential;
    public ProviderCredential Credential => _credential ?? throw new ObjectDisposedException(nameof(OwnedProviderCredentialLease));
    public ProviderCredentialIdentity Identity { get; } = identity;
    public ProviderCredentialGeneration Generation { get; } = generation;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public CancellationToken RotationToken { get; } = rotationToken;
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _credential, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
}
