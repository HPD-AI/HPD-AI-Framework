# HPD-Agent Venice.ai Provider

OpenAI-compatible chat provider for Venice.ai.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithVenice(model: "venice-uncensored")
    .Build();
```

Secrets:

- `venice:ApiKey` (VENICE_API_KEY)
- `venice:Endpoint` (VENICE_ENDPOINT, VENICE_BASE_URL)

Default endpoint: `https://api.venice.ai/api/v1/`
