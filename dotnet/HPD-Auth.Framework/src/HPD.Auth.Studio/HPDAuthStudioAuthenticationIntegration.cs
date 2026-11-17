using System.Buffers.Binary;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Studio;

/// <summary>Bridges one HPD.Auth-backed ASP.NET session to the hosted Studio authentication contract.</summary>
public sealed class HPDAuthStudioAuthenticationIntegration : IStudioAuthenticationIntegration
{
    private const string ProtectionKeyId = "hpd-auth-studio";
    private const string ReturnNavigationHandle = "studioFresh";
    private readonly HPDAuthStudioOptions _options;
    private readonly IDataProtector _antiForgery;
    private readonly IDataProtector _returnTargets;
    private readonly IDataProtector _freshContinuations;
    private readonly TimeProvider _time;

    public HPDAuthStudioAuthenticationIntegration(IServiceProvider services, HPDAuthStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Validate(options);
        Descriptor = StudioAuthenticationDescriptor.Create(options.IntegrationId, options.IntegrationVersion, options.Kind,
            options.StudioLoginRoute, options.StudioCallbackRoute, options.StudioLogoutRoute, options.StudioSessionRoute,
            options.AllowedOrigins.Order(StringComparer.Ordinal), options.Kind == StudioAuthenticationKind.CookieBff
                ? options.AntiForgeryHeaderName : null,
            options.Kind == StudioAuthenticationKind.CookieBff ? options.AntiForgeryPurpose : null,
            options.MaximumSession, options.Kind == StudioAuthenticationKind.Bearer,
            options.SupportedFreshAuthentication.OrderBy(static value => (byte)value));
        IDataProtectionProvider protection = services.GetRequiredService<IDataProtectionProvider>();
        _antiForgery = protection.CreateProtector(options.AntiForgeryPurpose + ":transport");
        _returnTargets = protection.CreateProtector(options.AntiForgeryPurpose + ":return-target");
        _freshContinuations = protection.CreateProtector(options.AntiForgeryPurpose + ":fresh");
        _time = services.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    public StudioAuthenticationDescriptor Descriptor { get; }

    public async ValueTask<StudioAuthenticationResult<StudioSessionObservation>> ObserveSessionAsync(
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        AuthenticatedSession? session = await AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false);
        return session is null
            ? StudioAuthenticationResult<StudioSessionObservation>.Failed(StudioAuthenticationFailure.AuthenticationRequired)
            : StudioAuthenticationResult<StudioSessionObservation>.Success(session.Observation);
    }

    public ValueTask<StudioAuthenticationResult<StudioProtectedReturnTarget>> ProtectReturnTargetAsync(
        HttpContext httpContext, string? relativeReturnTarget, CancellationToken cancellationToken)
    {
        string target = string.IsNullOrWhiteSpace(relativeReturnTarget) ? _options.DefaultReturnTarget : relativeReturnTarget;
        if (!SafeReturnTarget(target))
            return ValueTask.FromResult(StudioAuthenticationResult<StudioProtectedReturnTarget>.Failed(
                StudioAuthenticationFailure.CallbackInvalid));
        string payload = string.Join('\n', "v1", ProtectedUntil(_options.ReturnTargetLifetime), target);
        string protectedValue = Protect(_returnTargets, payload);
        return ValueTask.FromResult(StudioAuthenticationResult<StudioProtectedReturnTarget>.Success(
            StudioProtectedReturnTarget.Create(Encoding.UTF8.GetBytes(protectedValue))));
    }

    public async ValueTask BeginSignInAsync(
        HttpContext httpContext, StudioProtectedReturnTarget target, CancellationToken cancellationToken)
    {
        string? returnTarget = UnprotectReturnTarget(Encoding.UTF8.GetString(target.ToArray()));
        if (returnTarget is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (await AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false) is not null)
        {
            httpContext.Response.Redirect(returnTarget);
            return;
        }
        string token = WebEncoders.Base64UrlEncode(target.ToArray());
        string callback = QueryHelpers.AddQueryString(_options.StudioCallbackRoute, "target", token);
        string login = QueryHelpers.AddQueryString(_options.ProviderLoginRoute,
            _options.ProviderReturnQueryParameter, callback);
        httpContext.Response.Redirect(login);
    }

    public ValueTask CompleteCallbackAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        string? token = httpContext.Request.Query.TryGetValue("target", out var values) && values.Count == 1
            ? values[0] : null;
        if (token is null || token.Length is < 1 or > 4096)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return ValueTask.CompletedTask;
        }
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(token); }
        catch (FormatException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return ValueTask.CompletedTask;
        }
        string? target = UnprotectReturnTarget(Encoding.UTF8.GetString(bytes));
        if (target is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return ValueTask.CompletedTask;
        }
        httpContext.Response.Redirect(target);
        return ValueTask.CompletedTask;
    }

    public async ValueTask BeginSignOutAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (httpContext.RequestServices.GetService<SignInManager<HPD.Auth.Core.Entities.ApplicationUser>>() is { } signIn)
            await signIn.SignOutAsync().ConfigureAwait(false);
        else
            await httpContext.SignOutAsync(_options.AuthenticationScheme).ConfigureAwait(false);
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public async ValueTask<StudioAuthenticationResult<StudioTransportAuthorization>> AuthorizeRequestAsync(
        HttpContext httpContext, StudioTransportPurpose purpose, CancellationToken cancellationToken)
    {
        if (!OriginAllowed(httpContext))
            return StudioAuthenticationResult<StudioTransportAuthorization>.Failed(StudioAuthenticationFailure.OriginRejected);
        AuthenticatedSession? session = await AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false);
        if (session is null)
            return StudioAuthenticationResult<StudioTransportAuthorization>.Failed(StudioAuthenticationFailure.AuthenticationRequired);
        StudioTransportPurpose markerPurpose = purpose == StudioTransportPurpose.Bootstrap &&
            httpContext.Request.Path.Equals(Descriptor.LogoutRoute, StringComparison.Ordinal)
                ? StudioTransportPurpose.SignOut : purpose;
        if (_options.Kind == StudioAuthenticationKind.CookieBff && !ValidateMarker(httpContext, session, markerPurpose))
            return StudioAuthenticationResult<StudioTransportAuthorization>.Failed(StudioAuthenticationFailure.AntiForgeryInvalid);
        DateTimeOffset through = Min(_time.GetUtcNow().Add(_options.AuthorizationLifetime), session.Observation.ExpiresAtUtc);
        return StudioAuthenticationResult<StudioTransportAuthorization>.Success(
            StudioTransportAuthorization.Create(session.Observation, purpose, through));
    }

    public async ValueTask<StudioAuthenticationResult<StudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(
        HttpContext httpContext, StudioTransportPurpose purpose, CancellationToken cancellationToken)
    {
        if (!OriginAllowed(httpContext))
            return StudioAuthenticationResult<StudioBrowserAuthorization>.Failed(StudioAuthenticationFailure.OriginRejected);
        AuthenticatedSession? session = await AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false);
        if (session is null)
            return StudioAuthenticationResult<StudioBrowserAuthorization>.Failed(StudioAuthenticationFailure.AuthenticationRequired);
        DateTimeOffset through = Min(_time.GetUtcNow().Add(_options.AuthorizationLifetime), session.Observation.ExpiresAtUtc);
        StudioTransportAuthorization authority = StudioTransportAuthorization.Create(session.Observation, purpose, through);
        if (_options.Kind == StudioAuthenticationKind.Bearer)
        {
            string? token = BearerToken(httpContext);
            return token is null
                ? StudioAuthenticationResult<StudioBrowserAuthorization>.Failed(StudioAuthenticationFailure.AuthenticationRequired)
                : StudioAuthenticationResult<StudioBrowserAuthorization>.Success(
                    StudioBrowserAuthorization.Create("Authorization", "Bearer " + token, authority));
        }
        string marker = IssueMarker(session, purpose, through);
        return StudioAuthenticationResult<StudioBrowserAuthorization>.Success(
            StudioBrowserAuthorization.Create(_options.AntiForgeryHeaderName, marker, authority));
    }

    public async ValueTask<StudioAuthenticationResult<StudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(
        HttpContext httpContext, StudioFreshAuthenticationRequest request, CancellationToken cancellationToken)
    {
        if (!OriginAllowed(httpContext))
            return StudioAuthenticationResult<StudioFreshAuthenticationResult>.Failed(StudioAuthenticationFailure.OriginRejected);
        AuthenticatedSession? session = await AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false);
        if (session is null || !SessionMatches(request, session.Observation))
            return StudioAuthenticationResult<StudioFreshAuthenticationResult>.Failed(StudioAuthenticationFailure.AuthenticationRequired);
        StudioFreshAuthenticationBinding binding = StudioFreshAuthenticationBinding.Create(
            request, Descriptor.IntegrationId, Descriptor.Checksum, request.IssuedAtUtc);
        return StudioAuthenticationResult<StudioFreshAuthenticationResult>.Success(
            FreshResult(binding, session, allowChallenge: true));
    }

    public async ValueTask<StudioAuthenticationResult<StudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(
        HttpContext httpContext, StudioFreshAuthenticationContinuation continuation, CancellationToken cancellationToken)
    {
        if (!TryReadContinuation(continuation, out string? checksum) ||
            !StringComparer.Ordinal.Equals(checksum, Hex(continuation.Binding.Checksum)))
            return StudioAuthenticationResult<StudioFreshAuthenticationResult>.Failed(StudioAuthenticationFailure.CallbackInvalid);
        AuthenticatedSession? session = await AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false);
        if (session is null || !SessionMatches(continuation.Binding, session.Observation))
            return StudioAuthenticationResult<StudioFreshAuthenticationResult>.Failed(StudioAuthenticationFailure.AuthenticationRequired);
        return StudioAuthenticationResult<StudioFreshAuthenticationResult>.Success(
            FreshResult(continuation.Binding, session, allowChallenge: false));
    }

    private StudioFreshAuthenticationResult FreshResult(
        StudioFreshAuthenticationBinding binding, AuthenticatedSession session, bool allowChallenge)
    {
        StudioFreshAuthenticationClass achieved = AchievedAssurance(session.Principal);
        bool fresh = _time.GetUtcNow() - session.AuthenticatedAtUtc <= _options.MaximumFreshAuthenticationAge;
        if (fresh && achieved >= binding.RequiredAssurance)
        {
            string value = Protect(_freshContinuations,
                string.Join('\n', "authority", Hex(binding.Checksum), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))));
            DateTimeOffset authenticatedAt = session.AuthenticatedAtUtc < binding.IssuedAtUtc
                ? binding.IssuedAtUtc : session.AuthenticatedAtUtc;
            return new StudioFreshAuthenticationResult.Satisfied(StudioFreshAuthenticationAuthority.Create(
                value, binding, authenticatedAt, achieved, ProtectionKeyId));
        }
        if (!allowChallenge || string.IsNullOrWhiteSpace(_options.FreshAuthenticationRoute))
            return new StudioFreshAuthenticationResult.Unsupported();
        string continuationValue = Protect(_freshContinuations,
            string.Join('\n', "continuation", Hex(binding.Checksum), binding.ExpiresAtUtc.UtcTicks,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))));
        StudioFreshAuthenticationContinuation continuation = StudioFreshAuthenticationContinuation.Create(
            continuationValue, binding, ReturnNavigationHandle, ProtectionKeyId);
        string target = QueryHelpers.AddQueryString(_options.FreshAuthenticationRoute,
            "continuation", continuation.ToString());
        return new StudioFreshAuthenticationResult.Challenge(continuation,
            StudioFreshAuthenticationBrowserAction.Create(
                StudioFreshAuthenticationBrowserActionKind.ExternalIdentityProvider, target));
    }

    private async ValueTask<AuthenticatedSession?> AuthenticateAsync(
        HttpContext context, CancellationToken cancellationToken)
    {
        AuthenticateResult result = await context.AuthenticateAsync(_options.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is null) return null;
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset? issuedValue = result.Properties?.IssuedUtc?.ToUniversalTime() ??
            ClaimInstant(result.Principal, "auth_time", "iat", "nbf");
        if (issuedValue is null) return null;
        DateTimeOffset issued = issuedValue.Value;
        DateTimeOffset expires = result.Properties?.ExpiresUtc?.ToUniversalTime() ?? ClaimInstant(result.Principal, "exp")
            ?? issued.Add(_options.MaximumSession);
        if (expires <= now || issued > now) return null;
        StudioSessionObservation observation = Observation(result.Principal, issued, expires);
        return new AuthenticatedSession(result.Principal, observation, issued);
    }

    private StudioSessionObservation Observation(ClaimsPrincipal principal,
        DateTimeOffset issued, DateTimeOffset expires)
    {
        string userId = Claim(principal, ClaimTypes.NameIdentifier, "sub") ?? throw new InvalidOperationException();
        string securityStamp = Claim(principal, "AspNet.Identity.SecurityStamp", "security_stamp") ?? "";
        string sessionIdentity = Claim(principal, "session_id", "sid", "jti") ?? "cookie-ticket";
        long generation = PrincipalGeneration(userId, securityStamp, sessionIdentity);
        StudioSha256 session = Digest("session", Descriptor.IntegrationId, _options.AuthenticationScheme,
            userId, securityStamp, sessionIdentity);
        string scope = string.Join('\n', principal.Claims
            .Where(static claim => claim.Type is ClaimTypes.Role || claim.Type is "instance_id" or "tenant_id" or
                "subscription_tier" or "scope" or "aud")
            .Select(static claim => claim.Type + "=" + claim.Value)
            .Order(StringComparer.Ordinal));
        StudioSha256 scopeChecksum = Digest("scope", _options.Audience, scope);
        return StudioSessionObservation.Create(generation, session, _options.Audience,
            scopeChecksum, issued, expires, Descriptor.Checksum);
    }

    private string IssueMarker(AuthenticatedSession session, StudioTransportPurpose purpose, DateTimeOffset through)
    {
        string payload = string.Join('\n', "v1", session.Observation.PrincipalGeneration,
            Hex(session.Observation.SessionChecksum), ((byte)purpose).ToString(CultureInfo.InvariantCulture),
            through.UtcTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        return Protect(_antiForgery, payload);
    }

    private bool ValidateMarker(HttpContext context, AuthenticatedSession session, StudioTransportPurpose purpose)
    {
        string? marker = context.Request.Headers[_options.AntiForgeryHeaderName].SingleOrDefault();
        string? payload = Unprotect(_antiForgery, marker);
        if (payload is null) return false;
        string[] fields = payload.Split('\n');
        if (fields is not ["v1", var generation, var checksum, var purposeValue, var expiry, _] ||
            !long.TryParse(generation, NumberStyles.None, CultureInfo.InvariantCulture, out long expectedGeneration) ||
            !byte.TryParse(purposeValue, NumberStyles.None, CultureInfo.InvariantCulture, out byte expectedPurpose) ||
            !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out long expiryTicks) ||
            expectedGeneration != session.Observation.PrincipalGeneration ||
            !StringComparer.Ordinal.Equals(checksum, Hex(session.Observation.SessionChecksum)) ||
            expectedPurpose != (byte)purpose || new DateTimeOffset(expiryTicks, TimeSpan.Zero) <= _time.GetUtcNow())
            return false;
        return true;
    }

    private bool TryReadContinuation(StudioFreshAuthenticationContinuation continuation, out string? checksum)
    {
        checksum = null;
        string? payload = Unprotect(_freshContinuations, continuation.ToString());
        if (payload is null) return false;
        string[] fields = payload.Split('\n');
        if (fields is not ["continuation", var value, var expiry, _] ||
            !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) ||
            new DateTimeOffset(ticks, TimeSpan.Zero) <= _time.GetUtcNow()) return false;
        checksum = value;
        return true;
    }

    private string? UnprotectReturnTarget(string value)
    {
        string? payload = Unprotect(_returnTargets, value);
        if (payload is null) return null;
        string[] fields = payload.Split('\n');
        if (fields is not ["v1", var expiry, var target] ||
            !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) ||
            new DateTimeOffset(ticks, TimeSpan.Zero) <= _time.GetUtcNow() || !SafeReturnTarget(target))
            return null;
        return target;
    }

    private StudioFreshAuthenticationClass AchievedAssurance(ClaimsPrincipal principal)
    {
        foreach (Claim claim in principal.Claims)
        {
            if (!_options.MultiFactorClaimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (string part in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (_options.HardwareClaimValues.Contains(part, StringComparer.OrdinalIgnoreCase))
                    return StudioFreshAuthenticationClass.HardwareBound;
                if (_options.MultiFactorClaimValues.Contains(part, StringComparer.OrdinalIgnoreCase))
                    return StudioFreshAuthenticationClass.MultiFactor;
            }
        }
        return StudioFreshAuthenticationClass.Password;
    }

    private long ProtectedUntil(TimeSpan lifetime) => _time.GetUtcNow().Add(lifetime).UtcTicks;

    private static long PrincipalGeneration(string userId, string securityStamp, string session)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', userId, securityStamp, session)), digest);
        long value = (long)(BinaryPrimitives.ReadUInt64BigEndian(digest) & 0x7fff_ffff_ffff_ffffUL);
        return value == 0 ? 1 : value;
    }

    private static StudioSha256 Digest(params string[] values) =>
        StudioSha256.FromDigest(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values))));

    private static string? Claim(ClaimsPrincipal principal, params string[] types)
    {
        foreach (string type in types)
            if (principal.FindFirst(type)?.Value is { Length: > 0 } value) return value;
        return null;
    }

    private static DateTimeOffset? ClaimInstant(ClaimsPrincipal principal, params string[] types)
    {
        string? value = Claim(principal, types);
        if (value is null) return null;
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset instant)
            ? instant : null;
    }

    private static bool SessionMatches(StudioFreshAuthenticationRequest request, StudioSessionObservation session) =>
        request.PrincipalGeneration == session.PrincipalGeneration &&
        StudioSha256.FixedTimeEquals(request.SessionChecksum, session.SessionChecksum) &&
        StudioSha256.FixedTimeEquals(request.ProtectedScopeChecksum, session.ProtectedScopeChecksum);

    private static bool SessionMatches(StudioFreshAuthenticationBinding binding, StudioSessionObservation session) =>
        binding.PrincipalGeneration == session.PrincipalGeneration &&
        StudioSha256.FixedTimeEquals(binding.SessionChecksum, session.SessionChecksum) &&
        StudioSha256.FixedTimeEquals(binding.ProtectedScopeChecksum, session.ProtectedScopeChecksum);

    private bool OriginAllowed(HttpContext context)
    {
        string? origin = context.Request.Headers.Origin.SingleOrDefault();
        return origin is null || _options.AllowedOrigins.Contains(origin, StringComparer.Ordinal);
    }

    private static string? BearerToken(HttpContext context)
    {
        string? authorization = context.Request.Headers.Authorization.SingleOrDefault();
        return authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization["Bearer ".Length..] : null;
    }

    private static bool SafeReturnTarget(string value) =>
        value.Length is >= 1 and <= 2_048 && value.StartsWith("/", StringComparison.Ordinal) &&
        !value.StartsWith("//", StringComparison.Ordinal) && !value.Any(char.IsControl);

    private static string Hex(StudioSha256 value) =>
        Convert.ToHexStringLower(value.ToArray());

    private string Protect(IDataProtector protector, string value) =>
        WebEncoders.Base64UrlEncode(protector.Protect(Encoding.UTF8.GetBytes(value)));

    private string? Unprotect(IDataProtector protector, string? value)
    {
        if (value is null || value.Length is < 1 or > 8192) return null;
        try
        {
            byte[] bytes = WebEncoders.Base64UrlDecode(value);
            return Encoding.UTF8.GetString(protector.Unprotect(bytes));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static void Validate(HPDAuthStudioOptions options)
    {
        if (!options.AllowedOrigins.Any() || options.AllowedOrigins.Count > 16 ||
            options.AuthenticationScheme.Length is < 1 or > 128 || options.AuthenticationScheme.Any(char.IsControl) ||
            options.Audience.Length is < 1 or > 128 ||
            options.ProviderLoginRoute.Length is < 1 or > 256 || !options.ProviderLoginRoute.StartsWith("/", StringComparison.Ordinal) ||
            options.ProviderReturnQueryParameter.Length is < 1 or > 64 ||
            options.FreshAuthenticationRoute.Length is < 1 or > 256 || !options.FreshAuthenticationRoute.StartsWith("/", StringComparison.Ordinal) ||
            options.MaximumFreshAuthenticationAge <= TimeSpan.Zero || options.MaximumFreshAuthenticationAge > TimeSpan.FromHours(24) ||
            options.AuthorizationLifetime <= TimeSpan.Zero || options.AuthorizationLifetime > TimeSpan.FromMinutes(10) ||
            options.ReturnTargetLifetime <= TimeSpan.Zero || options.ReturnTargetLifetime > TimeSpan.FromMinutes(30) ||
            options.FreshChallengeLifetime <= TimeSpan.Zero || options.FreshChallengeLifetime > TimeSpan.FromMinutes(30))
            throw new ArgumentException("HPD Auth Studio options are invalid.", nameof(options));
    }

    private sealed record AuthenticatedSession(
        ClaimsPrincipal Principal, StudioSessionObservation Observation, DateTimeOffset AuthenticatedAtUtc);
}
