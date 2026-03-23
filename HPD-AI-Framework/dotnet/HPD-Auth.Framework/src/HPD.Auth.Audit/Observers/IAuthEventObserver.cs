using HPD.Auth.Core.Events;
using HPD.Events;

namespace HPD.Auth.Audit.Observers;

/// <summary>
/// Typed observer for a specific auth domain event.
///
/// Extends <see cref="IEventObserver{TEvent}"/> from HPD-Events — the shared
/// event infrastructure used across the HPD solution.
///
/// Implementations are registered in DI via
/// <see cref="Extensions.HPDAuthAuditBuilderExtensions.AddAuthObserver{TEvent,TObserver}"/>
/// and are automatically resolved and invoked by <see cref="Services.AuditingAuthObserver"/>
/// when a matching event is emitted on the request coordinator.
///
/// Contract:
/// - <see cref="IEventObserver{TEvent}.OnEventAsync"/> is fire-and-forget — exceptions are
///   caught by <see cref="Services.AuditingAuthObserver"/> and logged at Error level.
/// - Implementations should be idempotent where possible.
/// - Long-running work (email, external API calls) should be dispatched to a
///   background queue rather than awaited inline.
/// </summary>
/// <typeparam name="TEvent">The concrete auth event type this observer processes.</typeparam>
public interface IAuthEventObserver<TEvent> : IEventObserver<TEvent>
    where TEvent : AuthEvent;
