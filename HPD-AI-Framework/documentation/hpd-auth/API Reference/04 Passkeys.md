# Passkey Endpoints

Endpoints for registering and authenticating with FIDO2/WebAuthn passkeys (biometric, hardware security keys, etc.).

Requires `HPD.Auth.TwoFactor`, `options.Features.EnablePasskeys = true`, and `app.MapHPDTwoFactorEndpoints()`.

::: info
Your app must also register an `IPasskeyHandler<ApplicationUser>` in DI. This is an ASP.NET Identity 10 requirement — see [HPD.Auth.TwoFactor →](/Packages/03 TwoFactor) for setup details.
:::

## Registration flow

```
1. POST /api/auth/passkey/register/options   ← get creation options
2. (browser calls navigator.credentials.create() with the options)
3. POST /api/auth/passkey/register/complete  ← send attestation response
```

## POST /api/auth/passkey/register/options

Get WebAuthn creation options to start passkey registration.

**Authentication:** Required.

**Request**

```json
{}
```

**Response 200 OK**

Returns a `PublicKeyCredentialCreationOptions` object to pass directly to `navigator.credentials.create()` in the browser.

```json
{
  "rp": { "id": "myapp.com", "name": "MyApp" },
  "user": {
    "id": "M2ZhODVmNjQ...",
    "name": "alice@example.com",
    "displayName": "Alice"
  },
  "challenge": "abc123...",
  "pubKeyCredParams": [...],
  "timeout": 60000,
  "authenticatorSelection": {
    "residentKey": "required",
    "userVerification": "required"
  }
}
```

## POST /api/auth/passkey/register/complete

Complete passkey registration by submitting the attestation response from the browser.

**Authentication:** Required.

**Request** — pass the result of `navigator.credentials.create()` directly:

```json
{
  "id": "credential-id",
  "rawId": "base64url...",
  "response": {
    "clientDataJSON": "base64url...",
    "attestationObject": "base64url..."
  },
  "type": "public-key",
  "name": "MacBook Touch ID"
}
```

The optional `name` field lets the user label their passkey.

**Response 200 OK**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "MacBook Touch ID",
  "created_at": "2026-03-04T10:00:00Z"
}
```

## Authentication flow

```
1. POST /api/auth/passkey/authenticate/options   ← get assertion options
2. (browser calls navigator.credentials.get() with the options)
3. POST /api/auth/passkey/authenticate/complete  ← send assertion response
```

## POST /api/auth/passkey/authenticate/options

Get WebAuthn assertion options to start passkey authentication.

**Authentication:** None required.

**Request**

```json
{}
```

**Response 200 OK** — returns a `PublicKeyCredentialRequestOptions` object to pass to `navigator.credentials.get()`.

## POST /api/auth/passkey/authenticate/complete

Complete authentication by submitting the assertion response.

**Authentication:** None required.

**Request** — pass the result of `navigator.credentials.get()` directly:

```json
{
  "id": "credential-id",
  "rawId": "base64url...",
  "response": {
    "clientDataJSON": "base64url...",
    "authenticatorData": "base64url...",
    "signature": "base64url...",
    "userHandle": "base64url..."
  },
  "type": "public-key"
}
```

**Response 200 OK** — returns a full [TokenResponse](/API Reference/00 Overview#token-response-shape).

## GET /api/auth/passkeys

List the current user's registered passkeys.

**Authentication:** Required.

**Response 200 OK**

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "MacBook Touch ID",
    "created_at": "2026-03-04T10:00:00Z",
    "last_used_at": "2026-03-04T12:00:00Z"
  }
]
```

## PATCH /api/auth/passkeys/:id

Rename a passkey. **Authentication:** Required.

**Request**

```json
{ "name": "My YubiKey" }
```

**Response 200 OK**

## DELETE /api/auth/passkeys/:id

Remove a passkey. **Authentication:** Required.

**Response 204 No Content**
