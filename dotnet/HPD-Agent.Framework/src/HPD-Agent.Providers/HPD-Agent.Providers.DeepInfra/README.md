# HPD-Agent DeepInfra Provider

DeepInfra OpenAI-compatible chat provider for HPD-Agent.

## Usage

```csharp
var agent = new AgentBuilder()
    .WithDeepInfra("meta-llama/Meta-Llama-3-8B-Instruct")
    .Build();
```

If `apiKey` is not supplied, HPD resolves `deepinfra:ApiKey` from configuration or `DEEPINFRA_API_KEY` from the environment.

The default endpoint is `https://api.deepinfra.com/v1/openai/`. Set `endpoint`, `deepinfra:Endpoint`, `DEEPINFRA_ENDPOINT`, or `DEEPINFRA_BASE_URL` to use a compatible proxy.
