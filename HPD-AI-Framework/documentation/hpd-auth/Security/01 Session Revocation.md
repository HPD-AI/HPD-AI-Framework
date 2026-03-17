# Session Revocation

HPD.Auth uses security stamp rotation for session revocation — not a token denylist.

## How it works

Every user has a `SecurityStamp` — a random string stored in the database. This stamp is embedded in every JWT as a claim when the token is issued.

On every authenticated request, HPD.Auth validates the stamp in the token against the stamp in the database. If they don't match, the request is rejected with `401 Unauthorized`.

When you revoke sessions, HPD.Auth calls `UpdateSecurityStampAsync()`, which generates a new random stamp. All existing tokens immediately contain the old stamp — they will be rejected on the next request.

## The tradeoff

**Pros:**
- No denylist table to query on every request
- Scales horizontally without coordination between instances
- Works for both JWT and cookies

**Cons:**
- Revocation is not instantaneous for JWT. There is a short window between when you revoke and when the user's next request arrives. For a 60-minute token, this window is at most 60 minutes — but in practice, tokens are typically refreshed every 5-15 minutes.

For cookies, revocation is immediate because the cookie's security stamp is validated on every request by ASP.NET's `SecurityStampValidator`.

## When revocation is triggered

| Action | What happens |
|---|---|
| `POST /api/auth/logout` with `scope=global` | Security stamp rotated |
| `POST /api/auth/logout` with `scope=others` | Security stamp rotated |
| `DELETE /api/auth/sessions` | Security stamp rotated |
| `DELETE /api/admin/users/{id}/sessions` | Security stamp rotated |
| User changes password | Security stamp rotated (by ASP.NET Identity) |
| Admin resets password | Security stamp rotated |

`scope=local` logout does **not** rotate the security stamp — it only marks the current session as revoked in the `UserSessions` table.

## Reducing the revocation window

If your application requires faster revocation, reduce the token expiry:

```csharp
builder.Services.AddHPDAuth(options =>
{
    options.Jwt.ExpiryMinutes = 5; // Tokens expire in 5 minutes
});
```

Shorter tokens mean more frequent refreshes, which slightly increases load. Choose based on your security requirements.
