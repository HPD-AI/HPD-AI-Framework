# HPD Agent Documentation Strategy

This document captures the working strategy for rebuilding the HPD Agent documentation for the `0.5.0` open-source release.

The goal is not to refresh the old docs in place. The goal is to use documentation as a beginner-facing DX test: build the examples the way a new user would, find friction, improve the library where needed, and only then document the improved experience.

## Core Principle

If something is hard to explain in the docs, first check whether the API should be easier to use.

The new documentation should be written from the outside in:

1. Start with the smallest runnable user experience.
2. Notice confusing setup, naming, defaults, or package boundaries.
3. Decide whether the problem belongs in docs or in the library.
4. Improve the library when the API is creating unnecessary friction.
5. Write docs against the improved shape.

## Reference Structure

Use `documentation/hpd-auth` as the structural reference. It is organized by reader intent instead of internal framework architecture.

Target shape:

```text
hpd-agent/
  index.md
  Getting Started/
  Core Concepts/
  Guides/
  Packages/
  API Reference/
  Cookbook/
  archive/
```

The archived `hpd-agent` docs are source material, not the source of truth.

## Section Roles

`Getting Started` should answer the first-time user questions:

- What is HPD Agent?
- What problem does it solve?
- How do I install it?
- How do I run the smallest useful agent?
- What should I read next?

`Core Concepts` should explain the mental model:

- Agents
- Messages and turns
- Sessions
- Tools
- Providers
- Events
- Middleware
- Multi-agent workflows

`Guides` should be task-oriented:

- Build a console agent
- Add tools
- Stream responses
- Maintain sessions
- Configure a provider
- Add middleware
- Build a web agent
- Build a voice agent

`Packages` should explain what to install and why.

`API Reference` should be exact, reference-oriented, and concise.

`Cookbook` should contain runnable sample files.

## Cookbook Samples

Cookbook samples should use .NET 10 file-based apps so each sample can be a single `.cs` file without a dedicated `.csproj`.

Example package directives:

```csharp
#:package HPD.Agent@*
#:package HPD.Agent.Providers.OpenAI@*
#:property TargetFramework=net10.0
```

Use `@*` for package versions in cookbook samples so standalone files remain copy-paste runnable without central package management.

Run samples with:

```bash
dotnet run --file Cookbook/01-hello-agent.cs
```

or:

```bash
dotnet Cookbook/01-hello-agent.cs
```

During local library development, cookbook files may temporarily use `#:project` directives to reference source projects directly. Public-facing samples should use `#:package` once the release shape is ready.

## DX Loop

Each cookbook sample is also an integration test for the public developer experience.

For every sample:

1. Write the simplest runnable file.
2. Run it from a clean consumer mindset.
3. Capture any friction.
4. Classify the friction as a docs issue, DX issue, or architecture issue.
5. Fix library DX before documenting around avoidable complexity.
6. Update the sample.
7. Write or update the surrounding docs.

## Subagent Workflow

HPD Agent is too comprehensive to inspect linearly. Use focused subagents to investigate slices of the system.

The main agent acts as:

- Launcher
- Director
- Context router
- Synthesizer

Subagents should receive tight context packets:

- Goal of the investigation
- Files or folders to read
- Questions to answer
- Things to ignore
- Expected output shape

Subagents should not explore the entire repository. They should return compressed findings that can be compared against the documentation and DX goals.

Useful specialist missions:

- Beginner path and public API scout
- Cookbook sample author
- Provider setup scout
- Tools and function-calling scout
- Sessions and memory scout
- Events and streaming scout
- Middleware scout
- Archived docs salvage scout
- DX critic

## Success Criteria

The new docs are successful when:

- A new user can run a useful agent quickly.
- Cookbook samples are runnable and small.
- The docs explain the mental model before exposing the full feature surface.
- Package boundaries are understandable.
- Stale archived docs are not copied forward without verification.
- Awkward examples trigger library improvements instead of explanatory workarounds.

