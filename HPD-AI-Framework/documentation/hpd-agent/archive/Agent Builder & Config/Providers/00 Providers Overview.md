# Providers Overview

A **provider** is the LLM backend your agent talks to. Every agent needs exactly one provider configured — either at build time, or switched per-run via `AgentRunConfig`.

## Available Providers

| Provider Key | NuGet Package | Builder Method | Notes |
|---|---|---|---|
| `openai` | `HPD-Agent.Providers.OpenAI` | `.WithOpenAI(...)` | GPT-4o, o1, audio |
| `azure-openai` | `HPD-Agent.Providers.OpenAI` | `.WithAzureOpenAI(...)` | Traditional Azure OpenAI endpoints |
| `anthropic` | `HPD-Agent.Providers.Anthropic` | `.WithAnthropic(...)` | Claude models |
| `google-ai` | `HPD-Agent.Providers.GoogleAI` | `.WithGoogleAI(...)` | Gemini models |
| `azure-ai` | `HPD-Agent.Providers.AzureAI` | `.WithAzureAI(...)` | Azure AI Foundry / Projects |
| `azure-ai-inference` | `HPD-Agent.Providers.AzureAIInference` | `.WithAzureAIInference(...)` | Azure AI Inference endpoint |
| `ollama` | `HPD-Agent.Providers.Ollama` | `.WithOllama(...)` | Local models via Ollama |
| `mistral` | `HPD-Agent.Providers.Mistral` | `.WithMistral(...)` | Mistral AI models |
| `bedrock` | `HPD-Agent.Providers.Bedrock` | `.WithBedrock(...)` | AWS Bedrock |
| `huggingface` | `HPD-Agent.Providers.HuggingFace` | `.WithHuggingFace(...)` | HuggingFace Inference API |
| `openrouter` | `HPD-Agent.Providers.OpenRouter` | `.WithProvider("openrouter", ...)` | OpenRouter (100+ models) |
| `onnx-runtime` | `HPD-Agent.Providers.OnnxRuntime` | `.WithOnnxRuntime(...)` | Local ONNX models |

---

## Two Ways to Configure a Provider

### 1. Builder methods — code-first, IntelliSense-driven

Use the provider-specific builder method when you're writing config in code. You get full IntelliSense on every option, typed validation at `BuildAsync`, and a `clientFactory` to wrap the underlying `IChatClient`.

```csharp
var agent = await new AgentBuilder()
    .WithOpenAI(
        model: "gpt-4o",
        configure: opts =>
        {
            opts.Temperature = 0.7f;
            opts.MaxOutputTokenCount = 4096;
        })
    .BuildAsync();
```

For basic setup where you don't need provider-specific options, `.WithProvider` works too:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .BuildAsync();
```

Both require the provider's NuGet package — the package's `[ModuleInitializer]` registers the provider automatically on assembly load.

### 2. JSON config — file-driven, fully serializable

Use `AgentConfig` + `ProviderOptionsJson` when the config lives outside your code — an `agent.json` file, environment config, FFI, or any scenario where you want a single file to describe the complete agent.

**`agent.json`:**
```json
{
    "name": "MyAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "openai",
        "modelName": "gpt-4o",
        "apiKey": "sk-...",
        "providerOptionsJson": "{\"temperature\":0.7,\"maxOutputTokenCount\":4096}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("agent.json");
```

`ProviderOptionsJson` is a JSON string containing the provider-specific options. The keys are **camelCase** — they match the `[JsonPropertyName]` attributes on each provider's config class. See each provider's page for the full list of keys.

---

## Switching Providers at Runtime

Both approaches above configure the **default** provider used when no override is specified. You can switch provider, model, or API key on a per-`RunAsync` basis without rebuilding the agent:

```csharp
// Default run — uses the agent's configured provider
await foreach (var evt in agent.RunAsync("Hello")) { }

// This run — switches to a different model
await foreach (var evt in agent.RunAsync("Hello", runConfig: new AgentRunConfig
{
    ProviderKey = "anthropic",
    ModelId = "claude-opus-4-6"
})) { }
```

All providers referenced at runtime must have their NuGet package referenced in the project (the `[ModuleInitializer]` registers them at load time).

→ See [Run Config](../Run%20Config.md) for all runtime override options.

---

## Provider Pages

Each provider page covers:
- Installation and authentication
- Builder method reference
- Full JSON config reference (`ProviderOptionsJson` keys)
- Provider-specific features and examples

→ [OpenAI](OpenAI.md) · [Anthropic](Anthropic.md) · [Google AI](GoogleAI.md) · [Azure AI](AzureAI.md) · [Azure OpenAI](OpenAI.md#azure-openai-support) · [Azure AI Inference](AzureAI.md) · [Ollama](Ollama.md) · [Mistral](Mistral.md) · [Bedrock](Bedrock.md) · [HuggingFace](Huggingface.md) · [ONNX Runtime](OnnxRuntime.md)
