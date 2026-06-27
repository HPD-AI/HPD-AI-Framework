# HPD-Agent.Providers.NvidiaNim

NVIDIA NIM chat provider for HPD-Agent.

This provider uses NVIDIA NIM's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Set the API key environment variable documented for this provider, then configure an agent:

```csharp
var agent = await Agent.Create()
    .WithNvidiaNim()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithNvidiaNim(
        model: "meta/llama-3.1-70b-instruct",
        endpoint: "https://integrate.api.nvidia.com/v1/")
    .BuildAsync();
```
