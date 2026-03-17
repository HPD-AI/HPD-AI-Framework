# Admin Endpoints

Endpoints for managing users, sessions, roles, claims, and audit logs. All admin endpoints require the `Admin` role.

Requires `HPD.Auth.Admin` and `app.MapHPDAdminEndpoints()`.


## Users

**GET /api/admin/users**

List users with optional filtering and pagination.

**Authentication:** Admin required.

**Query parameters**

| Parameter | Type | Description |
|---|---|---|
| `page` | `int` | Page number (default: 1) |
| `pageSize` | `int` | Results per page (default: 20, max: 100) |
| `email` | `string?` | Filter by email (partial match) |
| `role` | `string?` | Filter by role name |
| `isActive` | `bool?` | Filter by active status |

**Response `200 OK`**

```json
{
  "users": [
    {
      "id": "3fa85f64...",
      "email": "alice@example.com",
      "first_name": "Alice",
      "last_name": "Smith",
      "subscription_tier": "pro",
      "is_active": true,
      "email_confirmed": true,
      "two_factor_enabled": false,
      "created_at": "2026-01-15T09:00:00Z",
      "last_login_at": "2026-03-04T10:00:00Z"
    }
  ],
  "total": 1,
  "page": 1,
  "page_size": 20
}
```


**GET /api/admin/users/count**

Get total user count.

**Authentication:** Admin required.

**Response `200 OK`**

```json
{ "count": 1423 }
```


**GET /api/admin/users/{id}**

Get a specific user by ID.

**Authentication:** Admin required.

**Response `200 OK`**

Returns a full user object including `app_metadata`.


**POST /api/admin/users**

Create a user directly (no signup flow, no email confirmation).

**Authentication:** Admin required.

**Request**

```json
{
  "email": "bob@example.com",
  "password": "TempPassword123!",
  "first_name": "Bob",
  "role": "user",
  "email_confirmed": true,
  "app_metadata": {
    "subscription_tier": "pro"
  }
}
```

**Response `201 Created`**

Returns the created user object.


**PUT /api/admin/users/{id}**

Update any user field, including `app_metadata`.

**Authentication:** Admin required.

**Request**

```json
{
  "first_name": "Robert",
  "subscription_tier": "enterprise",
  "app_metadata": {
    "stripe_customer_id": "cus_abc123"
  }
}
```

**Response `200 OK`**


**DELETE /api/admin/users/{id}**

Soft-delete a user. Sets `IsDeleted = true` and `DeletedAt`.

**Authentication:** Admin required.

**Response `204 No Content`**


## User Actions

**POST /api/admin/users/{id}/ban**

Ban a user for a specified duration.

**Authentication:** Admin required.

**Request**

```json
{
  "duration": "24h"
}
```

Supported duration formats: `"30m"`, `"1h"`, `"24h"`, `"7d"`, or any `TimeSpan`-parseable string.

**Response `204 No Content`**


**POST /api/admin/users/{id}/unban**

Remove a ban from a user.

**Authentication:** Admin required.

**Response `204 No Content`**


**POST /api/admin/users/{id}/reset-password**

Force a password reset for a user.

**Authentication:** Admin required.

**Request**

```json
{
  "new_password": "NewTemp456!"
}
```

**Response `204 No Content`**


**POST /api/admin/users/{id}/confirm-email**

Manually confirm a user's email.

**Authentication:** Admin required.

**Response `204 No Content`**


## Sessions

**GET /api/admin/users/{id}/sessions**

List all sessions for a user.

**Authentication:** Admin required.

**Response `200 OK`**

Same shape as [GET /api/auth/sessions](/API Reference/02 Sessions#get-apiauthsessions).


**DELETE /api/admin/users/{id}/sessions**

Revoke all sessions for a user. Rotates their security stamp.

**Authentication:** Admin required.

**Response `204 No Content`**


## Roles

**GET /api/admin/users/{id}/roles**

List roles assigned to a user.

**Authentication:** Admin required.

**Response `200 OK`**

```json
["user", "editor"]
```


**POST /api/admin/users/{id}/roles**

Assign a role to a user.

**Authentication:** Admin required.

**Request**

```json
{ "role": "editor" }
```

**Response `204 No Content`**


**DELETE /api/admin/users/{id}/roles/{role}**

Remove a role from a user.

**Authentication:** Admin required.

**Response `204 No Content`**


## Claims

**GET /api/admin/users/{id}/claims**

List custom claims for a user.

**Authentication:** Admin required.

**Response `200 OK`**

```json
[
  { "type": "department", "value": "engineering" }
]
```


**POST /api/admin/users/{id}/claims**

Add a custom claim.

**Authentication:** Admin required.

**Request**

```json
{ "type": "department", "value": "engineering" }
```

**Response `204 No Content`**


**DELETE /api/admin/users/{id}/claims**

Remove a custom claim.

**Authentication:** Admin required.

**Request**

```json
{ "type": "department", "value": "engineering" }
```

**Response `204 No Content`**


## Audit Logs

**GET /api/admin/audit-logs**

Query the audit log.

**Authentication:** Admin required.

**Query parameters**

| Parameter | Type | Description |
|---|---|---|
| `userId` | `string?` | Filter by user ID |
| `action` | `string?` | Filter by action (e.g., `"Login"`, `"Signup"`) |
| `category` | `string?` | Filter by category |
| `from` | `DateTime?` | Start of time range |
| `to` | `DateTime?` | End of time range |
| `page` | `int` | Page number (default: 1) |
| `pageSize` | `int` | Results per page (default: 50, max: 200) |

**Response `200 OK`**

```json
{
  "logs": [
    {
      "id": "3fa85f64...",
      "user_id": "7c9e6679...",
      "email": "alice@example.com",
      "action": "Login",
      "category": "Authentication",
      "ip_address": "203.0.113.1",
      "user_agent": "Mozilla/5.0...",
      "timestamp": "2026-03-04T10:00:00Z",
      "metadata": {}
    }
  ],
  "total": 1248,
  "page": 1,
  "page_size": 50
}
```


## Links

**POST /api/admin/generate-link**

Generate a signed URL for password reset or email verification. Useful for sending custom emails without going through the standard flow.

**Authentication:** Admin required.

**Request**

```json
{
  "type": "recovery",
  "email": "alice@example.com"
}
```

Supported types: `"recovery"`, `"signup"`, `"email_change"`.

**Response `200 OK`**

```json
{
  "action_link": "https://yourapp.com/api/auth/verify?type=recovery&token=abc123&user_id=3fa85f64",
  "email": "alice@example.com",
  "expires_at": "2026-03-05T10:00:00Z"
}
```
