# Authentication

HPD.Auth supports two authentication schemes — **JWT** and **Cookie** — and automatically routes each request to the right one.

## How routing works

A single `PolicyScheme` inspects every incoming request and forwards it to the correct handler:

```
Authorization: Bearer <token>  →  JWT handler
(anything else)                →  Cookie handler
```

This means browser apps get cookie auth automatically, API clients get JWT — no per-endpoint configuration needed.

## JWT

JWT tokens are HS256-signed using the `Jwt.Secret` you configure. Each token contains:

- `sub` — user ID
- `email`
- `roles` — all roles assigned to the user
- `security_stamp` — used for immediate revocation (see [Sessions →](/Core Concepts/02 Sessions))
- Any custom claims

Access tokens are short-lived (default 60 minutes). Refresh tokens are single-use and stored in the database. When a refresh token is used, a new access token and a new refresh token are issued — the used token is revoked.

### Getting a token

```http
POST /api/auth/token
Content-Type: application/json

{
  "grant_type": "password",
  "email": "alice@example.com",
  "password": "Password123!"
}
```

### Using a token

```http
GET /api/auth/user
Authorization: Bearer eyJhbGci...
```

### Refreshing a token

```http
POST /api/auth/token
Content-Type: application/json

{
  "grant_type": "refresh_token",
  "refresh_token": "abc123..."
}
```

## Cookie auth

Cookie auth is handled by ASP.NET's built-in cookie middleware, configured by `HPD.Auth.Authentication`. It is the default for browser-based apps where the browser manages the cookie automatically.

Cookie auth is activated when no `Authorization: Bearer` header is present on the request.

## Security stamp validation

On every authenticated JWT request, HPD.Auth validates the `security_stamp` claim in the token against the current stamp in the database. If they don't match (because the user changed their password or was forcibly logged out), the request is rejected with `401 Unauthorized` — even if the token has not expired yet.

This is how logout works across sessions without maintaining a denylist. See [Sessions & Revocation →](/Core Concepts/02 Sessions) for details.
