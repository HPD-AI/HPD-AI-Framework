# HPD.Auth.Authorization

Adds built-in authorization policies, rate limiting, feature flag support, and subscription tier gating.

## Installation

```bash
dotnet add package HPD.Auth.Authorization
```

```csharp
builder.Services
    .AddHPDAuth(options => { ... })
    .AddAuthentication()
    .AddAuthorization();   // ← this package
```

## Built-in policies

Use these with `RequireAuthorization()` or `[Authorize(Policy = ...)]`:

```csharp
using HPD.Auth.Authorization;

app.MapGet("/admin", AdminHandler)
   .RequireAuthorization(HPDAuthPolicies.RequireAdmin);

app.MapGet("/dashboard", DashboardHandler)
   .RequireAuthorization(HPDAuthPolicies.RequireUser);

app.MapGet("/pro", ProHandler)
   .RequireAuthorization(HPDAuthPolicies.RequireActiveSubscription);
```

| Policy constant | Requires |
|---|---|
| `HPDAuthPolicies.RequireAdmin` | `Admin` role |
| `HPDAuthPolicies.RequireUser` | Any authenticated user |
| `HPDAuthPolicies.RequireEmailVerified` | Email confirmed |
| `HPDAuthPolicies.RequireTwoFactor` | 2FA enabled |
| `HPDAuthPolicies.RequireActiveSubscription` | `subscription_tier` != `"free"` |

## Authorization requirements

Five custom requirements back the built-in policies. You can also use them to build your own:

| Requirement | Handler | Description |
|---|---|---|
| `AppAccessRequirement` | `AppAccessHandler` | Validates the `Audience` claim |
| `ResourceOwnerRequirement` | `ResourceOwnerHandler` | Validates resource ownership |
| `SubscriptionTierRequirement` | `SubscriptionTierHandler` | Checks subscription tier |
| `RateLimitRequirement` | `RateLimitHandler` | Enforces rate limiting |
| `FeatureFlagRequirement` | `FeatureFlagHandler` | Checks a feature flag |

### Custom policy example

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireProOrHigher", policy =>
        policy.AddRequirements(new SubscriptionTierRequirement(
            allowedTiers: ["pro", "enterprise"])));
});
```

## Pluggable services

Some requirements are backed by interfaces you implement:

| Interface | Used by | Purpose |
|---|---|---|
| `ISubscriptionService` | `SubscriptionTierHandler` | Look up user subscription tier |
| `IFeatureFlagService` | `FeatureFlagHandler` | Check if a feature flag is enabled |
| `IAppPermissionService` | `AppAccessHandler` | Validate app-scoped access |
| `IRateLimitService` | `RateLimitHandler` | Rate limit enforcement |

`IRateLimitService` has a built-in in-memory implementation (`InMemoryRateLimitService`) registered by default. The others are no-ops until you provide an implementation.

## Rate limiting

The built-in in-memory rate limiter is registered automatically. Configure it via `HPDAuthOptions.RateLimit`.

To use a distributed rate limiter (e.g., Redis-backed), implement `IRateLimitService` and register it:

```csharp
builder.Services.AddScoped<IRateLimitService, RedisRateLimitService>();
```
