# HPD-Agent Hyperbolic Provider

OpenAI-compatible chat provider for Hyperbolic.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithHyperbolic(model: "Qwen/Qwen2.5-72B-Instruct")
    .Build();
```

Secrets:

- `hyperbolic:ApiKey` (HYPERBOLIC_API_KEY)
- `hyperbolic:Endpoint` (HYPERBOLIC_ENDPOINT, HYPERBOLIC_BASE_URL)

Default endpoint: `https://api.hyperbolic.xyz/v1/`
