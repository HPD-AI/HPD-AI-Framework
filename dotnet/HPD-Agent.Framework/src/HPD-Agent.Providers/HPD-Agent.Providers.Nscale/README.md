# HPD-Agent Nscale Provider

OpenAI-compatible chat provider for Nscale.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithNscale(model: "Qwen/Qwen3-Coder-480B-A35B-Instruct-FP8")
    .Build();
```

Secrets:

- `nscale:ApiKey` (NSCALE_API_KEY)
- `nscale:Endpoint` (NSCALE_ENDPOINT, NSCALE_BASE_URL)

Default endpoint: `https://inference.api.nscale.com/v1/`
