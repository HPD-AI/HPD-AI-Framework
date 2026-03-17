---
layout: home

hero:
  name: "HPD-Agent"
  text: "Stop Wrestling. Start Building."
  tagline: The Only .NET Agent Framework You'll Ever Need. Build production-ready AI agents in .NET without the complexity. Configure once. Scale forever. Deploy anywhere.
  image:
    src: /logo.svg
    alt: HPD-Agent
  actions:
    - theme: brand
      text: Get Started
      link: /Getting Started/00 Agents Overview
    - theme: alt
      text: View on GitHub
      link: https://github.com/HPD-AI/HPD-Agent

features:
  - icon:
      src: /icons/shield.svg
    title: Crash Recovery
    details: Checkpoints save your progress. Resume exactly where you left off after crashes or restarts.

  - icon:
      src: /icons/git-branch.svg
    title: Multi-Agent Workflows
    details: Orchestrate teams of agents. Conditional routing. Sub-agent hierarchies. Event bubbling.

  - icon:
      src: /icons/mic.svg
    title: Voice Agents
    details: Real-time voice with turn detection, interruption handling, and filler audio. Production-ready.

  - icon:
      src: /icons/radio.svg
    title: Event Streaming
    details: Real-time events. Priority channels. Bidirectional communication. Full observability.

  - icon:
      src: /icons/user-check.svg
    title: Human-in-the-Loop
    details: Pause for approval. Tool-level permissions. Security that doesn't get in the way.

  - icon:
      src: /icons/zap.svg
    title: Native AOT
    details: Native compilation. Instant startup. Minimal memory. Serverless perfection.

  - icon:
      src: /icons/layout-template.svg
    title: Structured Output
    details: Type-safe JSON extraction. Schema validation. Works across all providers.

  - icon:
      src: /icons/plug.svg
    title: Provider Agnostic
    details: 9 LLM providers - OpenAI, Anthropic, Gemini, Bedrock, Ollama, and more. One API. Zero lock-in.

  - icon:
      src: /icons/link.svg
    title: MCP Protocol
    details: Native Model Context Protocol support. Connect external tool servers instantly.

  - icon:
      src: /icons/layers.svg
    title: Extensible Middleware
    details: Hook into every step - message, iteration, function. Fully extensible pipeline architecture.

  - icon:
      src: /icons/activity.svg
    title: Full Observability
    details: 50+ event types. OpenTelemetry support. Real-time metrics. Complete visibility.

  - icon:
      src: /icons/wrench.svg
    title: Modular Toolkits
    details: Organize capabilities in C# classes. Collapsible containers reduce context usage. Type-safe and AOT-ready.
---

## Quick Start

```bash
# Install the HPD-Agent package
dotnet add package HPD-Agent

# Create your first agent
dotnet new console -n MyAgent
cd MyAgent
```

```csharp
using HPD.Agent;

var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .BuildAsync();

await agent.RunAsync("Hello! Tell me about yourself.");
```

## Why HPD-Agent?

HPD-Agent is the only .NET agentic framework you'll ever need. It's designed for production from day one:

- **Native AOT First** - Full source generation, instant startup
- **Provider Agnostic** - Never be locked into a single LLM vendor
- **Event-Driven** - Real-time streaming with 50+ event types
- **Multi-Agent Ready** - Built-in orchestration and routing
- **Production Features** - Crash recovery, permissions, middleware, observability

[Get Started →](/Getting Started/00 Agents Overview)
