# HPD.Auth.TwoFactor

Adds TOTP (authenticator apps) and passkey (FIDO2/WebAuthn) support.

## Installation

```bash
dotnet add package HPD.Auth.TwoFactor
```

```csharp
builder.Services
    .AddHPDAuth(options => { ... })
    .AddAuthentication()
    .AddTwoFactor();   // ← add this

app.UseHPDAuth();
app.MapHPDAuthEndpoints();
app.MapHPDTwoFactorEndpoints();   // ← add this
```

## TOTP

No additional configuration required. Once `MapHPDTwoFactorEndpoints()` is called, the TOTP endpoints are live.

See [2FA & TOTP endpoints →](/API Reference/03 TwoFactor)

## Passkeys (FIDO2/WebAuthn)

Passkeys require one extra step: registering an `IPasskeyHandler<ApplicationUser>` in DI. This is an ASP.NET Identity 10 requirement.

### Step 1: Enable passkeys in options

```csharp
builder.Services.AddHPDAuth(options =>
{
    options.Features.EnablePasskeys = true;
});
```

### Step 2: Register a passkey handler

ASP.NET Identity 10 requires you to register an `IPasskeyHandler<TUser>`. The handler is responsible for:
- Storing and retrieving passkey credentials
- Validating the relying party ID (your domain)

A minimal implementation:

```csharp
using Microsoft.AspNetCore.Identity;

public class MyPasskeyHandler : IPasskeyHandler<ApplicationUser>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _http;

    public MyPasskeyHandler(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor http)
    {
        _userManager = userManager;
        _http = http;
    }

    public string GetRelyingPartyId()
    {
        // Return your app's domain (no scheme, no port)
        return _http.HttpContext?.Request.Host.Host ?? "localhost";
    }

    public string GetRelyingPartyName() => "My App";
}
```

Register it:

```csharp
builder.Services.AddScoped<IPasskeyHandler<ApplicationUser>, MyPasskeyHandler>();
```

### Step 3: Use the passkey endpoints

See [Passkey endpoints →](/API Reference/04 Passkeys)

## Endpoints registered by MapHPDTwoFactorEndpoints()

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/factors` | Begin TOTP setup |
| `POST` | `/api/auth/factors/{id}/challenge` | Initiate challenge |
| `POST` | `/api/auth/factors/{id}/verify` | Verify code + enable 2FA |
| `DELETE` | `/api/auth/factors/{id}` | Remove factor |
| `POST` | `/api/auth/2fa/verify` | Complete 2FA login |
| `POST` | `/api/auth/passkey/register/options` | Get registration options |
| `POST` | `/api/auth/passkey/register/complete` | Complete registration |
| `POST` | `/api/auth/passkey/authenticate/options` | Get authentication options |
| `POST` | `/api/auth/passkey/authenticate/complete` | Complete authentication |
| `GET` | `/api/auth/passkeys` | List passkeys |
| `PATCH` | `/api/auth/passkeys/{id}` | Rename passkey |
| `DELETE` | `/api/auth/passkeys/{id}` | Remove passkey |
