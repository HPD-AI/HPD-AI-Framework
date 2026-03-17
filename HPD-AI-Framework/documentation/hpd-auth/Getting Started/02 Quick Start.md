# Quick Start

This walks you through building a working app with signup, login, and a protected endpoint in under 5 minutes.

## 1. Create the project

```bash
dotnet new webapi -n MyApp
cd MyApp
dotnet add package HPD.Auth
dotnet add package HPD.Auth.Authentication
dotnet add package HPD.Auth.Audit
```

## 2. Configure Program.cs

Replace the contents of `Program.cs`:

```csharp
using HPD.Auth.Admin.Extensions;
using HPD.Auth.Audit.Extensions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHPDAuth(options =>
    {
        options.AppName = "MyApp";

        // JWT secret — use a real secret in production, not hardcoded
        options.Jwt.Secret = "super-secret-key-minimum-32-characters";
        options.Jwt.ExpiryMinutes = 60;

        // Disable email confirmation for this quick start
        options.Features.RequireEmailConfirmation = false;
    })
    .AddAuthentication()
    .AddAudit();

builder.Services.AddMemoryCache();

var app = builder.Build();

app.UseHPDAuth();
app.MapHPDAuthEndpoints();

// A protected endpoint to test auth
app.MapGet("/me", (HttpContext ctx) => ctx.User.Identity?.Name)
   .RequireAuthorization();

app.Run();
```

## 3. Sign up

```bash
curl -X POST http://localhost:5000/api/auth/signup \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@example.com",
    "password": "Password123!"
  }'
```

Response:

```json
{
  "access_token": "eyJhbGci...",
  "refresh_token": "abc123...",
  "expires_at": 1735689600,
  "token_type": "bearer",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "alice@example.com",
    "email_confirmed": false,
    "created_at": "2026-03-04T10:00:00Z"
  }
}
```

## 4. Call a protected endpoint

Use the `access_token` from signup:

```bash
curl http://localhost:5000/me \
  -H "Authorization: Bearer eyJhbGci..."
```

Response: `alice@example.com`

## 5. Log in

```bash
curl -X POST http://localhost:5000/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "grant_type": "password",
    "email": "alice@example.com",
    "password": "Password123!"
  }'
```

## 6. Refresh a token

```bash
curl -X POST http://localhost:5000/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "grant_type": "refresh_token",
    "refresh_token": "abc123..."
  }'
```

## What you have now

With those ~20 lines of `Program.cs` you have:

- `POST /api/auth/signup` — register
- `POST /api/auth/token` — login / refresh
- `POST /api/auth/logout` — logout (local, global, or all other sessions)
- `GET /api/auth/user` — get current user
- `PUT /api/auth/user` — update profile
- `POST /api/auth/recover` — request password reset
- `POST /api/auth/verify` — confirm email / complete password reset
- `POST /api/auth/resend` — resend confirmation email
- `GET/DELETE /api/auth/sessions` — list and revoke sessions

## Next steps

- [Full configuration reference →](/Getting Started/03 Configuration Reference)
- [Add 2FA →](/Packages/03 TwoFactor)
- [Add social login →](/Packages/04 OAuth)
- [Add an admin API →](/Packages/05 Admin)
- [Send real emails →](/Guides/01 Send Emails)
