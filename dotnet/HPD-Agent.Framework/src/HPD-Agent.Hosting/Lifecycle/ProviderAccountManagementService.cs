using System.Collections.Immutable;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Providers;

namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>Manages provider authorization without exposing credentials or decrypted sessions.</summary>
public interface IProviderAccountManagementService
{
    /// <summary>Begins a protected authorization transaction and returns its redacted challenge.</summary>
    ValueTask<ProviderAuthorizationChallenge> BeginAuthorizationAsync(BeginProviderAuthorizationHostRequest request, CancellationToken cancellationToken = default);
    /// <summary>Validates and completes a previously begun protected authorization transaction.</summary>
    ValueTask CompleteAuthorizationAsync(CompleteProviderAuthorizationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Advances one device authorization transaction by at most one provider step.</summary>
    ValueTask<ProviderDeviceAuthorizationStatus> AdvanceDeviceAuthorizationAsync(ProviderDeviceAuthorizationOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads one device transaction without contacting the provider.</summary>
    ValueTask<ProviderDeviceAuthorizationStatus> GetDeviceAuthorizationStatusAsync(ProviderDeviceAuthorizationOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Cancels one device transaction without changing an existing account session.</summary>
    ValueTask CancelDeviceAuthorizationAsync(ProviderDeviceAuthorizationOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads non-secret authorization status.</summary>
    ValueTask<ProviderAuthorizationStatus> GetStatusAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Conditionally removes local authorization state.</summary>
    ValueTask<ProviderDisconnectResult> DisconnectAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Attempts remote revocation without deleting local state.</summary>
    ValueTask<ProviderRevocationResult> RevokeAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Attempts remote revocation and independently reports local deletion.</summary>
    ValueTask<ProviderDisconnectResult> RevokeAndDisconnectAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Default Hosting projection over the provider authentication coordinator.</summary>
public sealed class ProviderAccountManagementService : IProviderAccountManagementService
{
    private readonly ProviderAuthenticationCoordinator _coordinator;
    private readonly IProviderAuthenticationSelectionAuthorizer _authorizer;

    /// <summary>Creates a host account-management projection with mandatory selection authorization.</summary>
    public ProviderAccountManagementService(
        ProviderAuthenticationCoordinator coordinator,
        IProviderAuthenticationSelectionAuthorizer authorizer)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    }

    /// <inheritdoc />
    public async ValueTask<ProviderAuthorizationChallenge> BeginAuthorizationAsync(BeginProviderAuthorizationHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await PrepareAuthorizedAsync(request.Account, cancellationToken).ConfigureAwait(false);
        return await _coordinator.BeginAuthorizationAsync(new BeginProviderAuthorizationRequest
        {
            Plan = plan,
            Flow = request.Flow
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CompleteAuthorizationAsync(CompleteProviderAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Response);
        var plan = await PrepareAuthorizedAsync(request.Account, cancellationToken).ConfigureAwait(false);
        await _coordinator.CompleteBrowserAuthorizationAsync(plan, request.Response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ProviderDeviceAuthorizationStatus> AdvanceDeviceAuthorizationAsync(
        ProviderDeviceAuthorizationOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await PrepareAuthorizedAsync(request.Account, cancellationToken).ConfigureAwait(false);
        return await _coordinator.AdvanceDeviceAuthorizationAsync(
            plan, request.TransactionId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ProviderDeviceAuthorizationStatus> GetDeviceAuthorizationStatusAsync(
        ProviderDeviceAuthorizationOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await _coordinator.GetDeviceAuthorizationStatusAsync(
            await PrepareAuthorizedAsync(request.Account, cancellationToken).ConfigureAwait(false),
            request.TransactionId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CancelDeviceAuthorizationAsync(
        ProviderDeviceAuthorizationOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _coordinator.CancelDeviceAuthorizationAsync(
            await PrepareAuthorizedAsync(request.Account, cancellationToken).ConfigureAwait(false),
            request.TransactionId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ProviderAuthorizationStatus> GetStatusAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default) =>
        await _coordinator.GetStatusAsync(await PrepareAuthorizedAsync(request, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<ProviderDisconnectResult> DisconnectAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default) =>
        await _coordinator.DisconnectAsync(await PrepareAuthorizedAsync(request, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<ProviderRevocationResult> RevokeAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default) =>
        await _coordinator.RevokeAsync(await PrepareAuthorizedAsync(request, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<ProviderDisconnectResult> RevokeAndDisconnectAsync(ProviderAccountOperationRequest request, CancellationToken cancellationToken = default) =>
        await _coordinator.RevokeAndDisconnectAsync(await PrepareAuthorizedAsync(request, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    private async ValueTask<ProviderCredentialPlan> PrepareAuthorizedAsync(
        ProviderAccountOperationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Authentication);
        if (request.Authentication is ExplicitApiKeyProviderAuthentication)
            throw new AgentRunConfigurationException("RuntimeAuthenticationNotPortable", "authentication",
                "Hosting accepts only portable authentication references.", request.ProviderKey);
        var scopes = request.Authentication is OAuthProviderAuthentication oauth
            ? (oauth.Scopes ?? []).Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray()
            : ImmutableArray<string>.Empty;
        var authentication = new EffectiveProviderAuthentication
        {
            Configuration = ProviderClientConfigSnapshot.CloneAuthentication(request.Authentication),
            Kind = request.Authentication switch
            {
                ApiKeyProviderAuthentication => ProviderAuthenticationKind.ApiKey,
                OAuthProviderAuthentication => ProviderAuthenticationKind.OAuth,
                ExternalIdentityProviderAuthentication => ProviderAuthenticationKind.ExternalIdentity,
                AnonymousProviderAuthentication => ProviderAuthenticationKind.Anonymous,
                _ => throw new ArgumentOutOfRangeException(nameof(request.Authentication))
            },
            StableReferenceIdentity = "hosting-authorized-reference",
            Scopes = scopes,
            AuthorizationProfile = (request.Authentication as OAuthProviderAuthentication)?.AuthorizationProfile,
            AuthorizationStoreIdentity = (request.Authentication as OAuthProviderAuthentication)?.StoreKey
        };
        await _authorizer.AuthorizeAsync(new ProviderAuthenticationSelectionContext
        {
            Caller = new ProviderAuthorizationScopeSnapshot
            {
                TrustDomainId = request.AuthorizationScope.TrustDomainId,
                TenantId = request.AuthorizationScope.TenantId,
                PrincipalId = request.AuthorizationScope.PrincipalId
            },
            Backend = new ProviderBackendIdentity(request.ProviderKey, request.BackendKey),
            Family = request.Family,
            Authentication = authentication,
            Source = ProviderSelectionSource.Hosting
        }, cancellationToken).ConfigureAwait(false);
        return await _coordinator.PrepareAsync(new ProviderCredentialRequest
        {
            ProviderKey = request.ProviderKey,
            BackendKey = request.BackendKey,
            Family = request.Family,
            Authentication = authentication.Configuration,
            AuthorizationScope = request.AuthorizationScope,
            Audience = request.Audience
        }, cancellationToken).ConfigureAwait(false);
    }
}
