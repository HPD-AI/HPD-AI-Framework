# HPD.Auth

The core package. Always required. Provides DI registration, the `HPDAuthDbContext`, and the core auth endpoints.

## What it registers

When you call `AddHPDAuth()`, it:

1. Builds and registers `HPDAuthOptions`
2. Registers `ITenantContext` → `SingleTenantContext` (single-tenant default)
3. Registers `HPDAuthDbContext` with SQLite in-memory
4. Registers ASP.NET Core Identity (`UserManager`, `SignInManager`, etc.)
5. Registers ASP.NET Data Protection (keys persisted to the database)
6. Registers `IAuditLogger`, `ISessionManager`, `IRefreshTokenStore`
7. Registers no-op email and SMS senders (replaced by your implementations)

## Database

By default, HPD.Auth uses SQLite in-memory with a shared cache. The schema is created automatically on startup — no migrations needed.

The connection string is derived from `AppName`:
```
DataSource=file:{AppName}?mode=memory&cache=shared
```

This means all requests within the same process share the same database, and the data is lost when the process restarts. This is appropriate for development, testing, and scenarios where you don't need persistence.

::: info Persistent storage
A PostgreSQL provider package is planned. Until then, if you need persistence, you can inject your own `HPDAuthDbContext` configuration before calling `AddHPDAuth()` — but this is not officially supported.
:::

## Endpoints registered by MapHPDAuthEndpoints()

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/signup` | Register a new user |
| `POST` | `/api/auth/token` | Login / refresh token |
| `POST` | `/api/auth/logout` | Logout |
| `GET` | `/api/auth/user` | Get current user |
| `PUT` | `/api/auth/user` | Update current user |
| `POST` | `/api/auth/recover` | Request password reset |
| `POST` | `/api/auth/verify` | Verify email / complete reset |
| `POST` | `/api/auth/resend` | Resend confirmation email |
| `GET` | `/api/auth/sessions` | List active sessions |
| `DELETE` | `/api/auth/sessions/{id}` | Revoke a session |
| `DELETE` | `/api/auth/sessions` | Revoke all sessions |

## Installation

```bash
dotnet add package HPD.Auth
```

```csharp
builder.Services.AddHPDAuth(options =>
{
    options.AppName = "MyApp";
});

app.UseHPDAuth();
app.MapHPDAuthEndpoints();
```
