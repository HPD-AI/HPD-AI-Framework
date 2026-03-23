using HPD.Auth.Audit.Observers;
using HPD.Auth.Audit.Services;
using HPD.Auth.Builder;
using HPD.Auth.Core.Events;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Audit.Extensions;

/// <summary>
/// Extension methods on <see cref="IHPDAuthBuilder"/> for registering
/// HPD.Auth.Audit services into the DI container.
///
/// Usage in Program.cs / Startup.cs:
/// <code>
/// services.AddHPDAuth(options =>
/// {
///     options.Features.EnableAuditLog = true;
/// })
/// .AddAudit()
/// .AddAuthObserver&lt;UserLoggedInEvent, LoginAlertObserver&gt;();
/// </code>
///
/// <see cref="AddAudit"/> registers:
/// - A scoped <see cref="IEventCoordinator"/> (one per request) used by auth endpoints
///   to emit auth events.
/// - <see cref="AuditingAuthObserver"/> as a scoped <see cref="IEventObserver{TEvent}"/>
///   (when <c>EnableAuditLog</c> is true). The observer is attached to the coordinator
///   during the request via the <see cref="HPD.Auth.Audit.Middleware.AuthEventObserverMiddleware"/>.
/// - When <c>EnableAuditLog</c> is false, no observer is registered and events are
///   silently dropped after emission.
/// </summary>
public static class HPDAuthAuditBuilderExtensions
{
    /// <summary>
    /// Registers the HPD.Auth.Audit services:
    /// - A scoped <see cref="IEventCoordinator"/> for the request lifetime.
    /// - <see cref="AuditingAuthObserver"/> (when <c>EnableAuditLog</c> is true).
    /// </summary>
    public static IHPDAuthBuilder AddAudit(this IHPDAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        // One EventCoordinator per request — same pattern as Agent/Graph (per-instance).
        services.AddScoped<IEventCoordinator>(_ => new EventCoordinator());

        if (builder.Options.Features.EnableAuditLog)
        {
            // AuditingAuthObserver: writes audit log + fans out to registered IAuthEventObserver<T>.
            services.AddScoped<AuditingAuthObserver>();
        }

        return builder;
    }

    /// <summary>
    /// Registers a typed <see cref="IAuthEventObserver{TEvent}"/> implementation
    /// for the specified auth event type.
    ///
    /// Multiple observers can be registered for the same event type — all will be
    /// invoked when that event is emitted on the request coordinator.
    ///
    /// Example:
    /// <code>
    /// builder.AddAuthObserver&lt;UserLoggedInEvent, LoginAlertObserver&gt;();
    /// builder.AddAuthObserver&lt;UserLoggedInEvent, AnalyticsObserver&gt;();
    /// </code>
    /// </summary>
    public static IHPDAuthBuilder AddAuthObserver<TEvent, TObserver>(
        this IHPDAuthBuilder builder)
        where TEvent : AuthEvent
        where TObserver : class, IAuthEventObserver<TEvent>
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Use AddScoped (not TryAddScoped) so multiple observers for the same event
        // are all registered and resolved via GetServices<T>().
        builder.Services.AddScoped<IAuthEventObserver<TEvent>, TObserver>();

        return builder;
    }
}
