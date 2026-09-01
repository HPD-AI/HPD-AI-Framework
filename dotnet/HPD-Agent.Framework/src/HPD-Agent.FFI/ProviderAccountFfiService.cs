using System.Collections.Immutable;
using HPD.Agent.Providers;

namespace HPD.Agent.FFI;

/// <summary>Contains one portable provider-account operation crossing the native boundary.</summary>
public sealed record ProviderAccountFfiRequest
{
    /// <summary>Gets the canonical provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the canonical backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the client family whose authentication capability is requested.</summary>
    public required ProviderClientFamily Family { get; init; }
    /// <summary>Gets the portable authentication reference.</summary>
    public required ProviderAuthentication Authentication { get; init; }
    /// <summary>Gets the caller trust boundary.</summary>
    public required ProviderAuthorizationScope AuthorizationScope { get; init; }
    /// <summary>Gets the requested audience, resource, and scopes.</summary>
    public required ProviderCredentialAudience Audience { get; init; }
}

/// <summary>Selects one flow for a native provider authorization begin operation.</summary>
public sealed record BeginProviderAuthorizationFfiRequest
{
    /// <summary>Gets the portable provider-account selection.</summary>
    public required ProviderAccountFfiRequest Account { get; init; }
    /// <summary>Gets the authorization flow selected by the native host.</summary>
    public required ProviderAuthorizationFlow Flow { get; init; }
}

/// <summary>Contains a correlated native authorization-completion request.</summary>
public sealed record CompleteProviderAuthorizationFfiRequest
{
    /// <summary>Gets the portable provider-account selection.</summary>
    public required ProviderAccountFfiRequest Account { get; init; }
    /// <summary>Gets the transient browser response supplied by the native host.</summary>
    public required BrowserAuthorizationResponse Response { get; init; }
}

/// <summary>Identifies one native device authorization transaction.</summary>
public sealed record ProviderDeviceAuthorizationFfiRequest
{
    /// <summary>Gets the portable provider-account selection.</summary>
    public required ProviderAccountFfiRequest Account { get; init; }
    /// <summary>Gets the opaque device transaction identity.</summary>
    public required string TransactionId { get; init; }
}

/// <summary>Contains a redacted native account-operation failure.</summary>
public sealed record ProviderAccountFfiError
{
    /// <summary>Gets the stable non-secret diagnostic code.</summary>
    public required string DiagnosticCode { get; init; }
}

/// <summary>Projects provider account management onto an authorization-checked FFI boundary.</summary>
internal sealed class ProviderAccountFfiService
{
    private readonly ProviderAuthenticationCoordinator _coordinator;
    private readonly IProviderAuthenticationSelectionAuthorizer _authorizer;

    internal ProviderAccountFfiService(
        ProviderAuthenticationCoordinator coordinator,
        IProviderAuthenticationSelectionAuthorizer authorizer)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    }

    internal async ValueTask<ProviderAuthorizationChallenge> BeginAsync(
        BeginProviderAuthorizationFfiRequest request,
        CancellationToken cancellationToken = default) =>
        await _coordinator.BeginAuthorizationAsync(
            new BeginProviderAuthorizationRequest
            {
                Plan = await PrepareAsync(request.Account, cancellationToken).ConfigureAwait(false),
                Flow = request.Flow
            },
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask CompleteAsync(
        CompleteProviderAuthorizationFfiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Response);
        await _coordinator.CompleteBrowserAuthorizationAsync(
            await PrepareAsync(request.Account, cancellationToken).ConfigureAwait(false),
            request.Response,
            cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<ProviderDeviceAuthorizationStatus> AdvanceDeviceAsync(
        ProviderDeviceAuthorizationFfiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await _coordinator.AdvanceDeviceAuthorizationAsync(
            await PrepareAsync(request.Account, cancellationToken).ConfigureAwait(false),
            request.TransactionId,
            cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<ProviderDeviceAuthorizationStatus> GetDeviceStatusAsync(
        ProviderDeviceAuthorizationFfiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await _coordinator.GetDeviceAuthorizationStatusAsync(
            await PrepareAsync(request.Account, cancellationToken).ConfigureAwait(false),
            request.TransactionId,
            cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask CancelDeviceAsync(
        ProviderDeviceAuthorizationFfiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _coordinator.CancelDeviceAuthorizationAsync(
            await PrepareAsync(request.Account, cancellationToken).ConfigureAwait(false),
            request.TransactionId,
            cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<ProviderAuthorizationStatus> StatusAsync(
        ProviderAccountFfiRequest request,
        CancellationToken cancellationToken = default) =>
        await _coordinator.GetStatusAsync(
            await PrepareAsync(request, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask<ProviderDisconnectResult> DisconnectAsync(
        ProviderAccountFfiRequest request,
        CancellationToken cancellationToken = default) =>
        await _coordinator.DisconnectAsync(
            await PrepareAsync(request, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask<ProviderRevocationResult> RevokeAsync(
        ProviderAccountFfiRequest request,
        CancellationToken cancellationToken = default) =>
        await _coordinator.RevokeAsync(
            await PrepareAsync(request, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask<ProviderDisconnectResult> RevokeAndDisconnectAsync(
        ProviderAccountFfiRequest request,
        CancellationToken cancellationToken = default) =>
        await _coordinator.RevokeAndDisconnectAsync(
            await PrepareAsync(request, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<ProviderCredentialPlan> PrepareAsync(
        ProviderAccountFfiRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Authentication);
        ArgumentNullException.ThrowIfNull(request.AuthorizationScope);
        ArgumentNullException.ThrowIfNull(request.Audience);
        if (request.Authentication is ExplicitApiKeyProviderAuthentication)
            throw new AgentRunConfigurationException(
                "RuntimeAuthenticationNotPortable",
                "authentication",
                "FFI accepts only portable authentication references.",
                request.ProviderKey);

        var scopes = request.Authentication is OAuthProviderAuthentication oauth
            ? (oauth.Scopes ?? []).Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal)
                .ToImmutableArray()
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
            StableReferenceIdentity = "ffi-authorized-reference",
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
            Source = ProviderSelectionSource.Ffi
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
