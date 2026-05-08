using HPD.Auth.Core.Events;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.Audit.Observers;

/// <summary>
/// Convenience base class for <see cref="IAuthEventObserver{TEvent}"/> implementations.
///
/// Provides:
/// - A typed <see cref="Logger"/> for structured logging within the observer.
/// - A sealed <see cref="HandleAsync"/> entry point that wraps
///   <see cref="ExecuteAsync"/> in a try/catch, fulfilling the observer contract
///   that exceptions must not propagate.
///
/// Usage — inherit and override <see cref="ExecuteAsync"/>:
/// <code>
/// public class WelcomeEmailObserver : AuthEventObserverBase&lt;UserRegisteredEvent&gt;
/// {
///     private readonly IEmailSender _email;
///
///     public WelcomeEmailObserver(IEmailSender email, ILogger&lt;WelcomeEmailObserver&gt; logger)
///         : base(logger) { _email = email; }
///
///     protected override async Task ExecuteAsync(UserRegisteredEvent e, CancellationToken ct)
///         => await _email.SendWelcomeAsync(e.Email, ct);
/// }
/// </code>
/// </summary>
/// <typeparam name="TEvent">The concrete auth event type this observer processes.</typeparam>
public abstract class AuthEventObserverBase<TEvent> : IAuthEventObserver<TEvent>
    where TEvent : AuthEvent
{
    protected ILogger Logger { get; }

    protected AuthEventObserverBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Entry point called by <see cref="Services.AuditingAuthObserver"/>.
    /// Wraps <see cref="ExecuteAsync"/> in try/catch.
    /// </summary>
    public async ValueTask HandleAsync(TEvent evt, CancellationToken ct = default)
    {
        try
        {
            await ExecuteAsync(evt, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Observer {ObserverType} was cancelled while processing {EventType}",
                GetType().Name, typeof(TEvent).Name);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Observer {ObserverType} threw an unhandled exception processing {EventType}",
                GetType().Name, typeof(TEvent).Name);
        }
    }

    /// <summary>
    /// Override this method to implement observer logic.
    /// </summary>
    protected abstract Task ExecuteAsync(TEvent evt, CancellationToken ct);
}
