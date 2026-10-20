# HPD-Agent 0.9.0

## Targets

- Supported modern targets: `net8.0`, `net9.0`, and `net10.0`.
- Legacy `netstandard2.0` and `net472` support remains disabled.

## Highlights

- Generated event compositions are authoritative, with library modules composed
  automatically when no application composition is supplied.
- Generic-context capabilities and cross-assembly ToolHarness registries now
  generate and activate correctly.
- Local environment provider integration includes reviewed executable lifecycle
  and container-host readiness.
- Multi-agent workflow references, stream ownership, and target-specific
  provider test matrices are aligned.
- Large Markdown literals use exact-source fast paths and preserve transformed
  source mappings.

## Dependency Changes

- Migrated OpenAPI support to `Microsoft.OpenApi` and
  `Microsoft.OpenApi.YamlReader` 3.11.0.
- Updated the `Microsoft.Extensions.*` platform family to 10.0.13.
- Updated the `Microsoft.Extensions.AI` family to 10.11.0.
- Updated Microsoft.Agents, CodeAnalysis, Graph, OpenAI, Anthropic, AWS runtime,
  DashScope, and the low-risk provider/tooling package families.
- Updated ONNX Runtime, ONNX Runtime GenAI, PDFium, NLayer, LiveKit, and Markdig.
- Replaced deprecated `AWSSDK.Extensions.Bedrock.MEAI` with `AWS.Bedrock.MEAI`
  1.1.1.

## Upgrade Notes

- OpenAPI consumers must use the OpenAPI.NET v3 model namespace and
  `IOpenApiMediaType` content contracts.
- `Microsoft.Extensions.AI.Evaluation.NLP` remains a preview package because no
  stable release exists; 0.9.0 uses `10.11.0-preview.1.26513.7`.
- `Azure.AI.OpenAI` remains on `2.9.0-beta.1`, which is newer than the latest
  stable package and is an explicit exception.
- LiveKit `0.1.4` retains the `livekit-ffi/v0.12.60` archives, but all six RID
  library hashes were re-qualified.

## Package Coverage

- Bot packages, FileSystem ToolHarness, and LiveKit native assets now ship with
  the Agent release workflow.
- `HPD-Agent.MultiAgent.AspNetCore` remains blocked until
  `HPD-Base.AspNetCore` and `HPD-Graph.Base` are published.
- `HPD-Agent.Studio` remains on its separate Studio release path.

## Validation

- Full net10 Agent solution: green.
- Supported net8 and net9 project sets: green.
- HPD-RAG, HPD-TUI, and PDF extraction suites: green.
- Vulnerability audit: zero known vulnerable packages.
- Release package build: Agent and foundation packages produced as 0.9.0.
