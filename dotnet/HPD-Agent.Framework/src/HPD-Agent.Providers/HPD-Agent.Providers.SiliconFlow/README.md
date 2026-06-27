# HPD-Agent.Providers.SiliconFlow

SiliconFlow chat provider for HPD-Agent.

This provider uses SiliconFlow's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Set the API key environment variable documented for this provider, then configure an agent:

```csharp
var agent = await Agent.Create()
    .WithSiliconFlow()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithSiliconFlow(
        model: "Qwen/Qwen3-32B",
        endpoint: "https://api.siliconflow.com/v1/")
    .BuildAsync();
```
