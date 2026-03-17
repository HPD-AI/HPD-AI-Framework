# React to Auth Events

HPD.Auth publishes a typed event for every significant auth action. You can subscribe to any event to add custom logic — send a Slack notification on login, sync user data to an external system on signup, or trigger a webhook on password change.

## Implement IAuthEventHandler\<T\>

Create a class that implements `IAuthEventHandler<TEvent>` for the event type you want to handle:

```csharp
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;

public class SlackLoginNotifier : IAuthEventHandler<UserLoggedInEvent>
{
    private readonly ISlackClient _slack;

    public SlackLoginNotifier(ISlackClient slack) => _slack = slack;

    public async Task HandleAsync(UserLoggedInEvent evt, CancellationToken ct)
    {
        await _slack.PostAsync(
            channel: "#auth-log",
            message: $":key: {evt.Email} logged in from {evt.IpAddress}",
            ct);
    }
}
```

## Register it in DI

```csharp
builder.Services.AddScoped<IAuthEventHandler<UserLoggedInEvent>, SlackLoginNotifier>();
```

## Multiple handlers for the same event

You can register as many handlers as you need for the same event type:

```csharp
builder.Services.AddScoped<IAuthEventHandler<UserRegisteredEvent>, WelcomeEmailSender>();
builder.Services.AddScoped<IAuthEventHandler<UserRegisteredEvent>, CRMSync>();
builder.Services.AddScoped<IAuthEventHandler<UserRegisteredEvent>, AnalyticsTracker>();
```

All handlers are called for every event of that type.

## Available event types

| Event class | Fired when |
|---|---|
| `UserRegisteredEvent` | A new user signs up |
| `UserLoggedInEvent` | A user logs in successfully |
| `UserLoggedOutEvent` | A user logs out |
| `LoginFailedEvent` | A login attempt fails |
| `PasswordChangedEvent` | A user changes their password |
| `PasswordResetRequestedEvent` | A password reset is requested |
| `EmailConfirmedEvent` | A user confirms their email |
| `TwoFactorEnabledEvent` | A user enables 2FA |
| `SessionRevokedEvent` | A session is revoked |

## Event properties

All events extend `AuthEventBase`:

```csharp
public abstract class AuthEventBase
{
    public string? UserId { get; init; }
    public string? Email { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public DateTime Timestamp { get; init; }
    public Guid InstanceId { get; init; }
}
```

Individual events may add extra properties. For example, `LoginFailedEvent` includes a `Reason` string.

## Handling errors in event handlers

If your handler throws, HPD.Auth logs the exception and continues — it does not bubble the error back to the user. This is intentional: a Slack notification failing should not cause a login to fail.

If you need guaranteed delivery, implement retry logic inside your handler, or use a background queue:

```csharp
public class ReliableWebhookHandler : IAuthEventHandler<UserRegisteredEvent>
{
    private readonly IBackgroundJobClient _jobs;

    public ReliableWebhookHandler(IBackgroundJobClient jobs) => _jobs = jobs;

    public Task HandleAsync(UserRegisteredEvent evt, CancellationToken ct)
    {
        // Enqueue a background job rather than calling the webhook inline
        _jobs.Enqueue(() => SendWebhookAsync(evt.UserId, evt.Email));
        return Task.CompletedTask;
    }
}
```
