# HPD-AI.Studio

Composable Svelte-based Studio shell for HPD AI framework modules.

## Use

```csharp
builder.Services.AddHPDAIStudio()
    .AddAgentStudio()
    .AddGraphStudio()
    .AddRagStudio()
    .AddAuthStudio()
    .AddMLStudio();

app.MapHPDAIStudio(options =>
{
    options.RoutePrefix = "/studio";
    options.ApiBasePath = "/api/hpd";
});
```

`HPD-AI.Studio` owns the shell, runtime configuration, routing, and final CSS bundle. Domain Studio packages such as `HPD-Agent.Studio`, `HPD-Graph.Studio`, `HPD-RAG.Studio`, `HPD.Auth.Studio`, and `HPD.ML.Studio` contribute module source and metadata through the fluent builder.

## Frontend Source

- `typescript/hpd-ai-studio/shell` owns the composed HPD AI Studio shell.
- `typescript/hpd-ai-studio/packages/hpd-studio-design` owns the shared design contract.
- `typescript/hpd-ai-studio/modules/*` owns the module contributions packaged by each domain `.Studio` package.

```bash
cd ../../../../typescript/hpd-ai-studio/shell
npm install
npm run build
```
