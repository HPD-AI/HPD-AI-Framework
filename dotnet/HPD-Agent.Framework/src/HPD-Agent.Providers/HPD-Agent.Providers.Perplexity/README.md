# HPD-Agent.Providers.Perplexity

Perplexity chat provider for HPD-Agent.

This provider uses Perplexity's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Set the API key environment variable documented for this provider, then configure an agent:

```csharp
var agent = await Agent.Create()
    .WithPerplexity()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithPerplexity(
        model: "sonar-pro",
        endpoint: "https://api.perplexity.ai/")
    .BuildAsync();
```
