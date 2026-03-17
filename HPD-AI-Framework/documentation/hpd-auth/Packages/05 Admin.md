# HPD.Auth.Admin

Adds admin endpoints for user management, session revocation, role/claim management, and audit log querying.

## Installation

```bash
dotnet add package HPD.Auth.Admin
```

```csharp
builder.Services
    .AddHPDAuth(options => { ... })
    .AddAuthentication()
    .AddAdmin();   // ← this package

app.UseHPDAuth();
app.MapHPDAuthEndpoints();
app.MapHPDAdminEndpoints();   // ← add this
```

## Access control

All admin endpoints require the `Admin` role. Assign it to a user through the admin API itself, or seed it at startup:

```csharp
// Seed an admin user at startup (example)
app.Lifetime.ApplicationStarted.Register(async () =>
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    var admin = await userManager.FindByEmailAsync("admin@yourapp.com");
    if (admin != null && !await userManager.IsInRoleAsync(admin, "Admin"))
    {
        await userManager.AddToRoleAsync(admin, "Admin");
    }
});
```

## Endpoint groups

| Group | Endpoints |
|---|---|
| Users | CRUD, list, count, filter |
| User Actions | ban, unban, reset password, confirm email |
| Sessions | list, revoke all |
| Roles | list, assign, remove |
| Claims | list, add, remove |
| External Logins | list, remove |
| 2FA | disable 2FA for a user |
| Audit Logs | query with filters |
| Links | generate signed reset/confirm URLs |

See [Admin API Reference →](/API Reference/06 Admin) for the full endpoint documentation.

## Implementation notes

The admin endpoints wrap `UserManager<ApplicationUser>` and `SignInManager<ApplicationUser>` directly — there is no custom reimplementation of Identity logic. This means any behavior you've configured on those services (custom validators, custom token providers) applies to admin operations as well.
