# HPD-Agent DeepSeek Provider

OpenAI-compatible chat provider for DeepSeek.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithDeepSeek(model: "deepseek-v4-flash")
    .Build();
```

Secrets:

- `deepseek:ApiKey` (DEEPSEEK_API_KEY)
- `deepseek:Endpoint` (DEEPSEEK_ENDPOINT, DEEPSEEK_BASE_URL)

Default endpoint: `https://api.deepseek.com/v1/`
