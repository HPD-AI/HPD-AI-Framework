# Agent Builder & Config

Reference documentation for all build-time agent configuration — what you can customize via `AgentBuilder` (the fluent API) and `AgentConfig` (the serializable data model).

---

## Core References

| Document | Description |
|----------|-------------|
| [Agent Builder](Agent%20Builder.md) | All `.With*()` builder methods — provider, tools, middleware, observability, session, and more |
| [Agent Config](Agent%20Config.md) | `AgentConfig` properties reference — the serializable data class |
| [Run Config](Run%20Config.md) | Per-invocation overrides via `AgentRunConfig` — provider switch, chat params, tools, permissions |

---

## Config Sections

These are nested config classes on `AgentConfig`. Each has its own reference page.

| Document | Config Class | Description |
|----------|-------------|-------------|
| [Error Handling](Error%20Handling.md) | `ErrorHandlingConfig` | Retries, backoff, timeouts, error formatting |
| [History Reduction](History%20Reduction.md) | `HistoryReductionConfig` | Trim or summarize conversation history |
| [Caching](Caching.md) | `CachingConfig` | Distributed LLM response caching |
| [Collapsing](Collapsing.md) | `CollapsingConfig` | Hierarchical toolkit organization |
| [Observability](Observability.md) | `ObservabilityConfig` | Event sampling and observer circuit breaker |
| [Session Store](Session%20Store.md) | `ISessionStore` / `SessionStoreOptions` | Durable conversation persistence |
| [Sandbox Config](Sandbox%20Config.md) | `SandboxConfig` | File system, network, and process sandboxing for tool execution |

---

## Providers

| Document | Description |
|----------|-------------|
| [Providers Overview](Providers/00%20Providers%20Overview.md) | Choosing and configuring an LLM provider |
| [OpenAI](Providers/OpenAI.md) | OpenAI and Azure OpenAI |
| [Anthropic](Providers/Anthropic.md) | Claude models |
| [Ollama](Providers/Ollama.md) | Local models via Ollama |
| [Google AI](Providers/GoogleAI.md) | Gemini models |
| [Bedrock](Providers/Bedrock.md) | AWS Bedrock |
| [Azure AI](Providers/AzureAI.md) | Azure AI Inference |
| [Mistral](Providers/Mistral.md) | Mistral AI |
| [Hugging Face](Providers/Huggingface.md) | Hugging Face Inference |
| [ONNX Runtime](Providers/OnnxRuntime.md) | On-device inference |
