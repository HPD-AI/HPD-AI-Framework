# Sessions & Revocation

HPD.Auth tracks active sessions per user. Each session represents a login from a specific device or browser and can be individually revoked.

## What a session contains

When a user logs in, a `UserSession` record is created with:

- Session ID
- User ID
- IP address
- User agent (device/browser info)
- Created at timestamp
- Expiry
- Revoked flag

## Listing sessions

```http
GET /api/auth/sessions
Authorization: Bearer <token>
```

Returns all active sessions for the current user:

```json
[
  {
    "id": "3fa85f64...",
    "ip_address": "203.0.113.1",
    "user_agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)...",
    "created_at": "2026-03-04T10:00:00Z",
    "current": true
  },
  {
    "id": "7c9e6679...",
    "ip_address": "198.51.100.42",
    "user_agent": "MyApp/1.0 (iPhone; iOS 17.0)",
    "created_at": "2026-03-01T08:00:00Z",
    "current": false
  }
]
```

## Revoking sessions

### Revoke a specific session

```http
DELETE /api/auth/sessions/{id}
Authorization: Bearer <token>
```

### Revoke all sessions

```http
DELETE /api/auth/sessions
Authorization: Bearer <token>
```

## Logout scopes

The logout endpoint accepts a `scope` parameter:

```http
POST /api/auth/logout
Authorization: Bearer <token>
Content-Type: application/json

{ "scope": "local" }
```

| Scope | Effect |
|---|---|
| `local` | Revokes only the current session (default) |
| `others` | Revokes all sessions except the current one |
| `global` | Revokes all sessions, including the current one |

## How revocation works

HPD.Auth uses `UpdateSecurityStampAsync()` for revocation — not a denylist.

When you revoke sessions (`scope=global`, or admin revokes a user's sessions), the user's `SecurityStamp` is rotated in the database. On the next request with an old token, the stamp in the token no longer matches the stamp in the database, and the request is rejected with `401`.

**The tradeoff:** There is a short window (until the token would normally be validated) where an old token could still work. For most applications this is acceptable. If you need immediate sub-second revocation on every request, you would need to reduce the token expiry or add a denylist check — HPD.Auth does not do this by default.

**For cookies:** Cookie auth validates the security stamp on every request automatically (via ASP.NET's `SecurityStampValidator`). Cookie revocation is immediate.

## Admin revocation

Admins can revoke all sessions for any user:

```http
DELETE /api/admin/users/{id}/sessions
Authorization: Bearer <admin-token>
```

This rotates the user's security stamp, invalidating all their active tokens and cookies immediately.
