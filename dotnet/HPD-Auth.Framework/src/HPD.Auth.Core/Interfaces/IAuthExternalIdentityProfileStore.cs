namespace HPD.Auth.Core.Interfaces;

/// <summary>Persists optional external-provider profile metadata independently of login authority.</summary>
public interface IAuthExternalIdentityProfileStore
{
    /// <summary>Creates or updates the profile bound to one provider identity.</summary>
    /// <param name="request">The complete profile update.</param>
    /// <param name="cancellationToken">Cancels caller observation.</param>
    Task UpsertAsync(
        AuthExternalIdentityProfileUpdate request,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes one external-provider profile update.</summary>
public sealed record AuthExternalIdentityProfileUpdate
{
    /// <summary>Gets the authoritative Auth user.</summary>
    public required Guid UserId { get; init; }
    /// <summary>Gets the normalized provider name.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets the provider-owned subject identifier.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets canonical JSON containing the permitted provider profile.</summary>
    public required string CanonicalIdentityJson { get; init; }
    /// <summary>Gets the UTC sign-in instant.</summary>
    public required DateTimeOffset SignedInAt { get; init; }
}
