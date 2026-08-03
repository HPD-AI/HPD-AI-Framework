# HPD.Gateway.OutputCaching

This optional package registers closed conservative ASP.NET Core Output Cache
profiles for HPD-managed YARP Routes.

It owns profile validation and startup composition only. ASP.NET Core owns
cache admission, key construction, response capture, the process-local memory
store, expiration, locking, and cached-response serving.

```csharp
services.AddHpdGatewayOutputCaching(cache =>
{
    cache.MaximumBodyBytes = 1024 * 1024;
    cache.StoreCapacityBytes = 16 * 1024 * 1024;
    cache.Add(new GatewayOutputCacheProfile
    {
        Name = "public-api",
        Version = 1,
        Expiration = TimeSpan.FromMinutes(1),
        QueryKeys = ["language"],
        HeaderNames = ["accept-encoding"]
    });
});

app.UseAuthentication();
app.UseAuthorization();
app.UseHpdGatewayOutputCaching();
app.MapHpdGatewayReverseProxy();
```

The middleware must run after host authentication/authorization and the
managed proxy endpoint must be mapped exactly once. Candidate validation
requires an explicit GET/HEAD-only Route, effective credential stripping, and
no request inspection on the same Route.

Redis/shared stores, private response caching, dynamic policies, stale or
revalidation semantics, range caching, and globally acknowledged purge are
not supported by this package.
