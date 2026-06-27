# HPD-Agent DashScope Provider

DashScope provider for HPD-Agent using the Cnblogs DashScope Microsoft.Extensions.AI adapter.

Provider key: `dashscope`

Supported families:

- Chat
- Embeddings

Environment variable aliases for API keys:

- `DASHSCOPE_API_KEY`
- `QWEN_API_KEY`
- `DASHSCOPE_KEY`

Basic chat setup:

```csharp
using HPD.Agent;
using HPD.Agent.Providers.DashScope;

var agent = await new AgentBuilder()
    .WithDashScope(model: "qwen-plus")
    .BuildAsync();
```

Embedding setup:

```csharp
using HPD.Agent;
using HPD.Agent.Providers.DashScope;

var builder = new AgentBuilder()
    .WithDashScopeEmbeddings(model: "text-embedding-v4");
```

Configuration options include `baseAddress`, `websocketBaseAddress`, `workspaceId`, `useVl`, chat sampling defaults, and embedding dimensions.
