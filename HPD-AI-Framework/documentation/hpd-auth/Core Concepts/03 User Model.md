# User Model

Every user in HPD.Auth is an `ApplicationUser` — an extension of ASP.NET Core Identity's `IdentityUser<Guid>` with additional fields for metadata, profile, account status, and multi-tenancy.

## Fields

### Identity (from ASP.NET Identity)

| Field | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique user ID |
| `Email` | `string` | Email address |
| `EmailConfirmed` | `bool` | Whether the email has been verified |
| `PhoneNumber` | `string?` | Phone number |
| `TwoFactorEnabled` | `bool` | Whether 2FA is active |
| `LockoutEnd` | `DateTimeOffset?` | When the lockout expires (null = not locked) |

### Profile

| Field | Type | Description |
|---|---|---|
| `FirstName` | `string?` | First name |
| `LastName` | `string?` | Last name |
| `DisplayName` | `string?` | Display name (shown in UI) |
| `AvatarUrl` | `string?` | URL to the user's avatar image |

### Metadata

| Field | Type | Description |
|---|---|---|
| `UserMetadata` | `string` (JSON) | User-writable metadata. Suitable for preferences, bio, social links. |
| `AppMetadata` | `string` (JSON) | Admin-only metadata. Suitable for subscription tier, feature flags, internal IDs. |

Both fields are stored as JSON. See [User Metadata vs App Metadata →](/Security/02 Metadata) for the security implications.

### Account status

| Field | Type | Description |
|---|---|---|
| `IsActive` | `bool` | Whether the account is active. Default: `true`. |
| `IsDeleted` | `bool` | Soft delete flag. |
| `DeletedAt` | `DateTime?` | When the account was soft-deleted. |
| `SubscriptionTier` | `string` | Subscription tier. Default: `"free"`. |

### Tracking

| Field | Type | Description |
|---|---|---|
| `Created` | `DateTime` | When the account was created. |
| `Updated` | `DateTime` | When the account was last updated. |
| `LastLoginAt` | `DateTime?` | Last successful login timestamp. |
| `LastLoginIp` | `string?` | IP address of the last login. |
| `EmailConfirmedAt` | `DateTime?` | When the email was confirmed. |

### RequiredActions

`RequiredActions` is a `List<string>` of actions the user must complete before getting full access. While any pending actions exist, login returns a partial auth response rather than a full token.

Built-in values:

| Value | Meaning |
|---|---|
| `"VERIFY_EMAIL"` | User must confirm their email |
| `"UPDATE_PASSWORD"` | User must change their password |
| `"ACCEPT_TOS"` | User must accept terms of service |
| `"CONFIGURE_2FA"` | User must set up 2FA |

### Multi-tenancy

| Field | Type | Description |
|---|---|---|
| `InstanceId` | `Guid` | Tenant discriminator. Defaults to `Guid.Empty` in single-tenant apps. |

See [Multi-Tenancy →](/Guides/06 Multi-Tenancy) for how to activate tenant isolation.

## The user in token responses

When a token is issued, a `user` object is embedded in the response:

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
    "subscription_tier": "pro",
    "user_metadata": {},
    "app_metadata": {},
    "two_factor_enabled": false,
    "last_login_at": "2026-03-04T10:00:00Z",
    "created_at": "2026-01-15T09:00:00Z",
    "updated_at": "2026-03-04T10:00:00Z"
  }
}
```

## Getting the current user

```http
GET /api/auth/user
Authorization: Bearer <token>
```

## Updating the current user

Users can update their own profile fields and `UserMetadata`. They cannot write to `AppMetadata`.

```http
PUT /api/auth/user
Authorization: Bearer <token>
Content-Type: application/json

{
  "first_name": "Alice",
  "last_name": "Smith",
  "display_name": "Alice",
  "user_metadata": {
    "theme": "dark",
    "bio": "Software developer"
  }
}
```
