# Auth Endpoints

Core authentication endpoints for signup, login, logout, user profile, and password recovery.


## POST /api/auth/signup

Register a new user.

**Authentication:** None required.

**Request**

```json
{
  "email": "alice@example.com",
  "password": "Password123!"
}
```

**Response `200 OK`**

Returns a [TokenResponse](/API Reference/00 Overview#token-response-shape).

If `Features.RequireEmailConfirmation` is `true`, the response includes `"required_actions": ["VERIFY_EMAIL"]` and login will be blocked until the email is confirmed.

### Errors

| Status | Reason |
|---|---|
| `400` | Invalid email or password does not meet policy |
| `409` | Email already registered |


## POST /api/auth/token

Login or refresh an access token.

**Authentication:** None required.

### Request — password grant

```json
{
  "grant_type": "password",
  "email": "alice@example.com",
  "password": "Password123!"
}
```

### Request — refresh token grant

```json
{
  "grant_type": "refresh_token",
  "refresh_token": "abc123..."
}
```

**Response `200 OK`**

Returns a [TokenResponse](/API Reference/00 Overview#token-response-shape).

### Errors

| Status | Reason |
|---|---|
| `400` | Missing or invalid `grant_type` |
| `401` | Wrong email or password |
| `401` | Refresh token invalid or revoked |
| `403` | Account locked out |
| `422` | 2FA required — use [POST /api/auth/2fa/verify](/API Reference/03 TwoFactor#post-apiauthfactorsidverify) |


## POST /api/auth/logout

Log out the current user.

**Authentication:** Required.

**Request**

```json
{
  "scope": "local"
}
```

| Scope | Effect |
|---|---|
| `local` | Revoke the current session only (default) |
| `others` | Revoke all other sessions, keep the current one |
| `global` | Revoke all sessions including the current one |

**Response `204 No Content`**


## GET /api/auth/user

Get the current user's profile.

**Authentication:** Required.

**Response `200 OK`**

Returns the `user` object from [TokenResponse](/API Reference/00 Overview#token-response-shape).


## PUT /api/auth/user

Update the current user's profile.

**Authentication:** Required.

**Request**

All fields are optional. Only provided fields are updated.

```json
{
  "first_name": "Alice",
  "last_name": "Smith",
  "display_name": "Alice",
  "avatar_url": "https://example.com/avatar.png",
  "user_metadata": {
    "theme": "dark",
    "bio": "Software developer"
  }
}
```

::: warning
Users cannot write to `app_metadata`. Any `app_metadata` field in the request body is ignored.
:::

**Response `200 OK`**

Returns the updated `user` object.


## POST /api/auth/recover

Request a password reset email.

**Authentication:** None required.

**Request**

```json
{
  "email": "alice@example.com"
}
```

**Response `200 OK`**

Always returns `200` regardless of whether the email exists, to prevent user enumeration.


## POST /api/auth/verify

Verify an email or complete a password reset.

**Authentication:** None required.

### Request — email confirmation

```json
{
  "type": "signup",
  "token": "abc123...",
  "user_id": "3fa85f64..."
}
```

### Request — password reset

```json
{
  "type": "recovery",
  "token": "abc123...",
  "user_id": "3fa85f64...",
  "new_password": "NewPassword456!"
}
```

### Request — email change confirmation

```json
{
  "type": "email_change",
  "token": "abc123...",
  "user_id": "3fa85f64..."
}
```

**Response `200 OK`**

For `type=recovery`, returns a [TokenResponse](/API Reference/00 Overview#token-response-shape) so the user is immediately logged in after resetting.

For `type=signup` and `type=email_change`, returns `200 OK` with no body.

### Errors

| Status | Reason |
|---|---|
| `400` | Invalid or expired token |
| `404` | User not found |


## POST /api/auth/resend

Resend the email confirmation email.

**Authentication:** None required.

**Request**

```json
{
  "email": "alice@example.com"
}
```

**Response `200 OK`**

Always returns `200`. Rate-limited to one request per 60 seconds per email (configurable via `RateLimit.ResendEmailWindowSeconds`).
