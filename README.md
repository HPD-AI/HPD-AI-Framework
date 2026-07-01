# HPD AI Framework

[![GitHub](https://img.shields.io/badge/GitHub-HPD--AI%2FHPD--AI--Framework-181717?logo=github)](https://github.com/HPD-AI/HPD-AI-Framework)
[![NuGet](https://img.shields.io/nuget/v/HPD-Agent.Framework?label=NuGet&color=004880&logo=nuget)](https://www.nuget.org/packages/HPD-Agent.Framework)

A set of C# frameworks for building production AI applications. Use the package family that matches the thing you need to build: agents, RAG, graph workflows, terminal UIs, ML pipelines, authentication.

Product documentation, websites, and opinionated product layers live in their own repositories. Use the links under each architecture diagram for the canonical source and published docs when available.

## HPD AI Platform

HPD AI Platform is the all-in-one enterprise product layer over these frameworks. It assembles backend services, agents, RAG, graph workflows, auth, storage, evaluations, environments, Studio, and SDK access into one governed product surface.

[GitHub](https://github.com/HPD-AI/HPD-AI-Platform)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/svg/overview-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/svg/overview.svg">
  <img alt="HPD AI Framework Packages" src="assets/svg/overview.svg">
</picture>


## HPD-Agent

Use HPD-Agent to build production agents that can talk to models, call tools, stream events, manage sessions, hand work to sub-agents, and expose chat surfaces.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/svg/agent-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/svg/agent-architecture.svg">
  <img alt="HPD-Agent Architecture" src="assets/svg/agent-architecture.svg">
</picture>

[GitHub](https://github.com/HPD-AI/HPD-Agent) · [Documentation](https://hpd-ai.github.io/HPD-Agent-Framework/)

---

## HPD-RAG

Use HPD-RAG to build retrieval systems where ingestion, storage, search, reranking, formatting, and evaluation can be replaced or removed independently.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/svg/rag-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/svg/rag-architecture.svg">
  <img alt="HPD-RAG Architecture" src="assets/svg/rag-architecture.svg">
</picture>

[GitHub](https://github.com/HPD-AI/HPD-RAG-Framework)

---

## HPD-Graph

Use HPD-Graph to run typed workflow graphs that need routing, parallel layers, checkpoint/resume, human or external waits, artifacts, partitions, and incremental execution.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/svg/graph-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/svg/graph-architecture.svg">
  <img alt="HPD-Graph Architecture" src="assets/svg/graph-architecture.svg">
</picture>

[GitHub](https://github.com/HPD-AI/HPD-Graph-Framework)

---

## HPD-TUI

Use HPD-TUI to build native AOT-friendly terminal interfaces that stay allocation-conscious while rendering retained views, prompts, trees, tables, markdown, and streaming output.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/svg/tui-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/svg/tui-architecture.svg">
  <img alt="HPD-TUI Architecture" src="assets/svg/tui-architecture.svg">
</picture>

[GitHub](https://github.com/HPD-AI/HPD-TUI-Framework)

---

## HPD-ML

Use HPD-ML to build machine-learning pipelines with a common data abstraction, composable transforms, pluggable learners, evaluation, and model serialization.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/svg/ml-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/svg/ml-architecture.svg">
  <img alt="HPD-ML Architecture" src="assets/svg/ml-architecture.svg">
</picture>

[GitHub](https://github.com/HPD-AI/HPD-ML-Framework) · [Documentation](https://hpd-ai.github.io/HPD-ML-Framework/)

---

## HPD-Auth

Use HPD-Auth when you want hosted-auth-service ergonomics inside your own ASP.NET app: identity, sessions, JWT/cookie auth, 2FA, passkeys, OAuth, admin APIs, and audit events without a separate service.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/svg/auth-architecture-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/svg/auth-architecture.svg">
  <img alt="HPD-Auth Architecture" src="assets/svg/auth-architecture.svg">
</picture>

[GitHub](https://github.com/HPD-AI/HPD-Auth-Framework) · [Documentation](https://hpd-ai.github.io/HPD-Auth-Framework/)


## HPD-AI Use Discretion

HPD-AI Framework is pre-1.0. Until `1.0.0`, API and persistence contracts may continue to evolve as the framework stabilizes. 
