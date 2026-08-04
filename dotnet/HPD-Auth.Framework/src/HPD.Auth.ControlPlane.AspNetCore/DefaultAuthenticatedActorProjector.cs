using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace HPD.Auth.ControlPlane;

internal sealed class DefaultAuthenticatedActorProjector(ControlPlaneRegistry registry)
    : IAuthenticatedActorProjector
{
    public ValueTask<AuthenticatedActorProjection> ProjectAsync(
        HttpContext context,
        string controlPlaneProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.User.Identity?.IsAuthenticated != true)
            throw Failure("hpd.auth.actor.unauthenticated");

        var endpointProfile = context.GetEndpoint()?.Metadata.GetMetadata<ControlPlaneEndpointMetadata>();
        if (endpointProfile is null || !string.Equals(endpointProfile.Profile, controlPlaneProfile, StringComparison.Ordinal))
            throw Failure("hpd.auth.actor.profileMissing");

        ControlPlaneProfile profile;
        try
        {
            profile = registry.GetProfile(controlPlaneProfile);
        }
        catch (InvalidOperationException)
        {
            throw Failure("hpd.auth.actor.profileMissing");
        }

        var actorId = Read(context.User, profile.ActorIdentifierClaim, 256, required: true, identifier: true)!;
        var tenant = ReadOptional(context.User, profile.TenantClaim, 128);
        var method = ReadOptional(context.User, profile.AuthenticationMethodClaim, 64);
        var assurance = ReadOptional(context.User, profile.AssuranceClaim, 64);

        return ValueTask.FromResult(new AuthenticatedActorProjection
        {
            ActorId = actorId,
            AuthenticationProfile = new string(profile.AuthenticationProfile.AsSpan()),
            TenantId = tenant,
            AuthenticationMethod = method,
            AssuranceLevel = assurance
        });
    }

    private static string? ReadOptional(ClaimsPrincipal principal, string? claimType, int maximumBytes) =>
        claimType is null ? null : Read(principal, claimType, maximumBytes, required: false, identifier: false);

    private static string? Read(
        ClaimsPrincipal principal,
        string claimType,
        int maximumBytes,
        bool required,
        bool identifier)
    {
        var values = principal.FindAll(claimType)
            .Select(static claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (values.Length == 0)
        {
            if (required)
                throw Failure("hpd.auth.actor.identifierMissing");
            return null;
        }

        if (values.Length > 1)
            throw Failure(identifier
                ? "hpd.auth.actor.identifierAmbiguous"
                : "hpd.auth.actor.factAmbiguous");

        var value = values[0];
        if (string.IsNullOrEmpty(value) || value.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(value) > maximumBytes)
            throw Failure("hpd.auth.actor.factInvalid");

        return new string(value.AsSpan());
    }

    private static AuthenticatedActorProjectionException Failure(string code) => new(code);
}
