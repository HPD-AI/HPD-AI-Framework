# HPD-Agent SambaNova Provider

OpenAI-compatible chat provider for SambaNova.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithSambaNova(model: "Meta-Llama-3.3-70B-Instruct")
    .Build();
```

Secrets:

- `sambanova:ApiKey` (SAMBANOVA_API_KEY)
- `sambanova:Endpoint` (SAMBANOVA_ENDPOINT, SAMBANOVA_BASE_URL)

Default endpoint: `https://api.sambanova.ai/v1/`
