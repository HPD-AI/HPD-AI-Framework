# HPD-Agent.Providers.MiniMax

MiniMax chat provider for HPD-Agent.

This provider uses MiniMax's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Set the API key environment variable documented for this provider, then configure an agent:

```csharp
var agent = await Agent.Create()
    .WithMiniMax()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithMiniMax(
        model: "MiniMax-M3",
        endpoint: "https://api.minimax.io/v1/")
    .BuildAsync();
```
