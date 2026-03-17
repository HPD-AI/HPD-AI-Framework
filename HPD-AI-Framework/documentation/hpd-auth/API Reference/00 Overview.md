# REST API Reference

HPD.Auth exposes a REST API at `/api/auth/*` and `/api/admin/*`. All endpoints accept and return JSON.

## Base URL

The API is served from your app's base URL. There is no separate service.

```
https://your-app.com/api/auth/*
https://your-app.com/api/admin/*
```

## Authentication

Most endpoints require authentication. Pass the access token as a Bearer header:

```http
Authorization: Bearer eyJhbGci...
```

Endpoints marked **Admin** additionally require the user to have the `Admin` role.

## Response format

### Success

Responses vary by endpoint. Token endpoints return a `TokenResponse`. User endpoints return a `UserResponse`. See each endpoint for the specific shape.

### Errors

All errors return a standard shape:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "errors": {
    "email": ["The Email field is required."]
  }
}
```

Common status codes:

| Code | Meaning |
|---|---|
| `400` | Validation error or bad request body |
| `401` | Missing or invalid token |
| `403` | Authenticated but not authorized (e.g., not an admin) |
| `404` | Resource not found |
| `409` | Conflict (e.g., email already registered) |
| `422` | Business logic error (e.g., invalid password, wrong 2FA code) |
| `429` | Rate limited |

## Token response shape

All login and signup endpoints return the same token shape:

```json
{
  "access_token": "eyJhbGci...",
  "refresh_token": "abc123...",
  "expires_at": 1735689600,
  "token_type": "bearer",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "alice@example.com",
    "email_confirmed": true,
    "email_confirmed_at": "2026-01-15T09:00:00Z",
    "first_name": "Alice",
    "last_name": "Smith",
    "display_name": "Alice",
    "avatar_url": null,
    "subscription_tier": "free",
    "user_metadata": {},
    "app_metadata": {},
    "two_factor_enabled": false,
    "required_actions": [],
    "last_login_at": "2026-03-04T10:00:00Z",
    "created_at": "2026-01-15T09:00:00Z",
    "updated_at": "2026-03-04T10:00:00Z"
  }
}
```

`expires_at` is a Unix timestamp (seconds since epoch).

## Sections

- [Auth endpoints →](/API Reference/01 Auth) — signup, login, logout, user profile, password recovery
- [Session endpoints →](/API Reference/02 Sessions) — list and revoke sessions
- [2FA & TOTP endpoints →](/API Reference/03 TwoFactor) — setup, verify, disable TOTP
- [Passkey endpoints →](/API Reference/04 Passkeys) — register and authenticate with FIDO2
- [OAuth endpoints →](/API Reference/05 OAuth) — social login with Google, GitHub, Microsoft
- [Admin endpoints →](/API Reference/06 Admin) — user management, audit logs, generate links
