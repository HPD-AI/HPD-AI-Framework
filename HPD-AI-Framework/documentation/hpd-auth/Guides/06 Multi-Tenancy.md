# Multi-Tenancy

Multi-tenancy support is baked into HPD.Auth as a "Sleeper Primitive" — the data model is ready for it, but it does nothing until you activate it. Single-tenant apps run with no configuration and zero overhead.

## How it works

Every entity (users, sessions, refresh tokens, audit logs, etc.) has an `InstanceId: Guid` field. EF Core global query filters ensure every database query is automatically scoped to the current tenant's `InstanceId`.

In single-tenant mode, `InstanceId` is always `Guid.Empty`. The query filters still apply — they just always filter for the same value.

## Activating multi-tenancy

### Step 1: Implement ITenantContext

Create a class that resolves the current tenant's `InstanceId` from the request:

```csharp
using HPD.Auth.Core.Interfaces;

public class HeaderTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;

    public HeaderTenantContext(IHttpContextAccessor http) => _http = http;

    public Guid InstanceId
    {
        get
        {
            var header = _http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            if (header != null && Guid.TryParse(header, out var id))
                return id;

            return Guid.Empty; // fallback to default tenant
        }
    }
}
```

Common patterns for resolving tenant:
- **HTTP header** (`X-Tenant-Id`) — shown above
- **Subdomain** — `tenant1.yourapp.com` → parse from `Host` header
- **JWT claim** — include `instance_id` in the token and read it from `ClaimsPrincipal`
- **Route parameter** — `/api/tenants/{tenantId}/...`

### Step 2: Register the implementation

Register your `ITenantContext` **before** calling `AddHPDAuth()`. HPD.Auth registers a `SingleTenantContext` (always returns `Guid.Empty`) as the default — your registration replaces it:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HeaderTenantContext>();

builder.Services.AddHPDAuth(options => { ... });
```

That's it. All database queries are now automatically tenant-scoped.

## What isolation covers

Once `ITenantContext` is returning real tenant IDs, the following are automatically isolated per tenant:

- Users and their passwords, profiles, metadata
- Sessions and refresh tokens
- Roles and claims
- Audit logs
- SSO providers
- Tenant settings

Composite unique indexes enforce that the same email address can exist in different tenants — `(InstanceId, NormalizedEmail)` is unique, not just `NormalizedEmail` alone.

## Creating tenants

HPD.Auth doesn't manage tenant lifecycle — that's your application's concern. Each tenant is identified by a `Guid` you assign. There is no tenant registration endpoint.

A typical pattern is to store tenants in your own table and generate a `Guid` ID when a tenant is created.

## Admin cross-tenant queries

The query filters apply to all queries by default. For admin operations that need to work across tenants (e.g., a super-admin dashboard), use `IgnoreQueryFilters()`:

```csharp
// This is a raw EF Core operation — do this in your own code, not via HPD.Auth endpoints
var allUsers = await dbContext.Users
    .IgnoreQueryFilters()
    .ToListAsync();
```

The HPD.Auth admin endpoints are scoped to the current tenant. Cross-tenant admin is out of scope for HPD.Auth and should be implemented in your own code.

## Tenant settings

Each tenant can have its own settings row (`TenantSettings`) with branding configuration. The `InstanceId` is the primary key — one row per tenant.

```csharp
// Accessing via HPDAuthDbContext (inject it directly if needed)
var settings = await dbContext.TenantSettings.FirstOrDefaultAsync();
```
