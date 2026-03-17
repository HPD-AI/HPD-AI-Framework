# User Metadata vs App Metadata

HPD.Auth stores two separate JSON metadata fields on every user: `UserMetadata` and `AppMetadata`. They look similar but serve different purposes and have different write access.

## UserMetadata

`UserMetadata` is writable by the user. It is intended for user preferences, profile customization, and other non-security-sensitive data.

```json
{
  "theme": "dark",
  "bio": "Software developer in Berlin",
  "social_links": {
    "twitter": "@alice",
    "github": "alice-dev"
  },
  "notifications": {
    "email": true,
    "push": false
  }
}
```

Users can read and write `UserMetadata` via `PUT /api/auth/user`:

```json
{
  "user_metadata": {
    "theme": "dark"
  }
}
```

## AppMetadata

`AppMetadata` is writable only by admins. It is intended for system-managed data: subscription tier, feature flags, internal IDs, compliance flags.

```json
{
  "stripe_customer_id": "cus_abc123",
  "subscription_tier": "pro",
  "feature_flags": ["beta_dashboard", "advanced_export"],
  "kyc_verified": true,
  "crm_id": "hs_00491"
}
```

Admins can read and write `AppMetadata` via `PUT /api/admin/users/{id}`:

```json
{
  "app_metadata": {
    "subscription_tier": "enterprise"
  }
}
```

## Why the separation matters

If users could write their own `AppMetadata`, they could escalate their own privileges:

```json
// A user should NOT be able to write this
{
  "subscription_tier": "enterprise",
  "is_admin": true
}
```

HPD.Auth enforces this at the API layer: any `app_metadata` field in a `PUT /api/auth/user` request is **silently ignored**. Users can never modify `AppMetadata` regardless of what they send.

::: warning
If you store authorization decisions in `AppMetadata` (subscription tier, feature flags, role equivalents), make sure you are reading from `AppMetadata` in your authorization handlers — not from `UserMetadata`, which the user can modify freely.
:::

## Reading metadata in code

Both fields are serialized as `string` in the database and deserialized to `JsonElement` in the `TokenResponse`:

```csharp
// In an event handler or endpoint handler
var user = await userManager.FindByIdAsync(userId);

// Parse user metadata
var userMeta = JsonSerializer.Deserialize<JsonElement>(user.UserMetadata);
var theme = userMeta.GetProperty("theme").GetString();

// Parse app metadata
var appMeta = JsonSerializer.Deserialize<JsonElement>(user.AppMetadata);
var tier = appMeta.GetProperty("subscription_tier").GetString();
```

In the token response, both are returned as parsed JSON objects (not strings), so your frontend can use them directly:

```typescript
const { user } = await login(email, password);

// Directly accessible as objects
const theme = user.user_metadata.theme;
const tier = user.app_metadata.subscription_tier;
```
