# HPD-Agent Together AI Provider

Together AI provider package for HPD-Agent. Supports chat completions and embeddings through the Together SDK's Microsoft.Extensions.AI adapters.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithTogether(
        model: "meta-llama/Llama-3.3-70B-Instruct-Turbo",
        configure: options =>
        {
            options.Temperature = 0.7;
            options.MaxOutputTokens = 1024;
        });
```

Set `TOGETHER_API_KEY` or pass `apiKey` explicitly.
