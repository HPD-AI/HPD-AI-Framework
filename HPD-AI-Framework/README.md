# HPD AI Framework

[![GitHub](https://img.shields.io/badge/GitHub-HPD--AI%2FHPD--AI--Framework-181717?logo=github)](https://github.com/HPD-AI/HPD-Agent-Framework)
[![Docs](https://img.shields.io/badge/Docs-hpd--ai.github.io-blue)](https://hpd-ai.github.io/HPD-Agent-Framework/)
[![NuGet](https://img.shields.io/nuget/v/HPD-Agent.Framework?label=NuGet&color=004880&logo=nuget)](https://www.nuget.org/packages/HPD-Agent.Framework)

A C# framework for building production AI Applications — AI agents, RAG pipelines, ML pipelines, authentication, and everything in between.


## HPD-Agent

Production-ready agent framework — tools, multi-turn conversations, middleware, sub-agents, multi-agent workflows, audio, and more. Paired with TypeScript/Svelte UI libraries for streaming chat interfaces.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="agent-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="agent-architecture.svg">
  <img alt="HPD-Agent Architecture" src="architecture.svg">
</picture>

---

## HPD-RAG

Fully modular RAG framework — every node in every pipeline is swappable or removable. Build your own ingestion, retrieval, and evaluation pipelines by snapping blocks together.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="rag-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="rag-architecture.svg">
  <img alt="HPD-RAG Architecture" src="rag-architecture.svg">
</picture>


---

## HPD-ML

Fully modular machine learning framework — data ingestion, feature engineering, model training, and evaluation all composable and extensible. Universal data abstraction with pluggable learners and transforms.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="ml-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="ml-architecture.svg">
  <img alt="HPD-ML Architecture" src="HPD-AI-Framework/ml-architecture.svg">
</picture>


---

## HPD-Auth

Hosted-auth-service experience as an embedded .NET library. Wraps ASP.NET Core Identity and exposes a ready-made REST API — JWT + Cookie dual-auth, session management, 2FA, passkeys, OAuth, admin API, and event-driven audit logging. No separate service to run. No per-user pricing. No data leaving your infrastructure.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="auth-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="auth-architecture.svg">
  <img alt="HPD-Auth Architecture" src="HPD-AI-Framework/auth-architecture.svg">
</picture>
