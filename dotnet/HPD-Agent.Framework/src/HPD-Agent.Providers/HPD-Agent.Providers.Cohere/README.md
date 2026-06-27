# HPD-Agent Cohere Provider

Cohere provider package for HPD-Agent.

## Usage

```csharp
var agent = new AgentBuilder()
    .WithCohere("command-r-plus", apiKey: "your-api-key")
    .Build();
```

If `apiKey` is not supplied, HPD resolves `cohere:ApiKey` from configuration or `COHERE_API_KEY` from the environment.

## Notes

The underlying Cohere SDK implements `Microsoft.Extensions.AI.IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>` directly. Its streaming adapter returns a single final update rather than token-by-token SSE streaming.
