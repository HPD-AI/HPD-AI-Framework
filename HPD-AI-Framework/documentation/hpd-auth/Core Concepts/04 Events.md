# Events & Audit Log

HPD.Auth publishes a typed event for every significant auth action. Events flow to the audit log automatically, and you can subscribe to any event type to add your own logic.

## How it works

```
Auth action (signup, login, etc.)
    ↓
IAuthEventPublisher.PublishAsync(event)
    ↓
AuditingEventPublisher
    ├── Writes to AuditLog (always)
    └── Fans out to all IAuthEventHandler<TEvent> implementations
```

The `AuditingEventPublisher` is registered automatically when you call `.AddAudit()`.

## Built-in event types

| Event | Fired when |
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

All events extend `AuthEventBase` and include:

| Property | Type | Description |
|---|---|---|
| `UserId` | `string?` | User ID (null for failed logins before user is identified) |
| `Email` | `string?` | User email |
| `IpAddress` | `string?` | Client IP address |
| `UserAgent` | `string?` | Client user agent |
| `Timestamp` | `DateTime` | When the event occurred |
| `InstanceId` | `Guid` | Tenant ID |

## The audit log

Every event is written to the `AuditLog` table. The audit log is **write-only and immutable** — rows are never updated or deleted.

Query the audit log through the admin API:

```http
GET /api/admin/audit-logs?userId=3fa85f64&limit=50
Authorization: Bearer <admin-token>
```

## Subscribing to events

Implement `IAuthEventHandler<TEvent>` to react to any event:

```csharp
public class SlackLoginNotifier : IAuthEventHandler<UserLoggedInEvent>
{
    private readonly ISlackClient _slack;

    public SlackLoginNotifier(ISlackClient slack) => _slack = slack;

    public async Task HandleAsync(UserLoggedInEvent evt, CancellationToken ct)
    {
        await _slack.PostAsync(
            $"User {evt.Email} logged in from {evt.IpAddress}",
            ct);
    }
}
```

Register it in DI:

```csharp
builder.Services.AddScoped<IAuthEventHandler<UserLoggedInEvent>, SlackLoginNotifier>();
```

You can register multiple handlers for the same event type — all of them will be called.

## Disabling the audit log

If you don't need audit logging, skip `.AddAudit()` and the events will be published to a no-op publisher that discards them:

```csharp
builder.Services
    .AddHPDAuth(options => { ... })
    .AddAuthentication();
    // No .AddAudit() — events are discarded
```

You can still subscribe to events without the audit log by registering handlers and calling `.AddAudit()` with `options.Features.EnableAuditLog = false`.
