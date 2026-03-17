# Protect Endpoints

HPD.Auth registers built-in authorization policies you can apply to any endpoint with `RequireAuthorization()` or `[Authorize]`.

## Built-in policies

| Policy name | Requires |
|---|---|
| `HPDAuthPolicies.RequireAdmin` | `Admin` role |
| `HPDAuthPolicies.RequireUser` | Authenticated (any role) |
| `HPDAuthPolicies.RequireEmailVerified` | `EmailConfirmed = true` |
| `HPDAuthPolicies.RequireTwoFactor` | `TwoFactorEnabled = true` |
| `HPDAuthPolicies.RequireActiveSubscription` | `SubscriptionTier != "free"` |

## Protect a Minimal API endpoint

```csharp
using HPD.Auth.Authorization;

app.MapGet("/dashboard", () => "Welcome!")
   .RequireAuthorization(HPDAuthPolicies.RequireUser);

app.MapGet("/admin", () => "Admin panel")
   .RequireAuthorization(HPDAuthPolicies.RequireAdmin);

app.MapGet("/pro-feature", () => "Premium content")
   .RequireAuthorization(HPDAuthPolicies.RequireActiveSubscription);
```

## Protect a controller

```csharp
[Authorize(Policy = HPDAuthPolicies.RequireAdmin)]
public class AdminController : ControllerBase
{
    [HttpGet("users")]
    public IActionResult GetUsers() => Ok();

    [AllowAnonymous]
    [HttpGet("health")]
    public IActionResult Health() => Ok();
}
```

## Require authentication globally

To require authentication for all endpoints by default (opt-out instead of opt-in):

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

Then use `[AllowAnonymous]` on public endpoints.

## Custom policies

Add your own policies in `Program.cs`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireEnterprise", policy =>
        policy.RequireClaim("subscription_tier", "enterprise"));

    options.AddPolicy("RequireUKUser", policy =>
        policy.RequireClaim("country", "GB"));
});
```

## Rate limiting policy

HPD.Auth.Authorization includes a rate-limiting requirement. Apply it to sensitive endpoints:

```csharp
app.MapPost("/api/transfer", HandleTransfer)
   .RequireAuthorization(HPDAuthPolicies.RequireUser)
   .RequireAuthorization("RateLimit");
```

Configure the rate limit in options (via `HPDAuthOptions.RateLimit`).

## Accessing the current user

In Minimal API handlers, access the current user through `HttpContext`:

```csharp
app.MapGet("/profile", (HttpContext ctx) =>
{
    var userId = ctx.User.FindFirst("sub")?.Value;
    var email = ctx.User.FindFirst("email")?.Value;
    return Results.Ok(new { userId, email });
}).RequireAuthorization();
```

In controllers, use `User` directly:

```csharp
[HttpGet("profile")]
[Authorize]
public IActionResult Profile()
{
    var userId = User.FindFirst("sub")?.Value;
    return Ok(new { userId });
}
```
