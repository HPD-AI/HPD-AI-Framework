# HPD-Agent.Providers.LMStudio

LM Studio chat provider for HPD-Agent.

This provider uses LM Studio's OpenAI-compatible Chat Completions API through the shared `HPD-Agent.Providers.OpenAICompatible` implementation.

## Setup

Start the LM Studio local server, then configure an agent. An API key is not required for the default local endpoint:

```csharp
var agent = await Agent.Create()
    .WithLMStudio()
    .BuildAsync();
```

Override the model or endpoint when needed:

```csharp
var agent = await Agent.Create()
    .WithLMStudio(
        model: "local-model",
        endpoint: "http://localhost:1234/v1/")
    .BuildAsync();
```
