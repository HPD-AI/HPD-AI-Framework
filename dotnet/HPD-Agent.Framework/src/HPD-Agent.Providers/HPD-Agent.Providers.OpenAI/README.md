# HPD-Agent.Providers.OpenAI

OpenAI and Azure OpenAI provider for HPD-Agent.

## Install

```bash
dotnet add package HPD-Agent.Providers.OpenAI
```

## Use When

Use this package when you need the OpenAI model provider in HPD Agent applications.


## Codex completed responses and model policy

The experimental `openai/codex` client implements both `IChatClient` operations over streaming Responses. `GetResponseAsync` collects the same validated stream using MEAI aggregation. Failed or unterminated streams throw; incomplete responses retain an incomplete finish reason. Compaction and other specialized consumers use the ordinary chat contract.

Hosts can set `OpenAIProviderConfig.CodexModelPolicy` to an `OpenAICodexModelPolicy` containing the exact model ID, account-discovered reasoning levels, and default. Explicit Off maps to Low; unspecified effort uses the server default. Only Low, Medium, High, and ExtraHigh are implemented, and explicit requests must also satisfy the supplied catalog constraints. Unknown catalog levels are metadata, not selectable modes. A null policy defers model availability to Codex while still validating implemented request levels.

Both completed and streaming response operations are advertised as supported. `RequiresStreamingTransport` describes the wire requirement. Signing copies request-owned headers before the credential lease ends; streaming does not retain the signer buffer. SDK retries are disabled, and a new operation acquires fresh credentials.
