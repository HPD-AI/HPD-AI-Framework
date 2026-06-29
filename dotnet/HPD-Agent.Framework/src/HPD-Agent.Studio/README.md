# HPD-Agent.Studio

HPD Agent Studio module contribution for `HPD-AI.Studio`.

## Use

Use `HPD-AI.Studio` to host the composed Studio shell. This package carries only the Agent module source and metadata.

```csharp
builder.Services.AddHPDAIStudio()
    .AddAgentStudio();
```

## Frontend Source

The reusable Svelte source lives in the repository TypeScript area.

- `typescript/hpd-ai-studio/modules/hpd-agent-studio` owns the HPD Agent Studio module packaged here.
