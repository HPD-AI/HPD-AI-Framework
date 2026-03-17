using HPD.Auth.Audit.Services;
using HPD.Auth.Core.Events;
using HPD.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Audit.Middleware;

/// <summary>
/// ASP.NET Core middleware that wires the <see cref="AuditingAuthObserver"/> onto the
/// per-request <see cref="IEventCoordinator"/> and drains emitted auth events after the
/// endpoint handler completes.
///
/// Flow:
///   1. Resolve the scoped <see cref="IEventCoordinator"/> for this request.
///   2. Call next (endpoint runs, emitting auth events onto the coordinator).
///   3. Drain all pending events from the coordinator, passing each
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
        await _next(context);

        // Drain events after the endpoint has run.
        var coordinator = context.RequestServices.GetService<IEventCoordinator>();
        var observer = context.RequestServices.GetService<AuditingAuthObserver>();

        if (coordinator is null || observer is null)
            return;

        // Use a short-lived CancellationToken — we are post-response, so use
        // the request aborted token as a best-effort guard.
        var ct = context.RequestAborted;

        while (coordinator.TryRead(out var evt))
        {
            if (evt is AuthEvent authEvent && observer.ShouldProcess(authEvent))
            {
                await observer.OnEventAsync(authEvent, ct);
            }
        }
    }
}
