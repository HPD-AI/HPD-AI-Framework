# HPD-AI.Platform

Composable Svelte-based Studio shell for HPD AI framework modules.

## Use

```csharp
builder.Services.AddHPDAIPlatform()
    .AddBaseStudio()
    .AddGraphStudio();

app.MapHPDAIPlatform(options =>
{
    options.RoutePrefix = "/studio";
    options.ApiBasePath = "/api/hpd";
});
```

`HPD-AI.Platform` owns the shell, runtime configuration, routing, and final CSS bundle. Domain Studio packages contribute immutable module definitions through the frozen Studio graph. Placeholder-only product modules are not registered or shipped.

Product packages should prefer their governed mapping extension (for example,
`MapHpdGatewayStudio`) when listener ownership or endpoint isolation is part of the product
contract. The shared host serves only registered SPA routes, rejects unknown or stale assets,
and applies its immutable-cache and restrictive browser-security policy.

## Frontend Source

- `typescript/hpd-ai-studio/shell` owns the composed HPD AI Platform shell.
- `typescript/hpd-ai-studio/packages/hpd-studio-design` owns the shared design contract.
- `typescript/hpd-ai-studio/modules/*` owns the module contributions packaged by each domain `.Studio` package.

```bash
cd ../../../../typescript/hpd-ai-studio/shell
npm install
npm run build
```
