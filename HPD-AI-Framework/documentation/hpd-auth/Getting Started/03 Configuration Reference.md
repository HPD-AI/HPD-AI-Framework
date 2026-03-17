# Configuration Reference

All HPD.Auth configuration lives in a single `HPDAuthOptions` object passed to `AddHPDAuth()`. There is no separate `IdentityOptions`, `ConfigureApplicationCookie`, or `PasswordHasherOptions` to configure — it is all here.

## Full example

```csharp
builder.Services.AddHPDAuth(options =>
{
    // ── Required ──────────────────────────────────────────────
    options.AppName = "MyApp";

    // ── JWT ───────────────────────────────────────────────────
    options.Jwt.Secret = "your-secret-key-minimum-32-characters";
    options.Jwt.ExpiryMinutes = 60;
    options.Jwt.Issuer = "https://myapp.com";
    options.Jwt.Audience = "myapp-users";

    // ── Password policy ───────────────────────────────────────
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredUniqueChars = 1;

    // ── Lockout ───────────────────────────────────────────────
    options.Lockout.Enabled = true;
    options.Lockout.MaxFailedAttempts = 5;
    options.Lockout.Duration = TimeSpan.FromMinutes(15);

    // ── Feature flags ─────────────────────────────────────────
    options.Features.RequireEmailConfirmation = true;
    options.Features.EnableAuditLog = true;
    options.Features.EnablePasskeys = true;

    // ── OAuth providers ───────────────────────────────────────
    options.OAuth.Google.ClientId = "...";
    options.OAuth.Google.ClientSecret = "...";
    options.OAuth.GitHub.ClientId = "...";
    options.OAuth.GitHub.ClientSecret = "...";
    options.OAuth.Microsoft.ClientId = "...";
    options.OAuth.Microsoft.ClientSecret = "...";
});
```

## Options reference

### Root

| Option | Type | Default | Description |
|---|---|---|---|
| `AppName` | `string` | — | **Required.** Scopes the JWT key ring and SQLite database name. |

### `Jwt`

| Option | Type | Default | Description |
|---|---|---|---|
| `Secret` | `string` | — | HMAC-SHA256 signing key. Minimum 32 characters. Required for JWT auth. |
| `ExpiryMinutes` | `int` | `60` | Access token lifetime in minutes. |
| `Issuer` | `string?` | `null` | JWT `iss` claim. |
| `Audience` | `string?` | `null` | JWT `aud` claim. |

### `Password`

| Option | Type | Default | Description |
|---|---|---|---|
| `RequiredLength` | `int` | `8` | Minimum password length. |
| `RequireDigit` | `bool` | `true` | Require at least one digit. |
| `RequireLowercase` | `bool` | `true` | Require at least one lowercase letter. |
| `RequireUppercase` | `bool` | `true` | Require at least one uppercase letter. |
| `RequireNonAlphanumeric` | `bool` | `false` | Require at least one non-alphanumeric character. |
| `RequiredUniqueChars` | `int` | `1` | Minimum number of unique characters. |

### `Lockout`

| Option | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Enable account lockout after failed attempts. |
| `MaxFailedAttempts` | `int` | `5` | Failed attempts before lockout. |
| `Duration` | `TimeSpan` | `15 minutes` | How long the lockout lasts. |

### `Features`

| Option | Type | Default | Description |
|---|---|---|---|
| `RequireEmailConfirmation` | `bool` | `false` | Block login until email is confirmed. |
| `EnableAuditLog` | `bool` | `true` | Write auth events to the audit log. |
| `EnablePasskeys` | `bool` | `false` | Enable FIDO2/WebAuthn passkey endpoints. |
| `EnableMagicLink` | `bool` | `false` | Enable magic link login. |

### `OAuth`

Each provider has `ClientId` and `ClientSecret`.

| Provider | Property |
|---|---|
| Google | `options.OAuth.Google` |
| GitHub | `options.OAuth.GitHub` |
| Microsoft | `options.OAuth.Microsoft` |

### `RateLimit`

| Option | Type | Default | Description |
|---|---|---|---|
| `ResendEmailWindowSeconds` | `int` | `60` | Minimum seconds between resend confirmation requests. |

## Using appsettings.json

You can read values from configuration instead of hardcoding them:

```csharp
builder.Services.AddHPDAuth(options =>
{
    options.AppName = "MyApp";
    options.Jwt.Secret = builder.Configuration["HPDAuth:Jwt:Secret"]
        ?? throw new InvalidOperationException("JWT secret is required");
    options.Features.RequireEmailConfirmation =
        builder.Configuration.GetValue<bool>("HPDAuth:Features:RequireEmailConfirmation");
});
```

```json
{
  "HPDAuth": {
    "Jwt": {
      "Secret": "your-secret-from-env-or-vault"
    },
    "Features": {
      "RequireEmailConfirmation": true
    }
  }
}
```

::: warning
Never put `Jwt.Secret` or OAuth client secrets in source control. Use environment variables, .NET user secrets in development, or a secrets vault in production.
:::
