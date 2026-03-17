# 2FA & TOTP Endpoints

Endpoints for setting up and verifying time-based one-time passwords (TOTP) and completing 2FA during login.

Requires `HPD.Auth.TwoFactor` and `app.MapHPDTwoFactorEndpoints()`.


## POST /api/auth/factors

Begin TOTP setup. Generates a shared secret and QR code URI for the user's authenticator app.

**Authentication:** Required.

**Request**

```json
{}
```

**Response `200 OK`**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "totp",
  "shared_key": "JBSWY3DPEHPK3PXP",
  "qr_code_uri": "otpauth://totp/MyApp:alice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=MyApp",
  "recovery_codes": null
}
```

Display the `qr_code_uri` as a QR code for the user to scan with their authenticator app (Google Authenticator, Authy, etc.), or show the `shared_key` for manual entry.


## POST /api/auth/factors/:id/challenge

Initiate a challenge for an existing factor. Currently a no-op for TOTP (the user generates codes from their app), but required to be called before verify.

**Authentication:** Required.

**Response `200 OK`**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "totp"
}
```


## POST /api/auth/factors/:id/verify

Verify a TOTP code and enable 2FA, or confirm a challenge.

**Authentication:** Required.

**Request**

```json
{
  "code": "123456"
}
```

**Response `200 OK`**

If this is the first verification (enabling 2FA), the response includes recovery codes:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "totp",
  "recovery_codes": [
    "abc12-defgh",
    "ijk34-lmnop",
    "qrs56-tuvwx",
    "yz789-01234",
    "56789-abcde",
    "fghij-klmno",
    "pqrst-uvwxy",
    "z1234-56789"
  ]
}
```

Store recovery codes securely. They are only shown once.

### Errors

| Status | Reason |
|---|---|
| `422` | Invalid or expired TOTP code |
| `404` | Factor not found |


## DELETE /api/auth/factors/:id

Remove a 2FA factor, disabling 2FA for the user.

**Authentication:** Required.

**Response `204 No Content`**


## POST /api/auth/2fa/verify

Complete a login when 2FA is required. Called after a `POST /api/auth/token` that returns `422` with a `2fa_required` error.

**Authentication:** None required (uses a temporary 2FA challenge token from the failed login response).

### Request — TOTP code

```json
{
  "challenge_token": "temp-token-from-login-response",
  "code": "123456"
}
```

### Request — Recovery code

```json
{
  "challenge_token": "temp-token-from-login-response",
  "recovery_code": "abc12-defgh"
}
```

**Response `200 OK`**

Returns a full [TokenResponse](/API Reference/00 Overview#token-response-shape).

### Errors

| Status | Reason |
|---|---|
| `422` | Invalid TOTP code or recovery code |
| `401` | Challenge token expired or invalid |
