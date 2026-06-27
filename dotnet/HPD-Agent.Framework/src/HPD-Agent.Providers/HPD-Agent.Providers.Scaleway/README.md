# HPD-Agent.Providers.Scaleway

Scaleway Generative APIs chat provider for HPD-Agent.

This provider uses Scaleway Generative APIs's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Set the API key environment variable documented for this provider, then configure an agent:

```csharp
var agent = await Agent.Create()
    .WithScaleway()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithScaleway(
        model: "qwen3.5-397b-a17b",
        endpoint: "https://api.scaleway.ai/v1/")
    .BuildAsync();
```
