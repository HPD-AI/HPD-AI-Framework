# Installation

HPD.Auth requires **.NET 10** or later. The passkey APIs used internally require ASP.NET Identity schema v3, which shipped in .NET 10.

## Install packages

Start with the core package and authentication:

```bash
dotnet add package HPD.Auth
dotnet add package HPD.Auth.Authentication
```

Then add any optional packages you need:

```bash
# Two-factor auth (TOTP + passkeys)
dotnet add package HPD.Auth.TwoFactor

# Social login (Google, GitHub, Microsoft)
dotnet add package HPD.Auth.OAuth

# Admin endpoints
dotnet add package HPD.Auth.Admin

# Authorization policies + rate limiting
dotnet add package HPD.Auth.Authorization

# Audit logging + event publishing
dotnet add package HPD.Auth.Audit
```

## Register services

In `Program.cs`, chain the packages you installed onto `AddHPDAuth()`:

```csharp
builder.Services
    .AddHPDAuth(options =>
    {
        options.AppName = "MyApp";
        options.Jwt.Secret = "your-secret-key-at-least-32-chars";
    })
    .AddAuthentication()   // HPD.Auth.Authentication
    .AddAudit()            // HPD.Auth.Audit
    .AddTwoFactor()        // HPD.Auth.TwoFactor
    .AddAdmin()            // HPD.Auth.Admin
    .AddAuthorization();   // HPD.Auth.Authorization
```

Only chain the methods for packages you've installed.

## Add middleware and endpoints

```csharp
var app = builder.Build();

app.UseHPDAuth(); // Adds UseAuthentication() + UseAuthorization() in correct order

app.MapHPDAuthEndpoints();        // Core: /api/auth/*
app.MapHPDAdminEndpoints();       // Admin: /api/admin/*
app.MapHPDTwoFactorEndpoints();   // 2FA: /api/auth/factors/*, /api/auth/passkey/*
app.MapHPDOAuthEndpoints();       // OAuth: /auth/{provider}, /auth/{provider}/callback

app.Run();
```

## Memory cache

The password recovery endpoints use in-memory rate limiting. Add this before `builder.Build()`:

```csharp
builder.Services.AddMemoryCache();
```

## Verify it works

Run your app and call the signup endpoint:

```bash
curl -X POST http://localhost:5000/api/auth/signup \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Password123!"}'
```

A `200 OK` with a token response means everything is wired up correctly.

## Next steps

- [Quick start →](/Getting Started/02 Quick Start)
- [Configuration reference →](/Getting Started/03 Configuration Reference)
