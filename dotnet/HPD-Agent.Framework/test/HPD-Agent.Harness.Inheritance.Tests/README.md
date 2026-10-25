# Harness inheritance foundation tests

These focused Roslyn tests compile and emit producer assemblies, then discover their capability
symbols from consumer compilations. They exercise the production symbol resolver through linked
source without requiring the unrelated provider/audio/runtime project graph.

Coverage includes every capability attribute kind, generic metadata, inherited virtual slots,
closed generic signatures, aliases, partial types, hidden declaration preservation, and inherited
metadata property lookup. Fixtures are compiled, not just inspected as generated text.

```sh
dotnet test test/HPD-Agent.Harness.Inheritance.Tests/HPD-Agent.Harness.Inheritance.Tests.csproj
```

These tests establish the discovery foundation. The companion `HPD-Agent.ToolHarness.Runtime.Tests`
project compiles separate producer/consumer assemblies, invokes inherited tools, checks composed
inheritance provenance and event declarations, validates inherited source collectors, and exercises
fluent skill/subagent definition admission, source pre-admission and lifecycle, coding TUI lineage,
and workflow construction/execution deadlines. External MCP/OpenAPI transport implementations and
Native AOT publication remain required integration coverage under the v10 inheritance proposal.
