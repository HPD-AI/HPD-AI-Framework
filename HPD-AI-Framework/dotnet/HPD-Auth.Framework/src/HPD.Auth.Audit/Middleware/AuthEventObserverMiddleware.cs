using HPD.Auth.Audit.Services;
using HPD.Auth.Core.Events;
using HPD.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Audit.Middleware;

/// <summary>
/// ASP.NET Core middleware that wires the <see cref="AuditingAuthObserver"/> onto the
/// per-request <see cref="IEventCoordinator"/> and observes emitted auth events while the
/// endpoint handler runs.
///
/// Flow:
///   1. Resolve the scoped <see cref="IEventCoordinator"/> for this request.
///   2. Register an auth-event subscription.
///   3. Call next (endpoint runs, emitting auth events onto the coordinator), passing each
///      <see cref="AuthEvent"/> to <see cref="AuditingAuthObserver"/>.
///
/// Registration: call <c>app.UseAuthEventObserver()</c> after <c>UseRouting</c>
/// and before <c>MapControllers</c> / endpoint mapping.
/// </summary>
public sealed class AuthEventObserverMiddleware
{
    private readonly RequestDelegate _next;

    public AuthEventObserverMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var coordinator = context.RequestServices.GetService<IEventCoordinator>();
        var scopeFactory = context.RequestServices.GetService<IServiceScopeFactory>();

        if (coordinator is null || scopeFactory is null)
        {
            await _next(context);
            return;
        }

        using var eventSubscription = coordinator.Subscribe<AuthEvent>(
            async authEvent =>
            {
                using var scope = scopeFactory.CreateScope();
                var observer = scope.ServiceProvider.GetRequiredService<AuditingAuthObserver>();
                await observer.HandleAsync(authEvent, context.RequestAborted);
            });

        await _next(context);
    }
}
