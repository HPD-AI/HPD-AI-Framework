# HPD-Agent.Providers.Moonshot

Moonshot/Kimi chat provider for HPD-Agent using the shared OpenAI-compatible chat-completions client.

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Moonshot;

var agent = await new AgentBuilder()
    .WithMoonshot(model: "kimi-k2.5")
    .BuildAsync();
```

Set `MOONSHOT_API_KEY` or `KIMI_API_KEY` for credential resolution. The default endpoint is `https://api.moonshot.ai/v1/`.
