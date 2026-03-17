using HPD.Auth.Audit.Observers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.Audit.Services;

/// <summary>
/// <see cref="IEventObserver{TEvent}"/> that writes an <see cref="AuditLogEntry"/>
/// for each auth event and fans out to all registered <see cref="IAuthEventObserver{TEvent}"/>
/// instances from the DI container.
///
/// Registered as a scoped service and attached to the request-scoped
/// <see cref="IEventCoordinator"/> by <see cref="HPD.Auth.Audit.Middleware.AuthEventObserverMiddleware"/>.
///
/// Resilience: audit write failures and observer exceptions are caught individually
/// and logged — they never break the primary auth flow.
/// </summary>
public sealed class AuditingAuthObserver : IEventObserver<AuthEvent>
{
    private readonly IAuditLogger _auditLogger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditingAuthObserver> _logger;

    public AuditingAuthObserver(
        IAuditLogger auditLogger,
        IServiceProvider serviceProvider,
        ILogger<AuditingAuthObserver> logger)
    {
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool ShouldProcess(AuthEvent evt) => true;

    public async Task OnEventAsync(AuthEvent evt, CancellationToken ct = default)
    {
        // ── Step 1: Write audit log entry ─────────────────────────────────────
        var entry = MapToAuditEntry(evt);
        if (entry is not null)
        {
            // Enrich IpAddress / UserAgent from AuthContext if not already set.
            entry = entry with
            {
                IpAddress = entry.IpAddress ?? evt.AuthContext?.IpAddress,
                UserAgent = entry.UserAgent ?? evt.AuthContext?.UserAgent,
            };

            try
            {
                await _auditLogger.LogAsync(entry, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AuditingAuthObserver failed to write audit log for {EventType} (EventId={EventId})",
                    evt.GetType().Name, evt.Timestamp);
            }
        }

        // ── Step 2: Fan out to registered observers ───────────────────────────
        await InvokeObserversAsync(evt, ct);
    }

    private async Task InvokeObserversAsync(AuthEvent evt, CancellationToken ct)
    {
        // Resolve and invoke typed IAuthEventObserver<TEvent> by reflecting the concrete type.
        // Each observer type is registered in DI; we resolve IEnumerable<IAuthEventObserver<TEvent>>.
        var observerType = typeof(IAuthEventObserver<>).MakeGenericType(evt.GetType());
        var baseObserverType = typeof(IEventObserver<>).MakeGenericType(evt.GetType());
        var observers = _serviceProvider.GetServices(observerType);

        foreach (var observer in observers)
        {
            if (observer is null) continue;

            try
            {
                var shouldProcess = (bool)baseObserverType.GetMethod(nameof(IEventObserver<AuthEvent>.ShouldProcess))!
                    .Invoke(observer, [evt])!;

                if (!shouldProcess) continue;

                var task = (Task)baseObserverType.GetMethod(nameof(IEventObserver<AuthEvent>.OnEventAsync))!
                    .Invoke(observer, [evt, ct])!;

                await task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Observer {ObserverType} failed for {EventType}",
                    observer.GetType().Name, evt.GetType().Name);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event → AuditLogEntry mapping
    // ─────────────────────────────────────────────────────────────────────────
    private static AuditLogEntry? MapToAuditEntry(AuthEvent evt) => evt switch
    {
        UserLoggedInEvent e => new AuditLogEntry(
            Action: AuditActions.UserLogin,
            Category: AuditCategories.Authentication,
            UserId: e.UserId,
            Metadata: new { e.AuthMethod }),

        UserLoggedOutEvent e => new AuditLogEntry(
            Action: AuditActions.UserLogout,
            Category: AuditCategories.Authentication,
            UserId: e.UserId,
            Metadata: new { e.SessionId }),

        UserRegisteredEvent e => new AuditLogEntry(
            Action: AuditActions.UserRegister,
            Category: AuditCategories.Authentication,
            UserId: e.UserId,
            Metadata: new { e.RegistrationMethod }),

        LoginFailedEvent e => new AuditLogEntry(
            Action: AuditActions.UserLoginFailed,
            Category: AuditCategories.Authentication,
            Success: false,
            ErrorMessage: e.Reason,
            Metadata: new { e.Email }),

        PasswordChangedEvent e => new AuditLogEntry(
            Action: AuditActions.PasswordChange,
            Category: AuditCategories.Authentication,
            UserId: e.UserId),

        PasswordResetRequestedEvent e => new AuditLogEntry(
            Action: AuditActions.PasswordResetRequest,
            Category: AuditCategories.Authentication,
            UserId: e.UserId,
            Metadata: new { e.Email }),

        EmailConfirmedEvent e => new AuditLogEntry(
            Action: AuditActions.EmailConfirm,
            Category: AuditCategories.Authentication,
            UserId: e.UserId,
            Metadata: new { e.Email }),

        TwoFactorEnabledEvent e => new AuditLogEntry(
            Action: AuditActions.TwoFactorEnable,
            Category: AuditCategories.Authentication,
            UserId: e.UserId,
            Metadata: new { e.Method }),

        SessionRevokedEvent e => new AuditLogEntry(
            Action: AuditActions.SessionRevoke,
            Category: AuditCategories.Authentication,
            UserId: e.UserId,
            Metadata: new { e.SessionId, e.RevokedBy }),

        _ => null,
    };
}
