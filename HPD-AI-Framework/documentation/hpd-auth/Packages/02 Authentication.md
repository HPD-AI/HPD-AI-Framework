# HPD.Auth.Authentication

Configures JWT Bearer authentication, Cookie authentication, and the PolicyScheme that routes between them.

## Installation

```bash
dotnet add package HPD.Auth.Authentication
```

```csharp
builder.Services
    .AddHPDAuth(options =>
    {
        options.Jwt.Secret = "your-secret-key-minimum-32-chars";
        options.Jwt.ExpiryMinutes = 60;
    })
    .AddAuthentication();   // ← this package
```

## What it configures

### JWT Bearer

- Validates tokens signed with `options.Jwt.Secret` using HS256
- Validates `Issuer` and `Audience` if configured
- Validates the security stamp claim on every request (enables immediate revocation via stamp rotation)
- Sets `ClockSkew` to 0 — tokens expire exactly when `exp` says they do

### Cookie authentication

- Configured with the settings from `HPDAuthOptions`
- Sliding expiration enabled by default
- `HttpOnly` cookies

### PolicyScheme (dual-auth routing)

A `PolicyScheme` named `"HPDAuth"` inspects every incoming request and forwards it to the right handler:

```
Authorization: Bearer <token>  →  JWT handler
(anything else)                →  Cookie handler
```

This means you never need to specify an authentication scheme per-endpoint. The routing happens automatically on every request.

## JWT configuration options

| Option | Description |
|---|---|
| `Jwt.Secret` | HMAC-SHA256 signing key. Required. Minimum 32 characters. |
| `Jwt.ExpiryMinutes` | Access token lifetime. Default: `60`. |
| `Jwt.Issuer` | JWT `iss` claim. Optional. |
| `Jwt.Audience` | JWT `aud` claim. Optional. |

## Token service

`HPD.Auth.Authentication` also registers `ITokenService` — the service that creates and rotates tokens. You don't call it directly; the auth endpoints use it internally.
