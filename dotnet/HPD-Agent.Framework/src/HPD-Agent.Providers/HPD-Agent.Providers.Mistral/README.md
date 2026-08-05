# HPD-Agent.Providers.Mistral

Mistral provider for HPD-Agent using the generated Mistral SDK and Microsoft.Extensions.AI.

## Install

```bash
dotnet add package HPD-Agent.Providers.Mistral
```

## Use When

Use this package when you need the Mistral model provider in HPD Agent applications.

Runtime model-call behavior is configured through `ChatClientConfig`. Mistral-only per-request fields are configured with `MistralChatRequestOptions`.
