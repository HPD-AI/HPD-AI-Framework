# HPD-Agent Fireworks AI Provider

Chat provider for Fireworks AI using the shared OpenAI-compatible chat-completions base.

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Fireworks;

var agent = await new AgentBuilder()
    .WithFireworks(model: "accounts/fireworks/models/llama-v3p1-8b-instruct")
    .BuildAsync();
```

Set `FIREWORKS_API_KEY` or pass `apiKey` explicitly. The default endpoint is `https://api.fireworks.ai/inference/v1/`.
