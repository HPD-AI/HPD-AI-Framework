# Packages Overview

HPD.Auth is split into focused packages. Install only what you need.

## Package map

```
HPD.Auth                    ← core endpoints + DI wiring (always required)
├── HPD.Auth.Authentication ← JWT + Cookie + PolicyScheme
├── HPD.Auth.TwoFactor      ← TOTP + passkeys
├── HPD.Auth.OAuth          ← Google / GitHub / Microsoft
├── HPD.Auth.Admin          ← admin endpoints
├── HPD.Auth.Authorization  ← policies + rate limiting
└── HPD.Auth.Audit          ← event publishing + audit log
```

## Minimal setup

```bash
dotnet add package HPD.Auth
dotnet add package HPD.Auth.Authentication
```

```csharp
builder.Services
    .AddHPDAuth(options => { options.AppName = "MyApp"; })
    .AddAuthentication();

app.UseHPDAuth();
app.MapHPDAuthEndpoints();
```

## Full setup

```bash
dotnet add package HPD.Auth
dotnet add package HPD.Auth.Authentication
dotnet add package HPD.Auth.TwoFactor
dotnet add package HPD.Auth.OAuth
dotnet add package HPD.Auth.Admin
dotnet add package HPD.Auth.Authorization
dotnet add package HPD.Auth.Audit
```

```csharp
builder.Services
    .AddHPDAuth(options => { ... })
    .AddAuthentication()
    .AddTwoFactor()
    .AddOAuth()
    .AddAdmin()
    .AddAuthorization()
    .AddAudit();

app.UseHPDAuth();
app.MapHPDAuthEndpoints();
app.MapHPDTwoFactorEndpoints();
app.MapHPDOAuthEndpoints();
app.MapHPDAdminEndpoints();
```

## Package details

| Package | Purpose | Docs |
|---|---|---|
| `HPD.Auth` | Core endpoints, DI, DbContext | [→](/Packages/01 HPD.Auth) |
| `HPD.Auth.Authentication` | JWT, Cookie, dual-auth PolicyScheme | [→](/Packages/02 Authentication) |
| `HPD.Auth.TwoFactor` | TOTP setup/verify + FIDO2 passkeys | [→](/Packages/03 TwoFactor) |
| `HPD.Auth.OAuth` | Google, GitHub, Microsoft social login | [→](/Packages/04 OAuth) |
| `HPD.Auth.Admin` | Admin user management + audit log query | [→](/Packages/05 Admin) |
| `HPD.Auth.Authorization` | Built-in policies + rate limiting | [→](/Packages/06 Authorization) |
| `HPD.Auth.Audit` | Event publishing + audit log persistence | [→](/Packages/07 Audit) |
