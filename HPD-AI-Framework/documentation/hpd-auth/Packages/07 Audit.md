# HPD.Auth.Audit

Adds event publishing and audit log persistence. Every auth action publishes a typed event that is written to the immutable audit log and fanned out to your registered event handlers.

## Installation

```bash
dotnet add package HPD.Auth.Audit
```

```csharp
builder.Services
    .AddHPDAuth(options =>
    {
        options.Features.EnableAuditLog = true;
    })
    .AddAuthentication()
    .AddAudit();   // ← this package
```

## What it registers

- `IAuthEventPublisher` → `AuditingEventPublisher`
- An EF Core `SaveChangesInterceptor` that automatically enriches audit log entries with the current HTTP context (IP address, user agent)

## Without this package

If you don't install `HPD.Auth.Audit`, HPD.Auth registers a `NullEventPublisher` that discards all events. No errors, no warnings — events are silently dropped.

## Audit log

The audit log is an append-only table. Rows are never updated or deleted. Every auth event is written as a row with:

- User ID and email
- Action and category
- IP address and user agent
- Timestamp
- Tenant ID
- Optional metadata (event-specific details)

Query it via `GET /api/admin/audit-logs` (requires `HPD.Auth.Admin`).

## Event handlers

Register `IAuthEventHandler<TEvent>` implementations to react to events:

```csharp
builder.Services.AddScoped<IAuthEventHandler<UserRegisteredEvent>, WelcomeEmailSender>();
```

See [React to Auth Events →](/Guides/03 Auth Events) for a full guide.

## HTTP context enrichment

The `AuditInterceptor` (an EF Core `SaveChangesInterceptor`) automatically populates:

- `IpAddress` — from `HttpContext.Connection.RemoteIpAddress`
- `UserAgent` — from the `User-Agent` header

This happens transparently on every `SaveChangesAsync()` call involving an `AuditLog` entity. You don't need to pass HTTP context to event publishers manually.
