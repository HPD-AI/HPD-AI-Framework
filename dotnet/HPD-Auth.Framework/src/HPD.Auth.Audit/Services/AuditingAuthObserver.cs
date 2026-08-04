using System.Collections.Immutable;
using System.Net;
using System.Text;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Core.Audit;
using HPD.Auth.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.Audit.Services;

public sealed partial class AuditingAuthObserver(
    IAuthAuditWriter writer,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditingAuthObserver> logger,
    IAuthCorrelationContext? correlationContext = null)
{
    public async ValueTask HandleAsync(AuthEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var write = Map(evt, correlationContext?.CorrelationId);
        if (write is null)
        {
            InvalidEvent(logger);
            return;
        }

        try
        {
            await writer.WriteAsync(write, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            AuditCancelled(logger);
            return;
        }
        catch
        {
            AuditFailed(logger);
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        await DispatchAsync(scope.ServiceProvider, evt, cancellationToken);
    }

    private async ValueTask DispatchAsync(
        IServiceProvider services,
        AuthEvent evt,
        CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case UserRegisteredEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case UserLoggedInEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case UserLoggedOutEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case LoginFailedEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case PasswordChangedEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case PasswordResetRequestedEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case EmailConfirmedEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case TwoFactorEnabledEvent value: await DispatchAsync(services, value, cancellationToken); break;
            case SessionRevokedEvent value: await DispatchAsync(services, value, cancellationToken); break;
            default: UnknownEvent(logger); break;
        }
    }

    private async ValueTask DispatchAsync<TEvent>(
        IServiceProvider services,
        TEvent evt,
        CancellationToken cancellationToken)
        where TEvent : AuthEvent
    {
        foreach (var observer in services.GetServices<IAuthEventObserver<TEvent>>())
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            try
            {
                await observer.HandleAsync(evt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                ObserverFailed(logger);
            }
        }
    }

    private static AuthAuditWrite? Map(AuthEvent evt, string? correlationId)
    {
        var ip = CanonicalIp(evt.AuthContext?.IpAddress);
        var agent = BoundedUserAgent(evt.AuthContext?.UserAgent);
        return evt switch
        {
            UserRegisteredEvent e when e.UserId != Guid.Empty => Write(
                "user.register", true, e.UserId, null, null, ip, agent,
                correlationId, Fact("registration-method", RegistrationMethod(e.RegistrationMethod))),
            UserLoggedInEvent e when e.UserId != Guid.Empty => Write(
                "user.login", true, e.UserId, null, null, ip, agent,
                correlationId, Fact("authentication-method", AuthenticationMethod(e.AuthMethod))),
            UserLoggedOutEvent e when e.UserId != Guid.Empty && e.SessionId != Guid.Empty => Write(
                "user.logout", true, e.UserId, e.SessionId, null, ip, agent, correlationId),
            LoginFailedEvent e => Write(
                "user.login.failed", false, null, null, Failure(e.Reason), ip, agent, correlationId),
            PasswordChangedEvent e when e.UserId != Guid.Empty => Write(
                "password.change", true, e.UserId, null, null, ip, agent, correlationId),
            PasswordResetRequestedEvent e when e.UserId != Guid.Empty => Write(
                "password.reset.request", true, e.UserId, null, null, ip, agent, correlationId),
            EmailConfirmedEvent e when e.UserId != Guid.Empty => Write(
                "email.confirm", true, e.UserId, null, null, ip, agent, correlationId),
            TwoFactorEnabledEvent e when e.UserId != Guid.Empty => Write(
                "2fa.enable", true, e.UserId, null, null, ip, agent,
                correlationId, Fact("authentication-method", AuthenticationMethod(e.Method))),
            SessionRevokedEvent e when e.UserId != Guid.Empty && e.SessionId != Guid.Empty => Write(
                "session.revoke", true, e.UserId, e.SessionId, null, ip, agent,
                correlationId, Fact("revoked-by", RevokedBy(e.RevokedBy))),
            _ => null
        };
    }

    private static AuthAuditWrite Write(
        string action,
        bool success,
        Guid? userId,
        Guid? sessionId,
        string? failure,
        string? ip,
        string? agent,
        string? correlationId,
        params AuthAuditFact[] facts) => new(
            action,
            "authentication",
            success,
            userId,
            sessionId,
            ip,
            agent,
            failure,
            correlationId is null ? null : new string(correlationId.AsSpan()),
            facts.OrderBy(static fact => fact.Key, StringComparer.Ordinal).ToImmutableArray());

    private static AuthAuditFact Fact(string key, string value) => new(key, value);

    private static string RegistrationMethod(string? value) => value?.ToLowerInvariant() switch
    {
        "email" => "email",
        "magic_link" or "magic-link" => "magic-link",
        "google" or "github" or "oauth" => "oauth",
        _ => "other"
    };

    private static string AuthenticationMethod(string? value) => value?.ToLowerInvariant() switch
    {
        "password" => "password",
        "google" or "github" or "oauth" => "oauth",
        "passkey" => "passkey",
        "magic_link" or "magic-link" => "magic-link",
        "totp" => "totp",
        "totp_disabled" or "totp-disabled" => "totp-disabled",
        _ => "other"
    };

    private static string RevokedBy(string? value) => value?.ToLowerInvariant() switch
    {
        "user" => "user",
        "admin" => "admin",
        "system" => "system",
        _ => "other"
    };

    private static string Failure(string? value) => value switch
    {
        "invalid_password" or "user_not_found" => "authentication.invalid-credentials",
        "account_locked" => "authentication.account-locked",
        "email_not_confirmed" => "authentication.email-unconfirmed",
        "account_disabled" => "authentication.account-disabled",
        _ => "authentication.failed"
    };

    private static string? CanonicalIp(string? value) =>
        IPAddress.TryParse(value, out var address) ? address.ToString() : null;

    private static string? BoundedUserAgent(string? value)
    {
        if (value is null)
            return null;
        var cleaned = new string(value.Where(static character => !char.IsControl(character)).ToArray());
        while (Encoding.UTF8.GetByteCount(cleaned) > 512)
            cleaned = cleaned[..^1];
        return cleaned.Length == 0 ? null : cleaned;
    }

    [LoggerMessage(2401, LogLevel.Warning, "Auth audit event was invalid")]
    private static partial void InvalidEvent(ILogger logger);
    [LoggerMessage(2402, LogLevel.Debug, "Auth audit dispatch was cancelled")]
    private static partial void AuditCancelled(ILogger logger);
    [LoggerMessage(2403, LogLevel.Error, "Auth audit write failed")]
    private static partial void AuditFailed(ILogger logger);
    [LoggerMessage(2404, LogLevel.Error, "Auth audit observer failed")]
    private static partial void ObserverFailed(ILogger logger);
    [LoggerMessage(2405, LogLevel.Warning, "Unknown Auth event was ignored")]
    private static partial void UnknownEvent(ILogger logger);
}
