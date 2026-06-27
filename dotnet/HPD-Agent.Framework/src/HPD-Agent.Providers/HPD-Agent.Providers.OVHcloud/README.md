# HPD-Agent OVHcloud AI Endpoints Provider

OpenAI-compatible chat provider for OVHcloud AI Endpoints.

## Configuration

```csharp
var agent = new AgentBuilder()
    .WithOVHcloud(model: "gpt-oss-120b")
    .Build();
```

Secrets:

- `ovhcloud:ApiKey` (OVHCLOUD_API_KEY)
- `ovhcloud:Endpoint` (OVHCLOUD_ENDPOINT, OVHCLOUD_BASE_URL)

Default endpoint: `https://oai.endpoints.kepler.ai.cloud.ovh.net/v1/`
