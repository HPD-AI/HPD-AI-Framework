using HPD.Auth.Core.Events;
namespace HPD.Auth.Audit.Observers;

/// <summary>
/// Typed subscription handler for a specific auth domain event.
///
/// Implementations are registered in DI via
/// <see cref="Extensions.HPDAuthAuditBuilderExtensions.AddAuthObserver{TEvent,TObserver}"/>
/// and are automatically resolved and invoked by <see cref="Services.AuditingAuthObserver"/>
/// when a matching event is emitted on the request coordinator.
///
/// </summary>
/// <typeparam name="TEvent">The concrete auth event type this observer processes.</typeparam>
public interface IAuthEventObserver<in TEvent>
    where TEvent : AuthEvent
{
    ValueTask HandleAsync(TEvent evt, CancellationToken ct = default);
}
