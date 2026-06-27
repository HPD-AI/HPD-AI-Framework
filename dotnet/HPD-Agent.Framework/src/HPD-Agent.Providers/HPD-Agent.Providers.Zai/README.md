# HPD-Agent.Providers.Zai

Z.AI chat provider for HPD-Agent.

This provider uses Z.AI's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Set the API key environment variable documented for this provider, then configure an agent:

```csharp
var agent = await Agent.Create()
    .WithZai()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithZai(
        model: "glm-4.7",
        endpoint: "https://api.z.ai/api/paas/v4/")
    .BuildAsync();
```
