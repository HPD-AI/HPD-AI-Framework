# HPD-Agent.Providers.Nebius

Nebius Token Factory chat provider for HPD-Agent.

This provider uses Nebius Token Factory's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Set the API key environment variable documented for this provider, then configure an agent:

```csharp
var agent = await Agent.Create()
    .WithNebius()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithNebius(
        model: "meta-llama/Meta-Llama-3.1-70B-Instruct",
        endpoint: "https://api.tokenfactory.nebius.com/v1/")
    .BuildAsync();
```
