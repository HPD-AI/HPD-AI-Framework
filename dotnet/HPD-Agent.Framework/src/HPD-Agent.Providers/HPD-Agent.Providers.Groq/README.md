# HPD-Agent Groq Provider

Groq chat provider for HPD-Agent using Groq's OpenAI-compatible chat completions API.

```csharp
var agent = new AgentBuilder()
    .WithGroq(model: "llama-3.3-70b-versatile")
    .Build();
```

Set `GROQ_API_KEY` or pass `apiKey` explicitly.
