using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies the host-owned authentication transport.</summary>
public enum BaseStudioAuthenticationKind : byte { CookieBff = 1, Bearer }
/// <summary>Identifies why the shell transport requests current authorization.</summary>
public enum BaseStudioTransportPurpose : byte { Bootstrap = 1, Observation, CommandPreview, CommandExecution, ReceiptResolution, ArtifactStaging }
/// <summary>Identifies a stable authentication integration failure.</summary>
public enum BaseStudioAuthenticationFailure : byte
{ AuthenticationRequired = 1, SessionExpired, OriginRejected, AntiForgeryInvalid, CallbackInvalid, RefreshFailed, IntegrationUnavailable }
/// <summary>Identifies one graph-declared fresh-authentication assurance class.</summary>
public enum BaseStudioFreshAuthenticationClass : byte { Password = 1, MultiFactor, HardwareBound }
/// <summary>Identifies the browser ceremony required to complete a fresh-authentication challenge.</summary>
public enum BaseStudioFreshAuthenticationBrowserActionKind : byte { Redirect = 1, WebAuthn, ExternalIdentityProvider }

/// <summary>Represents a closed authentication integration result.</summary>
public sealed class BaseStudioAuthenticationResult<T> where T : class
{
    private BaseStudioAuthenticationResult(T? value, BaseStudioAuthenticationFailure? failure)
    { Value = value; Failure = failure; }
    /// <summary>Gets the value when successful.</summary>
    public T? Value { get; }
    /// <summary>Gets the stable failure when unsuccessful.</summary>
    public BaseStudioAuthenticationFailure? Failure { get; }
    /// <summary>Gets whether the operation succeeded.</summary>
    public bool IsSuccess => Value is not null;
    /// <summary>Creates a successful result.</summary>
    public static BaseStudioAuthenticationResult<T> Success(T value) => new(value ?? throw new ArgumentNullException(nameof(value)), null);
    /// <summary>Creates a stable failed result.</summary>
    public static BaseStudioAuthenticationResult<T> Failed(BaseStudioAuthenticationFailure failure)
    { StudioContractValidation.Enum(failure); return new(null, failure); }
}

/// <summary>Describes one exact host-owned production authentication integration.</summary>
public sealed class BaseStudioAuthenticationDescriptor
{
    private BaseStudioAuthenticationDescriptor(string id, int version, BaseStudioAuthenticationKind kind,
        string login, string callback, string logout, string session, ImmutableArray<string> origins,
        string? header, string? cookiePurpose, TimeSpan maximumSession, bool refresh,
        ImmutableArray<BaseStudioFreshAuthenticationClass> supportedFreshAuthentication, BaseStudioSha256 checksum)
    { IntegrationId = id; Version = version; Kind = kind; LoginRoute = login; CallbackRoute = callback;
      LogoutRoute = logout; SessionRoute = session; AllowedOrigins = origins; AntiForgeryHeaderName = header;
      AntiForgeryCookiePurpose = cookiePurpose; MaximumSession = maximumSession; RefreshSupported = refresh;
      SupportedFreshAuthentication = supportedFreshAuthentication; Checksum = checksum; }
    /// <summary>Gets the integration identity.</summary>
    public string IntegrationId { get; }
    /// <summary>Gets the integration version.</summary>
    public int Version { get; }
    /// <summary>Gets the transport kind.</summary>
    public BaseStudioAuthenticationKind Kind { get; }
    /// <summary>Gets the same-origin login route.</summary>
    public string LoginRoute { get; }
    /// <summary>Gets the same-origin callback route.</summary>
    public string CallbackRoute { get; }
    /// <summary>Gets the same-origin logout route.</summary>
    public string LogoutRoute { get; }
    /// <summary>Gets the same-origin session-observation route.</summary>
    public string SessionRoute { get; }
    /// <summary>Gets allowed canonical origins.</summary>
    public ImmutableArray<string> AllowedOrigins { get; }
    /// <summary>Gets the cookie-BFF anti-forgery header.</summary>
    public string? AntiForgeryHeaderName { get; }
    /// <summary>Gets the cookie-BFF protection purpose.</summary>
    public string? AntiForgeryCookiePurpose { get; }
    /// <summary>Gets the maximum admitted session lifetime.</summary>
    public TimeSpan MaximumSession { get; }
    /// <summary>Gets whether host-owned bearer refresh is supported.</summary>
    public bool RefreshSupported { get; }
    /// <summary>Gets the exact canonically ordered fresh-authentication assurance classes supported by the integration.</summary>
    public ImmutableArray<BaseStudioFreshAuthenticationClass> SupportedFreshAuthentication { get; }
    /// <summary>Gets the canonical descriptor checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums a production authentication descriptor.</summary>
    public static BaseStudioAuthenticationDescriptor Create(string id, int version, BaseStudioAuthenticationKind kind,
        string login, string callback, string logout, string session, IEnumerable<string> origins,
        string? antiForgeryHeader, string? antiForgeryCookiePurpose, TimeSpan maximumSession, bool refresh,
        IEnumerable<BaseStudioFreshAuthenticationClass> supportedFreshAuthentication)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Enum(kind);
        if (version < 1 || maximumSession <= TimeSpan.Zero || maximumSession > TimeSpan.FromDays(30) || maximumSession.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        ValidateRoute(login); ValidateRoute(callback); ValidateRoute(logout); ValidateRoute(session);
        ImmutableArray<string> ownedOrigins = StudioContractValidation.Materialize(origins, 16, false, nameof(origins));
        if (!ownedOrigins.SequenceEqual(ownedOrigins.Order(StringComparer.Ordinal)) || ownedOrigins.Distinct(StringComparer.Ordinal).Count() != ownedOrigins.Length ||
            ownedOrigins.Any(static value => !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.Fragment)))
            throw new ArgumentException("Studio authentication origins are not canonical HTTPS origins.", nameof(origins));
        bool cookie = kind == BaseStudioAuthenticationKind.CookieBff;
        if (cookie != (antiForgeryHeader is not null && antiForgeryCookiePurpose is not null) ||
            antiForgeryHeader is not null && (!antiForgeryHeader.StartsWith("X-", StringComparison.Ordinal) || antiForgeryHeader.Any(char.IsControl)))
            throw new ArgumentException("Studio anti-forgery authority differs from authentication kind.");
        StudioContractValidation.OptionalId(antiForgeryCookiePurpose);
        ImmutableArray<BaseStudioFreshAuthenticationClass> supported = StudioContractValidation.Materialize(supportedFreshAuthentication, 3, true, nameof(supportedFreshAuthentication));
        if (supported.Any(static value => !Enum.IsDefined(value)) || !supported.SequenceEqual(supported.OrderBy(static value => (byte)value)) || supported.Distinct().Count() != supported.Length)
            throw new ArgumentException("Fresh-authentication assurance inventory is not canonical.", nameof(supportedFreshAuthentication));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.authentication.v1", writer =>
        {
            writer.String(id); writer.Int32(version); writer.Enum(kind); writer.String(login); writer.String(callback); writer.String(logout); writer.String(session);
            writer.Count(ownedOrigins.Length); foreach (string origin in ownedOrigins) writer.String(origin);
            writer.OptionalString(antiForgeryHeader); writer.OptionalString(antiForgeryCookiePurpose);
            writer.Int64(checked((long)maximumSession.TotalMilliseconds)); writer.Boolean(refresh); writer.Count(supported.Length); foreach (var value in supported) writer.Enum(value);
        });
        return new(id, version, kind, login, callback, logout, session, ownedOrigins,
            antiForgeryHeader, antiForgeryCookiePurpose, maximumSession, refresh, supported, checksum);
    }

    private static void ValidateRoute(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256 || !value.StartsWith("/", StringComparison.Ordinal) || value.Contains("//", StringComparison.Ordinal) ||
            value.Contains('?') || value.Contains('#') || value.Any(char.IsControl))
            throw new ArgumentException("Studio authentication route is invalid.", nameof(value));
    }
}

/// <summary>Projects the current host-authenticated Studio session without credentials.</summary>
public sealed class BaseStudioSessionObservation
{
    private BaseStudioSessionObservation(long principal, BaseStudioSha256 session, string audience, BaseStudioSha256 scope,
        DateTimeOffset issued, DateTimeOffset expires, BaseStudioSha256 descriptor)
    { PrincipalGeneration = principal; SessionChecksum = session; Audience = audience; ProtectedScopeChecksum = scope;
      IssuedAtUtc = issued; ExpiresAtUtc = expires; DescriptorChecksum = descriptor; }
    /// <summary>Gets the positive principal generation.</summary>
    public long PrincipalGeneration { get; }
    /// <summary>Gets the opaque authenticated-session checksum.</summary>
    public BaseStudioSha256 SessionChecksum { get; }
    /// <summary>Gets the exact authenticated audience.</summary>
    public string Audience { get; }
    /// <summary>Gets the opaque protected-scope checksum.</summary>
    public BaseStudioSha256 ProtectedScopeChecksum { get; }
    /// <summary>Gets canonical issuance time.</summary>
    public DateTimeOffset IssuedAtUtc { get; }
    /// <summary>Gets canonical session expiry.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }
    /// <summary>Gets the authentication descriptor checksum.</summary>
    public BaseStudioSha256 DescriptorChecksum { get; }
    /// <summary>Creates one deeply owned validated authenticated session observation.</summary>
    public static BaseStudioSessionObservation Create(long principal, BaseStudioSha256 session, string audience,
        BaseStudioSha256 scope, DateTimeOffset issued, DateTimeOffset expires, BaseStudioSha256 descriptor)
    {
        if (principal < 1 || issued.Offset != TimeSpan.Zero || expires.Offset != TimeSpan.Zero || issued == default || expires <= issued)
            throw new ArgumentException("Studio session authority is invalid.");
        StudioContractValidation.Id(audience); ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(descriptor);
        return new(principal, BaseStudioSha256.FromDigest(session.ToArray()), new string(audience.AsSpan()),
            BaseStudioSha256.FromDigest(scope.ToArray()), issued, expires, BaseStudioSha256.FromDigest(descriptor.ToArray()));
    }
}

/// <summary>Represents one purpose-protected return target; its payload is opaque outside the shell.</summary>
public sealed class BaseStudioProtectedReturnTarget
{
    private readonly byte[] _value;
    internal BaseStudioProtectedReturnTarget(ReadOnlySpan<byte> value)
    { if (value.Length is < 32 or > 2_048) throw new ArgumentOutOfRangeException(nameof(value)); _value = value.ToArray(); }
    /// <summary>Returns a defensive copy of protected bytes.</summary>
    public byte[] ToArray() => _value.ToArray();
}

/// <summary>Confirms current request authentication and anti-forgery authority.</summary>
public sealed class BaseStudioTransportAuthorization
{
    private BaseStudioTransportAuthorization(BaseStudioSessionObservation session, BaseStudioTransportPurpose purpose,
        DateTimeOffset through, BaseStudioSha256 checksum)
    { Session = session; Purpose = purpose; AuthorizedThroughUtc = through; Checksum = checksum; }
    /// <summary>Gets the validated authenticated session.</summary>
    public BaseStudioSessionObservation Session { get; }
    /// <summary>Gets the exact transport purpose.</summary>
    public BaseStudioTransportPurpose Purpose { get; }
    /// <summary>Gets the nonrenewable request-authorization expiry.</summary>
    public DateTimeOffset AuthorizedThroughUtc { get; }
    /// <summary>Gets the purpose-bound authorization checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one validated purpose-bound transport authorization.</summary>
    public static BaseStudioTransportAuthorization Create(BaseStudioSessionObservation session,
        BaseStudioTransportPurpose purpose, DateTimeOffset authorizedThroughUtc)
    {
        ArgumentNullException.ThrowIfNull(session); StudioContractValidation.Enum(purpose);
        if (authorizedThroughUtc.Offset != TimeSpan.Zero || authorizedThroughUtc <= session.IssuedAtUtc || authorizedThroughUtc > session.ExpiresAtUtc)
            throw new ArgumentException("Studio transport authorization lifetime is invalid.", nameof(authorizedThroughUtc));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.transport-authorization.v1", writer =>
        { writer.Int64(session.PrincipalGeneration); writer.Checksum(session.SessionChecksum); writer.Enum(purpose);
          writer.String(BaseStudioResponseAuthority.CanonicalUtc(authorizedThroughUtc)); writer.Checksum(session.DescriptorChecksum); });
        return new(session, purpose, authorizedThroughUtc, checksum);
    }
}

/// <summary>Contains one short-lived opaque browser transport header issued by the host integration.</summary>
public sealed class BaseStudioBrowserAuthorization
{
    private BaseStudioBrowserAuthorization(string headerName, string headerValue, BaseStudioTransportAuthorization authority)
    { HeaderName = headerName; HeaderValue = headerValue; Authority = authority; }
    /// <summary>Gets the host-selected same-origin request header name.</summary>
    public string HeaderName { get; }
    /// <summary>Gets the opaque nonpersistent header value.</summary>
    public string HeaderValue { get; }
    /// <summary>Gets the exact purpose-bound transport authority.</summary>
    public BaseStudioTransportAuthorization Authority { get; }
    /// <summary>Creates a deeply owned bounded browser authorization.</summary>
    public static BaseStudioBrowserAuthorization Create(string headerName, string headerValue,
        BaseStudioTransportAuthorization authority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName); ArgumentException.ThrowIfNullOrWhiteSpace(headerValue);
        ArgumentNullException.ThrowIfNull(authority);
        if (!headerName.StartsWith("X-", StringComparison.Ordinal) || headerName.Length > 128 ||
            headerName.Any(static value => !char.IsAsciiLetterOrDigit(value) && value != '-') ||
            System.Text.Encoding.UTF8.GetByteCount(headerValue) > 2_048 || headerValue.Any(char.IsControl))
            throw new ArgumentException("Studio browser authorization is invalid.");
        return new(new string(headerName.AsSpan()), new string(headerValue.AsSpan()), authority);
    }
}

/// <summary>Requests fresh authentication for one exact reviewed identified command.</summary>
public sealed record BaseStudioFreshAuthenticationRequest
{
    /// <summary>Gets the shell-owned mutation request identity.</summary>
    public required string RequestIdentity { get; init; }
    /// <summary>Gets the exact disclosed command.</summary>
    public required string CommandId { get; init; }
    /// <summary>Gets the checksum-valid typed command target.</summary>
    public required BaseStudioResourceIdentity Target { get; init; }
    /// <summary>Gets the exact current preview checksum.</summary>
    public required BaseStudioSha256 PreviewChecksum { get; init; }
    /// <summary>Gets the principal generation captured by server transport authorization.</summary>
    public required long PrincipalGeneration { get; init; }
    /// <summary>Gets the authenticated-session checksum captured by the server.</summary>
    public required BaseStudioSha256 SessionChecksum { get; init; }
    /// <summary>Gets the protected-scope checksum captured by the server.</summary>
    public required BaseStudioSha256 ProtectedScopeChecksum { get; init; }
    /// <summary>Gets the graph-required assurance class.</summary>
    public required BaseStudioFreshAuthenticationClass RequiredAssurance { get; init; }
    /// <summary>Gets the maximum admitted age of the actual authentication ceremony.</summary>
    public required TimeSpan MaximumAuthenticationAge { get; init; }
    /// <summary>Gets the Runtime-accepted acquisition issue time.</summary>
    public required DateTimeOffset IssuedAtUtc { get; init; }
    /// <summary>Gets the nonrenewable acquisition expiry.</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>Contains a protected browser action whose target is interpreted only by the host integration.</summary>
public sealed record BaseStudioFreshAuthenticationBrowserAction
{
    private BaseStudioFreshAuthenticationBrowserAction(BaseStudioFreshAuthenticationBrowserActionKind kind, string target)
    { Kind = kind; Target = target; }
    /// <summary>Gets the closed browser-action discriminator.</summary>
    public BaseStudioFreshAuthenticationBrowserActionKind Kind { get; }
    /// <summary>Gets the bounded host-issued browser target or WebAuthn request.</summary>
    public string Target { get; }
    /// <summary>Creates one bounded host-issued browser action.</summary>
    public static BaseStudioFreshAuthenticationBrowserAction Create(BaseStudioFreshAuthenticationBrowserActionKind kind, string target)
    {
        StudioContractValidation.Enum(kind); ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (target.Length > 4096 || target.Any(char.IsControl)) throw new ArgumentException("Fresh-authentication browser action is invalid.", nameof(target));
        return new(kind, new string(target.AsSpan()));
    }
}

/// <summary>Contains one purpose-bound protected fresh-authentication continuation.</summary>
public sealed class BaseStudioFreshAuthenticationContinuation
{
    private readonly string _protectedValue;
    private BaseStudioFreshAuthenticationContinuation(string value, BaseStudioFreshAuthenticationBinding binding,
        string returnNavigationHandle, string protectionKeyId)
    { _protectedValue = value; Binding = binding; ReturnNavigationHandle = returnNavigationHandle; ProtectionKeyId = protectionKeyId; }
    /// <summary>Gets the exact draft binding protected by the integration.</summary>
    public BaseStudioFreshAuthenticationBinding Binding { get; }
    /// <summary>Gets the bounded shell return-navigation handle.</summary>
    public string ReturnNavigationHandle { get; }
    /// <summary>Gets the protection-key identity used by the integration.</summary>
    public string ProtectionKeyId { get; }
    /// <summary>Creates one protected continuation.</summary>
    public static BaseStudioFreshAuthenticationContinuation Create(string protectedValue, BaseStudioFreshAuthenticationBinding binding,
        string returnNavigationHandle, string protectionKeyId)
    {
        ValidateProtected(protectedValue); ArgumentNullException.ThrowIfNull(binding);
        StudioContractValidation.Id(returnNavigationHandle); StudioContractValidation.Id(protectionKeyId);
        return new(new string(protectedValue.AsSpan()), binding, new string(returnNavigationHandle.AsSpan()), new string(protectionKeyId.AsSpan()));
    }
    /// <summary>Returns the opaque browser projection.</summary>
    public override string ToString() => _protectedValue;
    internal static void ValidateProtected(string value)
    { ArgumentException.ThrowIfNullOrWhiteSpace(value); if (value.Length is < 32 or > 4096 || value.Any(static c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Protected fresh-authentication value is invalid."); }
}

/// <summary>Binds fresh authentication to one exact reviewed command and authenticated scope.</summary>
public sealed record BaseStudioFreshAuthenticationBinding
{
    private BaseStudioFreshAuthenticationBinding(string requestIdentity, string commandId, BaseStudioResourceIdentity target,
        BaseStudioSha256 preview, long principal, BaseStudioSha256 session, BaseStudioSha256 scope,
        BaseStudioFreshAuthenticationClass assurance, TimeSpan maximumAge, string integrationId,
        BaseStudioSha256 integrationChecksum, DateTimeOffset issued, DateTimeOffset expires, BaseStudioSha256 checksum)
    { RequestIdentity = requestIdentity; CommandId = commandId; Target = target; PreviewChecksum = preview; PrincipalGeneration = principal;
      SessionChecksum = session; ProtectedScopeChecksum = scope; RequiredAssurance = assurance; MaximumAuthenticationAge = maximumAge;
      IntegrationId = integrationId; IntegrationChecksum = integrationChecksum; IssuedAtUtc = issued; ExpiresAtUtc = expires; Checksum = checksum; }
    /// <summary>Gets the request identity.</summary>
    public string RequestIdentity { get; }
    /// <summary>Gets the registered command identity.</summary>
    public string CommandId { get; }
    /// <summary>Gets the typed target.</summary>
    public BaseStudioResourceIdentity Target { get; }
    /// <summary>Gets the preview checksum.</summary>
    public BaseStudioSha256 PreviewChecksum { get; }
    /// <summary>Gets the principal generation.</summary>
    public long PrincipalGeneration { get; }
    /// <summary>Gets the session checksum.</summary>
    public BaseStudioSha256 SessionChecksum { get; }
    /// <summary>Gets the protected-scope checksum.</summary>
    public BaseStudioSha256 ProtectedScopeChecksum { get; }
    /// <summary>Gets required assurance.</summary>
    public BaseStudioFreshAuthenticationClass RequiredAssurance { get; }
    /// <summary>Gets maximum authentication age.</summary>
    public TimeSpan MaximumAuthenticationAge { get; }
    /// <summary>Gets integration identity.</summary>
    public string IntegrationId { get; }
    /// <summary>Gets integration descriptor checksum.</summary>
    public BaseStudioSha256 IntegrationChecksum { get; }
    /// <summary>Gets issue time.</summary>
    public DateTimeOffset IssuedAtUtc { get; }
    /// <summary>Gets expiry.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }
    /// <summary>Gets the purpose-bound binding checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one exact immutable binding from server-captured authority.</summary>
    public static BaseStudioFreshAuthenticationBinding Create(BaseStudioFreshAuthenticationRequest request,
        string integrationId, BaseStudioSha256 integrationChecksum, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request); StudioContractValidation.Id(request.RequestIdentity); StudioContractValidation.Id(request.CommandId);
        ArgumentNullException.ThrowIfNull(request.Target); ArgumentNullException.ThrowIfNull(request.PreviewChecksum);
        ArgumentNullException.ThrowIfNull(request.SessionChecksum); ArgumentNullException.ThrowIfNull(request.ProtectedScopeChecksum);
        StudioContractValidation.Enum(request.RequiredAssurance); StudioContractValidation.Id(integrationId); ArgumentNullException.ThrowIfNull(integrationChecksum);
        if (request.PrincipalGeneration < 1 || request.MaximumAuthenticationAge <= TimeSpan.Zero || request.MaximumAuthenticationAge > TimeSpan.FromHours(24) ||
            request.IssuedAtUtc != issuedAtUtc || issuedAtUtc.Offset != TimeSpan.Zero || request.ExpiresAtUtc.Offset != TimeSpan.Zero || request.ExpiresAtUtc <= issuedAtUtc)
            throw new ArgumentException("Fresh-authentication binding authority is invalid.", nameof(request));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.fresh-authentication-binding.v1", w =>
        { w.String(request.RequestIdentity); w.String(request.CommandId); w.Checksum(request.Target.AuthorityChecksum); w.Checksum(request.PreviewChecksum);
          w.Int64(request.PrincipalGeneration); w.Checksum(request.SessionChecksum); w.Checksum(request.ProtectedScopeChecksum); w.Enum(request.RequiredAssurance);
          w.Int64(request.MaximumAuthenticationAge.Ticks); w.String(integrationId); w.Checksum(integrationChecksum);
          w.String(BaseStudioResponseAuthority.CanonicalUtc(issuedAtUtc)); w.String(BaseStudioResponseAuthority.CanonicalUtc(request.ExpiresAtUtc)); });
        return new(new(request.RequestIdentity.AsSpan()), new(request.CommandId.AsSpan()), request.Target,
            BaseStudioSha256.FromDigest(request.PreviewChecksum.ToArray()), request.PrincipalGeneration,
            BaseStudioSha256.FromDigest(request.SessionChecksum.ToArray()), BaseStudioSha256.FromDigest(request.ProtectedScopeChecksum.ToArray()),
            request.RequiredAssurance, request.MaximumAuthenticationAge, new(integrationId.AsSpan()), BaseStudioSha256.FromDigest(integrationChecksum.ToArray()),
            issuedAtUtc, request.ExpiresAtUtc, checksum);
    }
}

/// <summary>Contains protected single-command fresh-authentication authority.</summary>
public sealed class BaseStudioFreshAuthenticationAuthority
{
    private readonly string _protectedValue;
    private BaseStudioFreshAuthenticationAuthority(string value, BaseStudioFreshAuthenticationBinding binding,
        DateTimeOffset authenticatedAtUtc, BaseStudioFreshAuthenticationClass achievedAssurance, string protectionKeyId)
    { _protectedValue = value; Binding = binding; AuthenticatedAtUtc = authenticatedAtUtc; AchievedAssurance = achievedAssurance; ProtectionKeyId = protectionKeyId; }
    /// <summary>Gets the exact protected command binding.</summary>
    public BaseStudioFreshAuthenticationBinding Binding { get; }
    /// <summary>Gets actual authentication UTC.</summary>
    public DateTimeOffset AuthenticatedAtUtc { get; }
    /// <summary>Gets achieved assurance.</summary>
    public BaseStudioFreshAuthenticationClass AchievedAssurance { get; }
    /// <summary>Gets protection-key identity.</summary>
    public string ProtectionKeyId { get; }
    /// <summary>Creates a bounded protected authority issued by the host integration.</summary>
    public static BaseStudioFreshAuthenticationAuthority Create(string protectedValue, BaseStudioFreshAuthenticationBinding binding,
        DateTimeOffset authenticatedAtUtc, BaseStudioFreshAuthenticationClass achievedAssurance, string protectionKeyId)
    {
        BaseStudioFreshAuthenticationContinuation.ValidateProtected(protectedValue); ArgumentNullException.ThrowIfNull(binding);
        StudioContractValidation.Enum(achievedAssurance); StudioContractValidation.Id(protectionKeyId);
        if (authenticatedAtUtc.Offset != TimeSpan.Zero || authenticatedAtUtc < binding.IssuedAtUtc || authenticatedAtUtc > binding.ExpiresAtUtc || achievedAssurance < binding.RequiredAssurance)
            throw new ArgumentException("Fresh-authentication authority does not satisfy its binding.");
        return new(new string(protectedValue.AsSpan()), binding, authenticatedAtUtc, achievedAssurance, new(protectionKeyId.AsSpan()));
    }
    /// <summary>Returns the opaque browser projection.</summary>
    public override string ToString() => _protectedValue;
}

/// <summary>Closed fresh-authentication acquisition result.</summary>
public abstract record BaseStudioFreshAuthenticationResult
{
    private BaseStudioFreshAuthenticationResult() { }
    /// <summary>Fresh authentication is complete.</summary>
    public sealed record Satisfied(BaseStudioFreshAuthenticationAuthority Authority) : BaseStudioFreshAuthenticationResult;
    /// <summary>A protected browser ceremony must complete before this draft may resume.</summary>
    public sealed record Challenge(BaseStudioFreshAuthenticationContinuation Continuation, BaseStudioFreshAuthenticationBrowserAction BrowserAction) : BaseStudioFreshAuthenticationResult;
    /// <summary>The required assurance class is not supported by the integration.</summary>
    public sealed record Unsupported : BaseStudioFreshAuthenticationResult;
}

/// <summary>Integrates Studio with exactly one host-owned production authentication profile.</summary>
public interface IBaseStudioAuthenticationIntegration
{
    /// <summary>Gets the immutable integration descriptor.</summary>
    BaseStudioAuthenticationDescriptor Descriptor { get; }
    /// <summary>Observes the current authenticated session.</summary>
    ValueTask<BaseStudioAuthenticationResult<BaseStudioSessionObservation>> ObserveSessionAsync(HttpContext httpContext, CancellationToken cancellationToken);
    /// <summary>Protects one bounded same-origin return target for the current authentication transaction.</summary>
    ValueTask<BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>> ProtectReturnTargetAsync(
        HttpContext httpContext, string? relativeReturnTarget, CancellationToken cancellationToken);
    /// <summary>Begins sign-in using a purpose-protected return target.</summary>
    ValueTask BeginSignInAsync(HttpContext httpContext, BaseStudioProtectedReturnTarget target, CancellationToken cancellationToken);
    /// <summary>Completes the fixed authentication callback.</summary>
    ValueTask CompleteCallbackAsync(HttpContext httpContext, CancellationToken cancellationToken);
    /// <summary>Begins sign-out.</summary>
    ValueTask BeginSignOutAsync(HttpContext httpContext, CancellationToken cancellationToken);
    /// <summary>Authorizes one same-origin shell transport request.</summary>
    ValueTask<BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>> AuthorizeRequestAsync(
        HttpContext httpContext, BaseStudioTransportPurpose purpose, CancellationToken cancellationToken);
    /// <summary>Acquires one opaque, nonpersistent browser header for an exact transport purpose.</summary>
    ValueTask<BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(
        HttpContext httpContext, BaseStudioTransportPurpose purpose, CancellationToken cancellationToken);
    /// <summary>Acquires authority for one exact reviewed destructive or recovery command.</summary>
    ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(
        HttpContext httpContext, BaseStudioFreshAuthenticationRequest request, CancellationToken cancellationToken);
    /// <summary>Completes a protected fresh-authentication callback and resumes only its bound draft.</summary>
    ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(
        HttpContext httpContext, BaseStudioFreshAuthenticationContinuation continuation, CancellationToken cancellationToken);
}

/// <summary>Provides the single installed production authentication integration.</summary>
public sealed class BaseStudioAuthenticationProvider
{
    /// <summary>Initializes the provider and requires exactly one integration.</summary>
    public BaseStudioAuthenticationProvider(IEnumerable<IBaseStudioAuthenticationIntegration> integrations)
    { Integration = integrations?.Single() ?? throw new InvalidOperationException("Studio requires exactly one authentication integration."); }
    /// <summary>Gets the installed integration.</summary>
    public IBaseStudioAuthenticationIntegration Integration { get; }
}

/// <summary>Registers the host-owned Studio authentication integration.</summary>
public static class BaseStudioAuthenticationBuilderExtensions
{
    /// <summary>Adds one explicitly constructed authentication integration factory.</summary>
    public static HPDAIPlatformBuilder AddStudioAuthentication(this HPDAIPlatformBuilder builder,
        Func<IServiceProvider, IBaseStudioAuthenticationIntegration> factory)
    {
        ArgumentNullException.ThrowIfNull(builder); ArgumentNullException.ThrowIfNull(factory);
        builder.Services.AddSingleton(factory);
        if (!builder.Services.Any(static value => value.ServiceType == typeof(BaseStudioAuthenticationProvider)))
            builder.Services.AddSingleton<BaseStudioAuthenticationProvider>();
        return builder;
    }
}
