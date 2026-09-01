<div align="center">

<img src="documentation/public/logo.svg" width="64" height="64" alt="HPD.Auth logo" />

# HPD.Auth

**A hosted-auth-service experience as an embedded .NET library.**

Configure once in `Program.cs`. Call the REST API from your frontend.
No separate service to run. No per-user pricing. No data leaving your infrastructure.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-14B8A6.svg)](../../LICENSE)

[Documentation](https://hpd-ai.github.io/HPD.Auth) · [Quick Start](#quick-start) · [Packages](#packages)

</div>

---

## What it is

HPD.Auth wraps ASP.NET Core Identity's trusted primitives — password hashing, security stamps, lockout, token providers — and exposes them as a ready-made REST API.

Add it to any ASP.NET Core app and immediately get:

- Signup, login, logout, password reset, email confirmation
- JWT + Cookie dual-auth with automatic routing
- Session management and multi-device revocation
- TOTP (authenticator app) two-factor auth
- Passkeys (FIDO2/WebAuthn)
- Google, GitHub, Microsoft social login
- Admin API: user management, ban/unban, roles, claims, audit log
- Event-driven audit logging

No scaffolding. Explicit storage. No middleware ordering to think about.

## Quick Start

```bash
dotnet add package HPD.Auth
dotnet add package HPD.Auth.Authentication
dotnet add package HPD.Auth.Base
dotnet add package HPD.Base.Sqlite
```

```csharp
// Program.cs
builder.Services.AddHPDBase(baseBuilder =>
{
    // Configure the host-owned provider, storage-protection authority, token
    // protection, and selection limits, then install the closed Auth graph.
    baseBuilder.UseStore(SqliteStore.Configure(options =>
    {
        options.StoreId = "auth";
        options.DataSource = builder.Configuration.GetConnectionString("auth-db")!;
    }));
    AuthBaseModule.Install(baseBuilder, authBaseOptions);
});

builder.Services.AddHPDAuth(options =>
    {
        options.AppName = "MyApp";
        options.Jwt.Secret = builder.Configuration["Auth:Jwt:Secret"];
    })
    .AddAuthentication()
    .AddAudit()
    .AddTwoFactor()
    .AddAdmin();

var app = builder.Build();

app.UseHPDAuth();
app.MapHPDAuthEndpoints();
app.MapHPDAdminEndpoints();
app.MapHPDTwoFactorEndpoints();

app.Run();
```

Your app now has a full auth API at `/api/auth/*` and `/api/admin/*`.

When JWT authentication is enabled, `Auth:Jwt:Secret` must be a strong random
secret of at least 32 UTF-8 bytes. Leave `Jwt.Secret` empty only for intentional
cookie-only hosts.

### Storage and Data Protection

HPD.Auth requires a finalized HPD Base application graph and an explicit certified
Base storage provider. The Auth-owned system collections store identity data,
sessions, refresh tokens, audit state, and the ASP.NET Core Data Protection key ring.

Use durable storage for any real host:

```csharp
builder.Services.AddHPDBase(baseBuilder =>
{
    baseBuilder.UseStore(SqliteStore.Configure(options =>
    {
        options.StoreId = "auth";
        options.DataSource = builder.Configuration.GetConnectionString("auth-db")!;
    }));
    AuthBaseModule.Install(baseBuilder, authBaseOptions);
});
```

All instances of the same app must share the same durable Base authority and exact
Auth graph. Schema plan/apply is an explicit deployment or control-plane operation;
Auth does not run migrations opportunistically during application startup. The host
must also provide the required storage-protection capability, refresh/recovery digest
key rings, and protected token-delivery authority.

```bash
# Sign up
curl -X POST http://localhost:5000/api/auth/signup \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@example.com","password":"Password123!"}'

# Login
curl -X POST http://localhost:5000/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"grant_type":"password","email":"alice@example.com","password":"Password123!"}'
```

## Packages

Install only what you need:

| Package | Purpose |
|---|---|
| `HPD.Auth` | Core endpoints: signup, login, logout, sessions, password reset |
| `HPD.Auth.Base` | Closed Auth collections, reads, operations, subjects, and lifecycle graph |
| `HPD.Auth.Authentication` | JWT + Cookie + dual-auth PolicyScheme |
| `HPD.Auth.TwoFactor` | TOTP (authenticator apps) + passkeys (FIDO2/WebAuthn) |
| `HPD.Auth.OAuth` | Google, GitHub, Microsoft social login |
| `HPD.Auth.Admin` | Admin endpoints: user management, ban, roles, claims, audit log |
| `HPD.Auth.Authorization` | Built-in policies, rate limiting, feature flags |
| `HPD.Auth.Audit` | Event publishing + audit log persistence |

## Why not just use ASP.NET Identity?

ASP.NET Core Identity gives you the primitives — `UserManager`, `SignInManager`, password hashing. What it doesn't give you is a usable HTTP API. You still have to build every endpoint, wire up session management, implement audit logging, and figure out JWT configuration.

HPD.Auth is that layer. It wraps Identity's trusted primitives so you don't have to.

## Why not Auth0 / Clerk?

| | HPD.Auth | Auth0 / Clerk |
|---|---|---|
| Runs in your process | ✓ | ✗ |
| Per-user pricing | ✗ | ✓ |
| Data leaves your infra | ✗ | ✓ |
| Fully customizable | ✓ | Limited |
| Works offline | ✓ | ✗ |

## API endpoints

```
POST   /api/auth/signup
POST   /api/auth/token          (grant_type=password|refresh_token)
POST   /api/auth/logout         (scope=local|global|others)
GET    /api/auth/user
PUT    /api/auth/user
POST   /api/auth/recover
POST   /api/auth/verify         (type=recovery|signup|email_change)
POST   /api/auth/resend
GET    /api/auth/sessions
DELETE /api/auth/sessions/{id}
DELETE /api/auth/sessions
POST   /api/auth/factors        (TOTP setup)
POST   /api/auth/factors/{id}/challenge
POST   /api/auth/factors/{id}/verify
DELETE /api/auth/factors/{id}
POST   /api/auth/2fa/verify
POST   /api/auth/passkey/*      (register + authenticate)
GET    /auth/{provider}         (OAuth challenge)
GET    /auth/{provider}/callback
GET    /api/admin/users         (+ many admin sub-routes)
GET    /api/admin/audit-logs
POST   /api/admin/generate-link
```

## Requirements

- .NET 10 or later
- ASP.NET Core 10

## Documentation

Full documentation at **[hpd-ai.github.io/HPD.Auth](https://hpd-ai.github.io/HPD.Auth)**

- [Getting Started](https://hpd-ai.github.io/HPD.Auth/Getting%20Started/00%20Introduction)
- [Configuration Reference](https://hpd-ai.github.io/HPD.Auth/Getting%20Started/03%20Configuration%20Reference)
- [API Reference](https://hpd-ai.github.io/HPD.Auth/API%20Reference/00%20Overview)
- [Guides](https://hpd-ai.github.io/HPD.Auth/Guides/01%20Send%20Emails)

## Repository structure

```
src/
├── HPD.Auth.Core/           Entities, interfaces, options, events
├── HPD.Auth.Base/           Closed HPD Base schema and operation authority
├── HPD.Auth.Infrastructure/ Base-backed Identity, token, session, audit, and Data Protection adapters
├── HPD.Auth/                Core endpoints + DI registration
├── HPD.Auth.Authentication/ JWT + Cookie + PolicyScheme
├── HPD.Auth.Admin/          Admin endpoints (10 groups, 25+ endpoints)
├── HPD.Auth.Audit/          Event publishing + audit log
├── HPD.Auth.TwoFactor/      TOTP + passkeys (Identity 10 APIs)
├── HPD.Auth.OAuth/          Google/GitHub/Microsoft
└── HPD.Auth.Authorization/  5 requirements, 5 handlers, 10 built-in policies
samples/
└── HPD.Auth.Sample/         Canary project — verifies full DI chain
tests/                       ~1100+ tests
documentation/               VitePress docs site
```

## License

FSL-1.1-ALv2 with Apache-2.0 future license © HPD
