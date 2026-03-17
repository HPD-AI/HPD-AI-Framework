# Password Policy & Lockout

HPD.Auth configures ASP.NET Core Identity's password and lockout settings through `HPDAuthOptions`. All defaults are reasonable for production — only change them if you have a specific reason.

## Password policy

Configure in `HPDAuthOptions.Password`:

```csharp
builder.Services.AddHPDAuth(options =>
{
    options.Password.RequiredLength = 12;         // Minimum 12 characters
    options.Password.RequireDigit = true;          // At least one digit
    options.Password.RequireLowercase = true;      // At least one lowercase
    options.Password.RequireUppercase = true;      // At least one uppercase
    options.Password.RequireNonAlphanumeric = true; // At least one symbol
    options.Password.RequiredUniqueChars = 4;      // At least 4 unique characters
});
```

### Defaults

| Setting | Default |
|---|---|
| `RequiredLength` | `8` |
| `RequireDigit` | `true` |
| `RequireLowercase` | `true` |
| `RequireUppercase` | `true` |
| `RequireNonAlphanumeric` | `false` |
| `RequiredUniqueChars` | `1` |

### Validation errors

Password validation errors are returned as standard validation responses:

```json
{
  "status": 400,
  "errors": {
    "password": [
      "Passwords must be at least 8 characters.",
      "Passwords must have at least one digit ('0'-'9')."
    ]
  }
}
```

## Account lockout

Lockout is triggered after too many failed login attempts. Configure in `HPDAuthOptions.Lockout`:

```csharp
builder.Services.AddHPDAuth(options =>
{
    options.Lockout.Enabled = true;
    options.Lockout.MaxFailedAttempts = 5;                  // Lock after 5 failures
    options.Lockout.Duration = TimeSpan.FromMinutes(15);    // Lock for 15 minutes
});
```

### Defaults

| Setting | Default |
|---|---|
| `Enabled` | `true` |
| `MaxFailedAttempts` | `5` |
| `Duration` | `15 minutes` |

### Behavior

- Each failed login increments the failed attempt counter
- A successful login resets the counter
- When the counter reaches `MaxFailedAttempts`, the account is locked until `LockoutEnd`
- A locked account returns `403 Forbidden` with a `locked_out` error code

### Unlocking an account

Admins can unlock a user via `POST /api/admin/users/{id}/unban`.

Lockout expires automatically after `Duration` — no admin action needed.

## Password hashing

HPD.Auth uses ASP.NET Identity's PBKDF2 password hasher with 100,000 iterations by default. This is the platform default and is not configurable through `HPDAuthOptions`. The hashing algorithm is Identity v3 compatible.

## Recommendations for production

- Set `RequiredLength` to at least 10
- Enable `RequireNonAlphanumeric` for higher-security applications
- Keep `MaxFailedAttempts` at 5 or lower — higher values give attackers more attempts
- Consider setting `Duration` to 30 minutes or longer for high-value accounts
