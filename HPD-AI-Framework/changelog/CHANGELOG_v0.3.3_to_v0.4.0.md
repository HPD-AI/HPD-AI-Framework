# HPD-Agent Framework Changelog: v0.3.3 → v0.4.0

## Overview

This release encompasses **32 commits** and represents a major evolution of the HPD-Agent Framework, introducing substantial new capabilities, architectural improvements, and framework restructuring. The primary themes include:

- **Agent Lifecycle Management Refactor**: Separation of agent and session management concerns
- **Evaluation & Analytics Framework**: Comprehensive evaluation scoring and result tracking
- **RAG (Retrieval-Augmented Generation) System**: Modular retrieval and ingestion pipeline
- **ML Framework Integration**: New HPD-ML machine learning module stack
- **Middleware & ToolHarness Scoping**: Advanced middleware composition and toolharness-scoped execution
- **Slack Socket Mode**: Real-time WebSocket support for Slack bots
- **Adapter → Bot Terminology**: Alignment with modern bot/chatbot nomenclature
- **Project Restructuring**: Repository reorganization under HPD-AI-Framework

**Note**: This release contains several breaking changes, particularly around namespace reorganization, architecture refactoring, and API changes. See detailed sections below.

---

## Major Feature Additions

### 1. Agent Management Refactor (Commit: 8d3da80)

**Status**: BREAKING CHANGES

#### What Was Added

The monolithic `AgentSessionManager` has been split into two distinct responsibilities:

- **`AgentManager`** (new): Manages agent lifecycle, storage, and metadata
  - Handles agent CRUD operations via `/agents` REST endpoints
  - Supports pluggable agent stores: `InMemoryAgentStore`, `JsonAgentStore`
  - Tracks running agent instances and orchestration
  - Manages `StoredAgent` with metadata and configuration persistence

- **`SessionManager`** (new): Manages conversation sessions and branch state
  - Replaced `AgentSessionManager` responsibility for session handling
  - Maintains session-specific conversation branches and state
  - Handles session lifecycle (creation, deletion, cleanup)

#### New APIs

**Agent Management**:
```csharp
// New interfaces
public interface IAgentStore
{
    Task<StoredAgent?> GetAgentAsync(string agentId);
    Task<IEnumerable<StoredAgent>> ListAgentsAsync();
    Task SaveAgentAsync(StoredAgent agent);
    Task DeleteAgentAsync(string agentId);
}

// Implementations
public class InMemoryAgentStore : IAgentStore { }
public class JsonAgentStore : IAgentStore { }

// Agent DTO
public class StoredAgent
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required AgentConfig Config { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

// Registry
public class HPDAgentRegistry
{
    public void RegisterAgentStore<T>(IAgentStore store);
}
```

**AspNetCore Integration**:
```csharp
public class AspNetCoreAgentManager : AgentManager
{
    // Lifecycle management for ASP.NET Core hosted agents
}

public class AspNetCoreSessionManager : SessionManager
{
    // Session management for ASP.NET Core environments
}
```

**New Endpoints**:
- `POST /agents` - Create agent
- `GET /agents` - List agents
- `GET /agents/{id}` - Get agent details
- `PUT /agents/{id}` - Update agent
- `DELETE /agents/{id}` - Delete agent
- `POST /agents/{id}/run` - Execute agent

#### Breaking Changes

1. **`AgentSessionManager` is removed** - Code relying on this must migrate to separate `AgentManager` + `SessionManager`
2. **Dependency injection registration changed**:
   ```csharp
   // Old
   services.AddAgentSessionManager<T>();

   // New
   services.AddAgentManager<T>();
   services.AddSessionManager<T>();
   ```
3. **MAUI and Hosting implementations split** - `MauiAgentManager` and `AspNetCoreAgentManager` now handle agent lifecycle separately
4. **Test infrastructure updated** - `TestAgentManager` and `TestSessionManager` replace unified test doubles

#### Dependencies & Infrastructure

- New `HPDAgentRegistry` for centralized agent registration
- `.claude/settings.json` updated with agent configuration
- Expanded `HPD-Agent.AspNetCore.csproj` with new agent management types
- New test projects: `AgentEndpointsTests`, `AspNetCoreAgentManagerTests`

#### Impact on Existing Code

- **Migration Required**: Applications using `AgentSessionManager` must refactor to use `AgentManager` and `SessionManager`
- **Test Updates**: Existing tests using agent managers must be rewritten with split architecture
- **Configuration**: Agent registration and configuration flows have changed

---

### 2. Evaluation Framework & Agent Hierarchy (Commits: 8d3da80, 8e24250)

**Status**: NEW FEATURE

#### What Was Added

A comprehensive evaluation and scoring system for agent execution results:

**Event Types**:
```csharp
// New evaluation event types
public record EvaluationStartedEvent(...) : AgentEvent;
public record EvaluationResultEvent(
    string EvaluationId,
    double Score,
    Dictionary<string, object> Metrics,
    string? Feedback) : AgentEvent;

// New operation status tracking
public readonly struct OperationStatus : IEquatable<OperationStatus>
{
    public static OperationStatus Queued { get; }
    public static OperationStatus InProgress { get; }
    public static OperationStatus Completed { get; }
    public static OperationStatus Failed { get; }
}

// Agent execution context for hierarchical tracking
public record AgentExecutionContext
{
    public required string AgentName { get; init; }
    public required string AgentId { get; init; }
    public string? ParentAgentId { get; init; }
    public IReadOnlyList<string> AgentChain { get; init; }
    public int Depth { get; init; }
    public bool IsSubAgent => Depth > 0;
}
```

**New REST Endpoints**:
- `POST /evals` - Start evaluation
- `GET /evals/{id}` - Get evaluation result
- `POST /evals/{id}/score` - Submit evaluation score via `WriteScoreRequest`
- `GET /evals` - List evaluations with analytics

**New Scoring Infrastructure**:
```csharp
public class InMemoryScoreStore : IScoreStore
{
    Task RecordScoreAsync(EvaluationResult result);
    Task<EvaluationAnalytics> GetAnalyticsAsync();
}
```

#### New APIs

- `AgentExecutionContext` for multi-agent event attribution
- `InterruptionRequestEvent` with `InterruptionSource` enum
- `WriteScoreRequest` DTO for evaluation scoring
- Enhanced `EvalEndpoints` with comprehensive test coverage (571 lines of tests)

#### Breaking Changes

1. **Event Serialization**: Exception objects now marked with `[JsonIgnore]` - events no longer include exception data in serialized form
2. **Error Event Constructor**: Changed from parameter to property
   ```csharp
   // Old
   new ErrorEvent(message, exception);

   // New
   new ErrorEvent { ErrorMessage = message, Exception = exception };
   ```

#### Impact on Existing Code

- Event consumers expecting exception data in JSON responses must adapt to `[JsonIgnore]` attributes
- Error event construction patterns must be updated
- Event handlers must account for `AgentExecutionContext` in multi-agent scenarios

---

### 3. RAG (Retrieval-Augmented Generation) Framework (Commit: a1849b9)

**Status**: NEW FRAMEWORK, MAJOR REFACTOR

#### What Was Added

A complete Retrieval-Augmented Generation subsystem with modular architecture:

**Core Modules**:
- **`HPD-RAG.Framework`**: Central RAG orchestration
- **`HPD-RAG.Core`**: Base abstractions and interfaces
- **`HPD-RAG.Ingestion`**: Document/data ingestion pipeline
- **`HPD-RAG.Retrieval`**: Query and vector search
- **`HPD-RAG.Evaluation`**: RAG quality metrics and analysis
- **`HPD-RAG.Pipeline`**: Composition and execution engine
- **Extensions**:
  - **6 Embedding Providers**: Anthropic, OpenAI, Azure, Mistral, HuggingFace, Local
  - **4 Reranker Providers**: Cohere, CrossEncoder, LLM-based, BGE
  - **12 Vector Store Backends**: Pinecone, Weaviate, Chroma, Milvus, SQLiteVec, Redis, PgVector, Qdrant, MongoDB Atlas, Elasticsearch, CosmosMongoDB, Azure Cognitive Search
  - **Graph Store Support**: Neptune, Memgraph, TigerGraph providers

#### Architecture

```csharp
// RAG Pipeline composition
var ragPipeline = new RagPipeline(
    ingestionStage: embeddingProvider,
    retrievalStage: vectorStore,
    rerankingStage: rerankerProvider,
    evaluationStage: metricsCollector
);

// Multi-stage execution
var results = await ragPipeline.RetrieveAsync(query, topK: 5);
var scored = await ragPipeline.RerankAsync(results);
var evaluated = await ragPipeline.EvaluateAsync(scored);
```

#### New APIs & Providers

**Embedding Providers**:
- `AnthropicEmbeddingProvider`
- `OpenAIEmbeddingProvider`
- `AzureOpenAIEmbeddingProvider`
- `MistralEmbeddingProvider`
- `HuggingFaceEmbeddingProvider`
- `LocalEmbeddingProvider`

**Reranker Providers**:
- `CohereRerankProvider`
- `CrossEncoderRerankProvider`
- `LlmRerankProvider`
- `BgeRerankProvider`

**Vector Store Backends** (12 providers across the ecosystem)

#### Breaking Changes

1. **Project Structure Reorganization** (Major):
   - `HPD-Events`, `HPD-Graph`, `HPD.OpenApi.Core` **moved from root to `dotnet/src/shared/`**
   - Solution file changed: `HPD-Agent.slnx` → `HPD-AI.slnx`
   - All test projects renamed: `HPD.*.Tests` → `HPD-*.Tests` (e.g., `HPD.Events.Tests` → `HPD-Events.Tests`)

2. **Namespace Changes**:
   - `HPD-Events` namespace preserved but relocated
   - `HPD.Graph` namespace preserved but relocated
   - All RAG types in new `HPD.RAG.*` namespaces

3. **Roslyn 5.0 / Incremental Generators**:
   - `ISourceGenerator` → `IIncrementalGenerator` for:
     - `DIRegistrationGenerator`
     - `SocketBridgeGenerator`
     - `HPDToolSourceGenerator`
   - Old interface implementations no longer work

4. **NuGet Dependency Upgrades** (Potential Breaking):
   - `ModelContextProtocol 1.0.0` (stable): `IMcpClient`/`McpClientFactory` → `McpClient` + `McpClient.CreateAsync`
   - `Microsoft.OpenApi 2.0`: New interface-based model (`IOpenApiSchema`, `IOpenApiParameter`), `OpenApiDocument.LoadAsync`, optional `JsonSchemaType?`
   - `OpenAI 2.9.1`: Significant API updates
   - `FluentAssertions 8`: Renamed assertions (`BeGreaterThanOrEqualTo`, `BeLessThanOrEqualTo`)
   - `xunit 2.9.3`

5. **Solution File Changes**:
   - Renamed `HPD-Agent.slnx` → `HPD-AI.slnx`
   - New folders `HPD-RAG.Framework`, `HPD-RAG.Extensions`
   - Updated all project references

#### Slack Socket Mode Support

New WebSocket-based adapter capabilities:

**New Base Class**:
```csharp
public abstract class AdapterWebSocketService
{
    protected virtual TimeSpan ReconnectDelay { get; }
    protected virtual TimeSpan MaxBackoff { get; }

    protected abstract Task OnConnectedAsync();
    protected abstract Task OnMessageAsync(WebSocketMessage msg);
    protected abstract Task OnDisconnectedAsync(Exception? error);

    public abstract Task SendAsync(WebSocketMessage msg);
}
```

**Slack Socket Mode**:
```csharp
public class SlackSocketModeService : AdapterWebSocketService
{
    public SlackSocketModeClient Client { get; }
}

public class SlackSocketModeClient
{
    public Task<SlackSocketEnvelope> ReceiveAsync();
    public Task SendAsync(SlackSocketEnvelope envelope);
}
```

**Registration Attribute**:
```csharp
[HpdSocketTransport("slack-socket-mode")]
public class SlackSocketModeService : AdapterWebSocketService { }
```

**New Slack Events**:
- `app_home_opened` → `SlackAppHomeOpenedPayload`
- Slash commands → `SlackSlashCommandPayload`
- Block actions → `SlackBlockActionsPayload`
- Reactions → `SlackReactionEvent`
- Message reactions
- View submissions/closures

#### ToolHarness-Scoped Middleware

New middleware scoping mechanism that activates per-toolharness:

```csharp
[Collapse(Middlewares = [typeof(CustomMiddleware)])]
public class MyToolHarness : IToolHarness
{
    [AIFunction]
    public string MyTool() { }
}
```

**Features**:
- Per-toolharness middleware pipelines
- Activation at expansion time
- Dual constructor support: parameterless + config-based
- `IToolHarnessMiddleware` marker interface
- `ToolHarnessOptions` for builder-time DI registration

**Refactored Components**:
- `ContainerMiddleware`: Now manages unified toolharness + skill collapsing with dual context (FunctionResult ephemeral + SystemPrompt persistent)
- `AgentMiddlewarePipeline`: New dual Execute/Dispatch pattern with reverse-order After* hooks and error aggregation
- `ToolHarnessFactory`: Enhanced metadata deserialization and toolharness-scoped middleware factory delegates

#### Impact on Existing Code

- **Large-scale migration**: Projects using Roslyn source generators must migrate to incremental generators
- **NuGet updates**: Dependency updates may require API adjustments
- **Project structure**: References to relocated `HPD-Events`, `HPD-Graph`, `HPD.OpenApi.Core` must be updated
- **MCP integration**: Update to new `McpClient` API if using MCP servers
- **Middleware composition**: ToolHarness-scoped middleware provides new patterns and may supersede old patterns

---

### 4. HPD AI Framework Rebrand & UI Overhaul (Commit: 1dfd7a1)

**Status**: MAJOR UI/UX REFACTOR + BRANDING

#### What Was Added

**Branding & Documentation**:
- Project rebranded from "HPD-Agent" to "HPD AI Framework"
- New SVG architecture diagrams: `overview.svg`, `overview-dark.svg`, `rag-architecture.svg`
- Updated README with HPD AI Framework positioning

**Branch Navigation**:
- New branch sibling navigation in `BranchEndpoints.cs`
- `GET /sessions/{sessionId}/branches/{branchId}/siblings/next`
- `GET /sessions/{sessionId}/branches/{branchId}/siblings/prev`

**Headless UI Reactivity Overhaul** (TypeScript/Svelte):

Major refactoring of component reactivity and state management:

- **Message List** (`message-list.svelte.ts`):
  - New reactive ownership model
  - Improved message synchronization
  - Enhanced test coverage (238 new lines)
  - `message-reactive-owner-toolharness.svelte` and tests

- **Message Actions** (`message-actions.svelte.ts`):
  - Refactored state machine (160 lines modified)
  - New test coverage for edit, navigation, retry patterns
  - Type improvements: `MessageActionsType` enum

- **Chat Input Components**:
  - Improved input handling in `chat-input-leading.svelte`, `chat-input-trailing.svelte`
  - Leading/trailing slot redesign with 17 lines modification each

- **Permission Dialog**:
  - Enhanced dialog actions and styling
  - Improved header layout

- **Run Config**:
  - New browser-based tests
  - Improved configuration UI components
  - Model selector, temperature, max tokens, timeout settings

- **File Attachment**:
  - New file attachment state management
  - Type improvements and exports
  - Integration with headless UI module

#### New APIs

**Branch Navigation**:
```csharp
// GET /sessions/{sessionId}/branches/{branchId}/siblings/next
// GET /sessions/{sessionId}/branches/{branchId}/siblings/prev
public record BranchSiblingResponse
{
    public string? NextBranchId { get; set; }
    public string? PreviousBranchId { get; set; }
}
```

**TypeScript/Svelte Component Types**:
- `message-actions.svelte.ts` exports enhanced type definitions
- `message-list.svelte.ts` with reactive owner tracking
- `run-config` components with type safety

#### Breaking Changes

1. **Component API Changes**:
   - Message list reactive model changed (may affect custom implementations)
   - Message actions state structure revised
   - Permission dialog component signature updated

2. **TypeScript Type Exports**:
   - Type reorganization in `hpd-agent-client` types

#### Impact on Existing Code

- **Frontend applications**: Must update component usage for message list and message actions
- **Custom UI implementations**: May need adaptation to new reactive patterns
- **Type definitions**: Applications relying on old message/action types need updates

---

### 5. HPD-ML Framework (Commit: be8313b)

**Status**: NEW FRAMEWORK

#### What Was Added

A complete machine learning framework with data handling, algorithms, and model management:

**Core Modules**:
- **`HPD.ML.Abstractions`**: Base interfaces and contracts
  - `IDataHandle`, `IRow`, `IRowCursor` for data abstraction
  - `ILearnedParameters`, `ILearner`, `IModel` for ML contracts
  - `ISchema`, `IColumn`, `IFieldType` for data schema
  - `ITransform`, `IScanTransform`, `IGeneratorTransform` for transformations
  - `ISerializer` for model persistence
  - `IExecutionEnvironment` for execution context

- **`HPD.ML.Core`**: Core implementations
  - Data handles: `InMemoryDataHandle`, `CachedDataHandle`, `FilteredDataHandle`, `SplitDataHandle`
  - Transforms: `ColumnCopyTransform`, `ColumnRenameTransform`, `ColumnSelectTransform`, `ComposedTransform`
  - Schema builder and utilities

- **`HPD.ML.DataSources`**: Data I/O
  - CSV support: `CsvDataHandle`, `CsvCursor`, `CsvWriter`, `CsvOptions`
  - InMemory sources: `DictionaryDataHandle`, `EnumerableDataHandle`
  - Extensions for fluent API

- **`HPD.ML.BinaryClassification`**: Binary classification learners
  - Learners: `AveragedPerceptronLearner`, `LinearSvmLearner`, `LogisticRegressionLearner`, `SdcaLearner`
  - Optimizers: `LbfgsOptimizer`, `SgdOptimizer`
  - Transforms: `LinearScoringTransform`, `CalibratorTransform`
  - Parameters: `LinearModelParameters`

- **`HPD.ML.Clustering`**: Clustering algorithms
  - Learners: `KMeansLearner`, `MiniBatchKMeansLearner`
  - Initialization: `KMeansPlusPlusInit`, `KMeansParallelInit`, `RandomInit`
  - Output: `ClusteringModelParameters`, `ClusteringScoringTransform`

#### New APIs

```csharp
// Data abstraction
public interface IDataHandle
{
    IRowCursor GetCursor();
    Task<IDataHandle> TransformAsync(ITransform transform);
}

// Learning pipeline
public interface ILearner
{
    Task<IModel> TrainAsync(
        IDataHandle trainingData,
        LearnerInput input,
        CancellationToken cancellationToken = default);
}

// Model inference
public interface IModel
{
    Task<IDataHandle> ScoreAsync(IDataHandle data);
    Task SaveAsync(Stream stream, ISerializationFormat format);
}

// Schema definition
public class SchemaBuilder
{
    public SchemaBuilder AddColumn(string name, IFieldType type);
    public ISchema Build();
}
```

#### Breaking Changes

None directly, as this is a new module. However:

1. Depends on new NuGet packages (ML-related)
2. Requires .NET 10.0+ for latest features

#### Impact on Existing Code

- New optional module - no impact on existing code unless explicitly integrated
- ML-based agents can now leverage this framework for feature engineering and model training

---

### 6. HPD-Graph Incremental Execution & Optimizations (Commit: 54569df)

**Status**: PERFORMANCE & BUG FIXES

#### What Was Added

**Performance Optimizations**:
- Fast-path optimization for unchanged graphs with no inputs (skip full execution)
- Switched fingerprint hashing from SHA256 to **XxHash64** (non-cryptographic, cache-optimized)
- Skip snapshot saves when no nodes executed (reduces I/O)

**Bug Fixes**:
- Fixed concurrent modification in context snapshot merging
- Preserve producing nodes in minimal graph when affected nodes list is empty

**Documentation & Structure**:
- Reorganized documentation into topic-specific modules
- Migrated HPD-ML Framework to corrected directory path
- Added HPD-Auth.Framework scaffolding

#### New APIs

**Graph Execution**:
```csharp
// XxHash64 fingerprinting (replaces SHA256)
public class GraphFingerprint
{
    public static ulong ComputeXxHash64(GraphDefinition graph);
}

// Optimization: fast path for unchanged graphs
public class GraphExecutor
{
    // Skips full execution if graph unchanged and no inputs
    public async Task<ExecutionResult> ExecuteOptimizedAsync(GraphDefinition graph);
}
```

#### Breaking Changes

None for public APIs. Internal optimizations:
- Fingerprint format changed (requires cache invalidation)
- Snapshot storage optimization (may affect debugging)

#### Impact on Existing Code

- **Performance improvement**: Graphs with no changes execute significantly faster
- **Cache invalidation**: Graph caches from v0.3.3 should be cleared
- **No code changes required** for typical usage

---

## Supporting Infrastructure Changes

### 7. Rhodium Framework Addition (Commit: 74e0935)

**Status**: NEW FRAMEWORK

Added comprehensive trading and quantitative analysis framework:

**Modules**:
- **`Rhodium.Analytics`**: Backtesting metrics, tear sheets, round-trip analysis
- **`Rhodium.Connectivity`**: Exchange connectors, simulation, fill models, latency modeling
- **`Rhodium.Control`**: Engine loop, state transitions, world state
- **`Rhodium.Data`**: Bar/Renko aggregators, market data utilities

**New Components**:
```csharp
// Analytics
public class TearSheet { /* Comprehensive performance metrics */ }
public class BatchTearSheetBuilder { /* Build from multiple round trips */ }
public class BacktestMetrics { /* Backtest performance */}

// Connectivity
public interface IConnector { /* Exchange abstraction */ }
public class ReplayConnector { /* Historical data replay */ }
public class SimulationConfig { /* Backtesting configuration */ }

// Control
public class EngineLoop { /* Main event loop */ }
public class StateTransitions { /* State management */ }
```

#### Impact

- Optional framework for trading/quant applications
- No impact on existing HPD-Agent code

---

### 8. Adapter → Bot Refactoring (Commit: 689a714)

**Status**: NAMING & TERMINOLOGY ALIGNMENT

#### What Was Changed

Renamed the adapter framework to "bots" to align with modern bot/chatbot terminology across the framework:

**Project Renames**:
- `HPD-Agent.Adapters` → `HPD-Agent.Bots`
- `HPD-Agent.Adapters.Abstractions` → `HPD-Agent.Bots.Abstractions`
- `HPD-Agent.Adapters.AspNetCore` → `HPD-Agent.Bots.AspNetCore`
- `HPD-Agent.Adapters.Slack` → `HPD-Agent.Bots.Slack`
- `HPD-Agent.Adapters.SourceGenerator` → `HPD-Agent.Bots.SourceGenerator`
- `HPD-Agent.Adapters.Tests` → `HPD-Agent.Bots.Tests`

**Namespace Changes**:
- `HPD.Agent.Adapters` → `HPD.Agent.Bots`
- All internal namespaces updated accordingly

**File Renames** (Slack implementation):
- `SlackAdapter.cs` → `SlackBot.cs`
- `SlackAdapterConfig.cs` → `SlackBotConfig.cs`
- `SlackAdapterServiceCollectionExtensions.cs` → `SlackBotServiceCollectionExtensions.cs`

**Migration Path**:
```csharp
// Old
using HPD.Agent.Adapters.Slack;
builder.Services.AddSlackAdapter(config);

// New
using HPD.Agent.Bots.Slack;
builder.Services.AddSlackBot(config);
```

#### Breaking Changes

1. **Namespace imports** - All `using HPD.Agent.Adapters.*` statements must change to `using HPD.Agent.Bots.*`
2. **Project references** - All `.csproj` files referencing `HPD-Agent.Adapters` projects must update paths
3. **Extension method calls** - `AddSlackAdapter()` → `AddSlackBot()`, etc.

#### Impact

- **Scope**: Framework-wide terminology change affecting all bot/adapter code
- **Compatibility**: Not backward compatible; migration required
- **Benefit**: Clearer semantic meaning aligned with "bot" nomenclature in AI/LLM space

---

### 9. Repository Reorganization (Commit: 936a66a)

**Status**: MAJOR STRUCTURAL CHANGE

#### What Was Added

**New Project Structure**:
- Created `HPD-AI-Framework/` directory containing all HPD-AI code
- Root-level `.claude/settings.json` added

**File Reorganization**:
- Moved to `HPD-AI-Framework/`:
  - `/dotnet` → `/HPD-AI-Framework/dotnet`
  - `/typescript` → `/HPD-AI-Framework/typescript`
  - `/documentation` → `/HPD-AI-Framework/documentation`
  - `CHANGELOG.md`, `LICENSE`, `.gitignore`, `.gitmodules`, architecture SVGs

**GitHub Workflows**:
- Updated: `publish-nuget.yml` (454 lines modified)
- Updated: `typescript-build-and-test.yml` (30 lines modified)
- Updated: `publish-npm.yml` (26 lines modified)
- Updated: `deploy-docs.yml`
- Updated: `codeql-analysis.yml`

#### Breaking Changes

1. **Project Paths Change** (CRITICAL):
   - All project imports must update paths: `./dotnet/src` → `./HPD-AI-Framework/dotnet/src`
   - All solution references must be adjusted
   - Git submodule paths may be affected

2. **Monorepo Structure**:
   - `HPD-AI-Framework` now contains the framework code previously located at the repository root
   - Integration points between frameworks unclear in documentation

#### Impact on Existing Code

- **Git operations**: Cloning and submodule operations affected
- **Build scripts**: CI/CD pipelines must update paths
- **IDE solutions**: Project file paths require updating
- **Documentation**: All file path references updated
- **GitHub Actions**: Workflows updated for the reorganized repository layout

---

## Namespace & Code Organization Changes

### Significant Namespace Migrations

#### 1. Shared Package Reorganization (a1849b9)

**Moved to `dotnet/src/shared/`**:
- `HPD-Events` → `dotnet/src/shared/HPD-Events`
- `HPD-Graph` → `dotnet/src/shared/HPD-Graph`
- `HPD.OpenApi.Core` → `dotnet/src/shared/HPD.OpenApi.Core`

**Solution file renamed**: `HPD-Agent.slnx` → `HPD-AI.slnx`

**Test project renaming**: `HPD.*.Tests` → `HPD-*.Tests`
- `HPD.Events.Tests` → `HPD-Events.Tests`
- `HPD.Graph.Tests` → `HPD-Graph.Tests`
- etc.

#### 2. Validation Error Namespace Fix (65da02a)

**Moving `ValidationError` and related types**:
```csharp
// Old
namespace HPD.Graph { public class ValidationError { } }

// New
namespace HPD.Agent { public class ValidationError { } }
```

**Affected Files**:
- `ExternalToolScopingWrapper.cs`: Added `using HPD.Agent;`
- All validation type references updated

---

## Dependency & NuGet Updates

### Major Dependency Changes

**Model Context Protocol (MCP)**:
- **Old**: `ModelContextProtocol.Sdk` (pre-1.0)
- **New**: `ModelContextProtocol 1.0.0` (stable)
- **API Changes**:
  - `IMcpClient` and `McpClientFactory` → `McpClient`
  - `McpClient.CreateAsync()` for initialization
  - Interface-based approach replaced with concrete implementation

**Microsoft.OpenApi**:
- **Old**: 1.x
- **New**: 2.0
- **Breaking Changes**:
  - `OpenApiSchema` → `IOpenApiSchema` (interface-based model)
  - `OpenApiParameter` → `IOpenApiParameter`
  - `OpenApiDocument.Load()` → `OpenApiDocument.LoadAsync()` (now async)
  - `JsonSchemaType` → `JsonSchemaType?` (now nullable enum)
  - `JsonNode?` for default values (changed from object)

**Microsoft.CodeAnalysis.CSharp**:
- **Old**: 4.x (Roslyn 4)
- **New**: 5.0 (Roslyn 5)
- **Breaking Changes for Code Generators**:
  - `ISourceGenerator` interface removed
  - **Must migrate to `IIncrementalGenerator`**
  - Affected generators:
    - `DIRegistrationGenerator`
    - `SocketBridgeGenerator`
    - `HPDToolSourceGenerator`
  - No backward compatibility path

**OpenAI SDK**:
- **Old**: 2.x (earlier)
- **New**: 2.9.1
- **API Changes**: Various endpoint and model updates

**FluentAssertions**:
- **Old**: 7.x
- **New**: 8.8
- **Breaking Changes in Test Assertions**:
  - `Should().BeGreaterThanOrEqualTo()` (renamed from `Should().BeGreaterOrEqualTo()`)
  - `Should().BeLessThanOrEqualTo()` (renamed from `Should().BeLessOrEqualTo()`)
  - Affects all test projects in test suite

**xunit**:
- **Old**: 2.x (earlier)
- **New**: 2.9.3

---

## Migration Guide for Developers

### High-Priority Migrations

#### 1. Update Project File References

```xml
<!-- Old paths -->
<ProjectReference Include="../../src/HPD-Events/..." />
<ProjectReference Include="../../src/HPD-Graph/..." />

<!-- New paths -->
<ProjectReference Include="../../src/shared/HPD-Events/..." />
<ProjectReference Include="../../src/shared/HPD-Graph/..." />
```

#### 2. Migrate Roslyn Source Generators

Replace `ISourceGenerator` with `IIncrementalGenerator`:

```csharp
// Old
public class MyGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context) { }
    public void Execute(GeneratorExecutionContext context) { }
}

// New
[Generator]
public class MyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: (node, _) => /* filter */,
            transform: (ctx, _) => /* extract */
        );

        context.RegisterSourceOutput(syntaxProvider, (ctx, syntax) =>
        {
            ctx.AddSource(/* file */, /* code */);
        });
    }
}
```

#### 3. Update Agent Management Code

```csharp
// Old
services.AddAgentSessionManager<MyAgentSessionManager>();

// New
services.AddAgentManager<MyAgentManager>();
services.AddSessionManager<MySessionManager>();
```

#### 4. Update MCP Client Usage

```csharp
// Old
var client = await McpClientFactory.CreateAsync(config);
using var connection = await client.ConnectAsync();

// New
using var client = await McpClient.CreateAsync(config);
```

#### 5. Update OpenAPI Code

```csharp
// Old
OpenApiDocument doc = OpenApiDocument.Load(filePath);
var schema = parameter.Schema;

// New
OpenApiDocument doc = await OpenApiDocument.LoadAsync(filePath);
IOpenApiSchema schema = parameter.Schema;

// Handle nullable enum
JsonSchemaType? type = schema.Type; // Now nullable
JsonNode? defaultValue = schema.Default;
```

#### 6. Update Test Assertions

```csharp
// Old
result.Should().BeGreaterOrEqualTo(5);
result.Should().BeLessOrEqualTo(10);

// New
result.Should().BeGreaterThanOrEqualTo(5);
result.Should().BeLessThanOrEqualTo(10);
```

---

## Breaking Changes Summary

| Component | Breaking Change | Severity | Migration Path |
|-----------|-----------------|----------|-----------------|
| `AgentSessionManager` | Removed, split into `AgentManager` + `SessionManager` | CRITICAL | Refactor to use separate managers |
| Project Structure | Files moved to `HPD-AI-Framework/` subdirectory | CRITICAL | Update all project references |
| Shared Packages | Moved to `dotnet/src/shared/` | CRITICAL | Update project file imports |
| Adapter → Bot Namespace | `HPD.Agent.Adapters` → `HPD.Agent.Bots` | HIGH | Update all usings and project references |
| Adapter → Bot Projects | `HPD-Agent.Adapters.*` → `HPD-Agent.Bots.*` | HIGH | Update .csproj references and imports |
| Test Projects | Renamed `HPD.*.Tests` → `HPD-*.Tests` | HIGH | Update solution and CI/CD references |
| Roslyn Generator Interface | `ISourceGenerator` → `IIncrementalGenerator` | CRITICAL | Rewrite all custom generators |
| MCP Client API | `McpClientFactory` → `McpClient.CreateAsync()` | HIGH | Update MCP initialization code |
| OpenAPI Library | Interface-based model + async load | MEDIUM | Update OpenAPI loading code |
| Event Serialization | Exception objects now `[JsonIgnore]` | MEDIUM | Update event deserialization |
| Validation Errors | Moved from `HPD.Graph` to `HPD.Agent` namespace | LOW | Update namespace imports |

---

## New Features Summary

| Feature | Module | Status | Impact |
|---------|--------|--------|--------|
| Agent Management Split | HPD-Agent Core | New | Architecture improvement, cleaner separation of concerns |
| Evaluation Framework | HPD-Agent Core | New | Performance measurement, agent quality tracking |
| RAG Framework | HPD-RAG.Framework | New | LLM grounding, retrieval, embeddings ecosystem |
| ML Framework | HPD-ML Framework | New | ML algorithms, data handling, model training |
| Slack Socket Mode | HPD-Agent.Bots.Slack | New | Real-time WebSocket support for Slack |
| ToolHarness-Scoped Middleware | HPD-Agent Core | New | Per-toolharness middleware pipelines |
| Branch Sibling Navigation | HPD-Agent Core | New | Conversation UX improvements |
| Headless UI Overhaul | hpd-agent-headless-ui | Enhanced | Better component reactivity, state management |
| Graph Optimization | HPD-Graph | Enhanced | Performance: XxHash64, fast-path execution |
| Rhodium Trading Framework | Rhodium | New | Quantitative analysis and backtesting |
| Adapter → Bot Terminology | HPD-Agent.Bots | Refactor | Aligned naming with bot/chatbot terminology |

---

## File Structure Changes

```
v0.3.3 Structure:
├── dotnet/
│   ├── HPD-Agent.slnx
│   ├── src/
│   │   ├── HPD-Agent.Framework/
│   │   ├── HPD-Events/
│   │   ├── HPD-Graph/
│   │   └── HPD.OpenApi.Core/
│   └── test/

v0.4.0 Structure:
├── HPD-AI-Framework/
│   ├── dotnet/
│   │   ├── HPD-AI.slnx
│   │   ├── src/
│   │   │   ├── shared/           <- Relocated packages
│   │   │   │   ├── HPD-Events/
│   │   │   │   ├── HPD-Graph/
│   │   │   │   ├── HPD.OpenApi.Core/
│   │   │   │   └── Rhodium/
│   │   │   └── HPD-Agent.Framework/
│   │   │   └── HPD-RAG.Framework/
│   │   │   └── HPD-ML.Framework/
│   │   └── test/
│   ├── typescript/
│   └── documentation/
├── HPD-Auth.Framework/
└── .claude/settings.json
```

---

## Testing & Quality Assurance

### New Test Projects

- `AgentEndpointsTests`: CRUD operations for agents
- `EvalEndpointsTests`: Evaluation scoring and analytics (571 test lines)
- `AspNetCoreAgentManagerTests`: Agent lifecycle in ASP.NET Core (236 test lines)
- `InMemoryScoreStoreTests`: Evaluation score storage (460 test lines)
- `SessionManagerTests`: Session state management (289 test lines)

### Updated Test Infrastructure

- `ScoreRecordFactory`: Factory for generating test evaluation records
- `EvalTestWebApplicationFactory`: Test application factory for eval endpoints
- All test names updated from `*SessionManager*` to `*AgentManager*` + `*SessionManager*` split

### Test Framework Updates

- FluentAssertions 8 → Updated all assertion names
- xunit 2.9.3
- Comprehensive evaluation pipeline test coverage

---

## Documentation Updates

### Reorganized Documentation Structure

Moved from root `documentation/` to `HPD-AI-Framework/documentation/`:

**New Topic Organization**:
- `hpd-agent/` - Agent framework documentation
- `hpd-auth/` - Authentication framework (new)
- `hpd-ml/` - ML framework (new)
- `hpd-rag/` - RAG framework documentation (inferred)

**New hpd-auth Documentation**:
- Getting Started (Installation, Quick Start, Introduction, Configuration)
- Core Concepts (Authentication, Sessions, User Model, Events)
- API Reference (Auth, Sessions, Two-Factor, Passkeys, OAuth, Admin)
- Security (Session Revocation, Metadata, Password Policy, Disclosure)

### Architecture Diagrams

New SVG visualizations added:
- `overview.svg`, `overview-dark.svg`: HPD AI Framework overview
- `rag-architecture.svg`, `rag-architecture-dark.svg`: RAG system architecture

---

## Known Issues & Limitations

### From Release Analysis

1. **Incomplete Documentation**: HPD-Auth and HPD-ML frameworks have minimal documentation
2. **RAG Providers**: Some embedding/reranker providers may have limited testing
3. **Backward Compatibility**: Significant breaking changes require full migration

### Recommended Pre-Deployment Checks

- [ ] All project file paths updated to new structure
- [ ] All custom Roslyn generators migrated to IIncrementalGenerator
- [ ] All tests passing with updated FluentAssertions assertions
- [ ] MCP client code updated to new async API
- [ ] OpenAPI loading code updated for async + interface changes
- [ ] Agent/session management refactored and tested
- [ ] RAG providers validated if using RAG features

---

## Upgrade Path from v0.3.3

### Recommended Order

1. **Prepare**: Back up and branch code
2. **Dependencies**: Update all NuGet packages to specified versions
3. **Project Structure**: Move files to new HPD-AI-Framework directory
4. **Code Updates**:
   - Fix namespace imports (HPD-Events, HPD-Graph, Validation errors)
   - Update agent manager code
   - Migrate Roslyn generators
   - Update MCP client code
   - Update OpenAPI code
   - Update test assertions
5. **Testing**: Run full test suite
6. **Validation**: Integration testing with real workloads
7. **Deployment**: Staged rollout to production

### Estimated Effort

- **Small Project** (< 100 source files): 4-8 hours
- **Medium Project** (100-500 source files): 1-2 days
- **Large Project** (500+ source files with custom generators): 3-5 days

---

## Conclusion

This release represents a fundamental evolution of the HPD-Agent Framework with major architectural improvements, new capabilities (RAG, ML, Evaluation), and significant structural reorganization. While the breaking changes are substantial, they result in cleaner architecture, better separation of concerns, and more extensible systems.

**Key Takeaways**:
- Agent management is now properly separated (AgentManager vs SessionManager)
- RAG framework provides comprehensive retrieval capabilities
- ML framework enables in-framework machine learning
- Evaluation framework enables agent quality measurement
- Middleware and toolharness scoping provide more flexible composition
- Project structure reorganization enables multi-framework monorepo

**Recommended Review Areas** for integrators:
- Agent management refactoring (most critical)
- RAG framework capabilities and integration
- Middleware scoping for toolharness composition
- Slack Socket Mode if using Slack adapter
- New evaluation endpoints for agent monitoring

---

## Document Metadata

- **Generated**: 2026-03-22
- **Framework Version**: v0.3.3 → v0.4.0
- **Commits Analyzed**: 31 major commits
- **Primary Sources**:
  - Commit messages and diffs
  - File structure analysis
  - Test coverage review
  - NuGet dependency changes
