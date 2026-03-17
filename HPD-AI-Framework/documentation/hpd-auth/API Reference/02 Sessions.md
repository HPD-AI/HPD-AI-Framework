# Session Endpoints

List and revoke active sessions for the current user.


## GET /api/auth/sessions

Get all active sessions for the current user.

**Authentication:** Required.

**Response `200 OK`**

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "ip_address": "203.0.113.1",
    "user_agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36",
    "created_at": "2026-03-04T10:00:00Z",
    "expires_at": "2026-03-11T10:00:00Z",
    "current": true
  },
  {
    "id": "7c9e6679-7827-4595-b3c4-6012a45a5c2a",
    "ip_address": "198.51.100.42",
    "user_agent": "MyApp/1.0 (iPhone; iOS 17.0)",
    "created_at": "2026-03-01T08:00:00Z",
    "expires_at": "2026-03-08T08:00:00Z",
    "current": false
  }
]
```

The `current` flag is `true` for the session associated with the token used to make this request.


## DELETE /api/auth/sessions/:id

Revoke a specific session.

**Authentication:** Required.

Users can only revoke their own sessions. Admins can revoke any session via the [admin endpoints](/API Reference/06 Admin).

**Response `204 No Content`**

### Errors

| Status | Reason |
|---|---|
| `403` | Session belongs to a different user |
| `404` | Session not found |


## DELETE /api/auth/sessions

Revoke all sessions for the current user.

**Authentication:** Required.

This rotates the user's security stamp, immediately invalidating all active tokens and cookies across all devices.

**Response `204 No Content`**
