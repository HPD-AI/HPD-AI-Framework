# HPD-Agent Cerebras Provider

OpenAI-compatible chat provider for Cerebras.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithCerebras(model: "gpt-oss-120b")
    .Build();
```

Secrets:

- `cerebras:ApiKey` (CEREBRAS_API_KEY)
- `cerebras:Endpoint` (CEREBRAS_ENDPOINT, CEREBRAS_BASE_URL)

Default endpoint: `https://api.cerebras.ai/v1/`
