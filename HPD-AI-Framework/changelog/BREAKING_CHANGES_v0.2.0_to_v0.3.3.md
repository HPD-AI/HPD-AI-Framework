# Breaking Changes: v0.2.0 → v0.3.3

This document catalogs all breaking changes in the HPD-Agent.Framework between versions 0.2.0 and 0.3.3. The period contains significant architectural refactoring including session/branch separation, memory system overhaul, graph feature expansion, and new observability infrastructure.

---

## Table of Contents

1. [Core API & Architecture](#core-api--architecture)
2. [Session & Branch Management](#session--branch-management)
3. [Content Storage (Memory System Overhaul)](#content-storage-memory-system-overhaul)
4. [Middleware & Execution](#middleware--execution)
5. [Graph & Execution Engine](#graph--execution-engine)
6. [MCP & Capabilities](#mcp--capabilities)
7. [Provider & Tool Infrastructure](#provider--tool-infrastructure)
8. [Dependencies & Observability](#dependencies--observability)
9. [Migration Guide](#migration-guide)

---

## Core API & Architecture

### AgentRunOptions → AgentRunConfig (Rename + Signature Changes)

**Breaking Change:** Complete class rename affecting 100+ call sites.

**Old API:**
```csharp
public class AgentRunOptions
{
    // ... various options
}

// Usage:
var options = new AgentRunOptions { ... };
var result = await agent.RunAsync(userMessage, options);
```

**New API:**
```csharp
public class AgentRunConfig
{
    public ChatRunConfig? Chat { get; set; }
    public string? ProviderKey { get; set; }
    public string? ModelId { get; set; }
    public string? ApiKey { get; set; }
    public string? ProviderEndpoint { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
    public IChatClient? OverrideChatClient { get; set; }
    public string? SystemInstructions { get; set; }
    public string? AdditionalSystemInstructions { get; set; }
    public List<string>? DynamicContextKeys { get; set; }
    public Dictionary<string, object>? ContextInstances { get; set; }
    public bool DisableEvaluators { get; set; }
    public bool IsInternalEvalJudgeCall { get; set; }
    // ... additional properties for audio, structured output, etc.
}
```

**Impact:**
- All agent invocations must use `AgentRunConfig` instead of `AgentRunOptions`
- All method signatures expecting `AgentRunOptions` now expect `AgentRunConfig`
- Projects with custom Agent extensions or tooling must update accordingly

**Migration:**
```csharp
// Before
var options = new AgentRunOptions { Temperature = 0.7 };

// After
var config = new AgentRunConfig
{
    Chat = new ChatRunConfig { Temperature = 0.7 }
};
```

---

## Session & Branch Management

### Monolithic AgentSession → Separated Session + Branch Model

**Breaking Change:** Fundamental architecture shift from single-message-store to multi-branch design.

**Old API:**
```csharp
public class AgentSession
{
    public string SessionId { get; }
    public List<ChatMessage> Messages { get; }  // All messages in one list
    public Dictionary<string, object> Metadata { get; }
    public ExecutionCheckpoint? LastCheckpoint { get; }
    public List<PendingWrite> PendingWrites { get; }
    // ... checkpoint operations
}
```

**New API:**
```csharp
public class Session
{
    public string Id { get; init; }
    public Dictionary<string, object> Metadata { get; init; }
    // Messages now in Branch objects
}

public class Branch
{
    public string Id { get; init; }
    public string SessionId { get; init; }
    public List<ChatMessage> Messages { get; init; }
    public string? ForkedFrom { get; init; }
    public int? ForkedAtMessageIndex { get; init; }
    // Branch-scoped state
}
```

**Impacts:**

1. **Session Loading:** Must now load both Session metadata AND Branch (with messages):
   ```csharp
   // Before
   var session = await store.LoadSessionAsync(sessionId);
   var messages = session.Messages;

   // After
   var session = await store.LoadSessionAsync(sessionId);
   var branch = await store.LoadBranchAsync(sessionId, branchId);
   var messages = branch.Messages;
   ```

2. **Session Persistence:** Methods now work with metadata-only Session and separate Branch:
   ```csharp
   // Before
   await store.SaveSessionAsync(agentSession);  // Saved everything

   // After
   await store.SaveSessionAsync(session);       // Metadata only
   await store.SaveBranchAsync(sessionId, branch);  // Messages and history
   ```

3. **ISessionStore Interface:** Completely redesigned:
   - Removed: All AgentSession-related methods
   - Removed: Checkpoint operation methods
   - Removed: PendingWrite management
   - Added: `LoadBranchAsync`, `SaveBranchAsync`, `ListBranchIdsAsync`, `DeleteBranchAsync`
   - Modified: Session methods now work with metadata-only Session type

### Checkpoint/DurableExecution System Removal

**Breaking Change:** Entire checkpoint and durable execution subsystem removed.

**Removed APIs:**
- `ExecutionCheckpoint` record and related types
- `DurableExecutionService` class
- `DurableExecutionConfig` class
- `ISessionStore` checkpoint methods: `SaveCheckpointAsync`, `LoadCheckpointAsync`, etc.
- `AgentBuilderCheckpointingExtensions.AddCheckpointing` extension
- `PendingWrite` record and persistence
- `CheckpointExceptions`, `CheckpointTypes` enums
- `AgentSession.LastCheckpoint` property
- `AgentSession.PendingWrites` collection

**Replacement:**
The checkpoint system is replaced with a lightweight **UncommittedTurn** concept:
```csharp
public sealed record UncommittedTurn
{
    public string BranchId { get; init; }
    public Message? LastMessage { get; init; }
    public SuspendReason? SuspendReason { get; init; }
    // Stateless delta-based recovery (~10-20KB vs ~100KB)
}

// ISessionStore now has:
Task<UncommittedTurn?> LoadUncommittedTurnAsync(string sessionId, ...);
Task SaveUncommittedTurnAsync(string sessionId, UncommittedTurn turn, ...);
Task DeleteUncommittedTurnAsync(string sessionId, ...);
```

**Migration Impact:**
- Code relying on checkpoint restoration must migrate to UncommittedTurn pattern
- No more explicit checkpoint management in middleware
- Agent framework handles UncommittedTurn automatically

---

## Content Storage (Memory System Overhaul)

### HPD-Agent.Memory Project Removed

**Breaking Change:** Entire project deleted with all memory system APIs.

**Removed Namespaces & Classes:**
- `HPD.Agent.Memory.*` namespace (entire)
- `DynamicMemory` module:
  - `DynamicMemory` class
  - `DynamicMemoryAgentMiddleware`
  - `DynamicMemoryConfig`, `DynamicMemoryOptions`
  - `DynamicMemoryStore`, `InMemoryDynamicMemoryStore`, `JsonDynamicMemoryStore`
  - `DynamicMemoryPlugin`
  - `MemoryBuilderExtensions.AddDynamicMemory()`

- `StaticMemory` module:
  - `StaticMemoryManager` class
  - `StaticMemoryAgentMiddleware`
  - `StaticMemoryConfig`, `StaticMemoryOptions`, `MemoryStrategy`
  - `StaticMemoryStore`, `InMemoryStaticMemoryStore`, `JsonStaticMemoryStore`
  - `StaticMemoryDocument`, `MemoryAutoDiscovery`, `MemoryDiscovery`
  - `MemoryBuilderExtensions.AddStaticMemory()`

- `PlanMode` module (relocated to core):
  - `PlanModeModule` moved to `HPD.Agent.Planning`
  - `PlanModeBuilderExtensions` moved to `HPD.Agent.Planning`

**Old Usage:**
```csharp
// Dynamic Memory
services.AddDynamicMemory(options => {
    options.Store = new InMemoryDynamicMemoryStore();
});

// Static Memory
services.AddStaticMemory(options => {
    options.MemoryStrategy = MemoryStrategy.Persistent;
});
```

**Migration:**
Memory functionality is now integrated into the unified `IContentStore` system. See [Content Store Migration](#content-store-migration) below.

### DocumentStore System Replacement

**Breaking Change:** Entire DocumentStore abstraction removed and replaced with `IContentStore`.

**Removed Interfaces & Classes:**
- `IDocumentContentStore`
- `IDocumentMetadataStore`
- `IInstructionDocumentStore`
- `ISkillDocumentLinker`
- `InstructionDocumentStoreBase`
- `InstructionDocumentStoreFactory`
- `FileSystemInstructionStore`
- `InMemoryInstructionStore`
- `SkillDocument`, `SkillDocumentMetadata`, `SkillDocumentReference`
- `DocumentRetrievalPlugin`
- `DocumentExtractionException`, `DocumentNotFoundException`, `DuplicateDocumentException`

**New Interface:**
```csharp
public interface IContentStore
{
    Task<string> PutAsync(string? scope, byte[] data, string contentType,
        ContentMetadata? metadata = null, CancellationToken cancellationToken = default);

    Task<ContentData?> GetAsync(string? scope, string contentId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string? scope, string contentId,
        CancellationToken cancellationToken = default);

    Task<List<ContentMetadata>> QueryAsync(string? scope,
        ContentQuery query, CancellationToken cancellationToken = default);
}
```

**New Implementations:**
- `InMemoryContentStore`
- `LocalFileContentStore`

### Skill Document Activation Changes

**Breaking Change:** Skill activation message format and visibility logic changed.

**Before:**
```csharp
// Skill documents referenced via read_skill_document tool
skills:
  - id: my-skill
    documents:
      - SkillDocument { id: "intro", content: "..." }

// Activation message used read_skill_document
"Read the skill document with id 'intro'"
```

**After:**
```csharp
// Skill documents referenced via content_read
// Source generator emits InitializeDocumentsAsync(IContentStore) per skill

public partial class MySkill
{
    public async Task InitializeDocumentsAsync(IContentStore store)
    {
        await store.PutAsync(
            scope: agentName,
            data: Encoding.UTF8.GetBytes("..."),
            contentType: "text/plain",
            metadata: new ContentMetadata { Folder = "/skills" }
        );
    }
}

// Activation message now uses content_read
"Read the skill introduction with content_read(\"/skills/my-skill-intro\")"
```

### Content Folder System Added

**New APIs:**
```csharp
public interface IContentFolder
{
    Task<string> WriteAsync(string name, byte[] data, string contentType, ...);
    Task<byte[]?> ReadAsync(string name, ...);
    Task DeleteAsync(string name, ...);
    Task<List<ContentMetadata>> ListAsync(...);
}

public class FolderOptions
{
    public string Name { get; set; }       // e.g., "/skills", "/knowledge"
    public string? Description { get; set; }
    public bool Ephemeral { get; set; }    // Auto-cleanup on session end
    public long? QuotaBytes { get; set; }
}

// AgentBuilder extension:
builder.UseDefaultContentStore()  // Creates /skills, /knowledge, /memory folders
```

---

## Middleware & Execution

### Middleware State Architecture Changes

**Breaking Change:** Significant refactoring of scoped middleware state system.

**Old State Persistence:**
```csharp
// AgentSession held all state
public class AgentSession
{
    public Dictionary<string, object> MiddlewareState { get; }
}
```

**New State Scoping:**
```csharp
// Separate scopes: Session-scoped vs Branch-scoped
public class StateScope
{
    public Dictionary<string, object> SessionScoped { get; }    // Shared across branches
    public Dictionary<string, object> BranchScoped { get; }     // Per-branch
}

// Middleware attribute declares scope:
[MiddlewareState(scope: StateScope.Session)]
public class MyPermissionMiddleware { }

[MiddlewareState(scope: StateScope.Branch)]
public class MyHistoryMiddleware { }
```

**Attribute Changes:**
- New: `[MiddlewareState(scope)]` attribute for declaring state ownership
- Modified: `MiddlewareStateContainer` now manages both session and branch scopes
- Impact: Middleware storing per-branch data must explicitly declare branch scope

### ToolVisibilityManager API Changes

**Breaking Change:** Removal of skill document visibility logic and simplification.

**Removed Methods:**
- `read_skill_document` tool registration (no longer used)
- `ToolVisibilityManager.GetSkillDocumentContent()`
- Document-based visibility filtering

**Removed Properties:**
- `SkillOptions.Documents` (was used for document registration)

---

## Graph & Execution Engine

### Port-Based Routing & Cloning Policy Added

**New APIs (Non-Breaking Additions):**
```csharp
// New CloningPolicy enum replaces ErrorPropagationPolicy
public enum CloningPolicy
{
    AlwaysClone,      // All edges get clones
    NeverClone,       // All edges get original (shared reference)
    LazyClone         // First edge gets original, rest get clones
}

// Edge enhancements for port-based routing
public class Edge
{
    public int? FromPort { get; set; }    // Output port of source node
    public int? ToPort { get; set; }      // Input port of dest node
    public CloningPolicy CloningPolicy { get; set; }
    // ... existing properties
}

// GraphBuilder supports port specification
builder.Connect(source, dest, fromPort: 0, toPort: 1)
```

**Behavior Change:** Input namespacing adjusted:
- Port 0 keys: `"nodeId.key"` (unchanged for backward compatibility)
- Port N (N>0) keys: `"nodeId:portN.key"` (new for multi-output nodes)

### Advanced Graph Features: Artifacts & Partitioning

**New Major APIs (Non-Breaking Additions):**
```csharp
// Artifact Registry
public interface IArtifactRegistry
{
    Task<string> RegisterAsync(string key, byte[] data, ...);
    Task<T> MaterializeAsync<T>(string key, ...);
    Task<List<T>> MaterializeManyAsync<T>(List<string> keys, ...);
    Task BackfillAsync(string key, PartitionKey partition, byte[] data, ...);
}

// Partition Definitions
public abstract class PartitionDefinition
{
    public string Key { get; }
}

public class TimePartitionDefinition : PartitionDefinition
{
    // Time-based partitioning with cron schedules
}

public class StaticPartitionDefinition : PartitionDefinition
{
    // User-defined partition keys
}

// Temporal Operators
public class EdgeRetryPolicy
{
    public int MaxRetries { get; set; }
    public TimeSpan? ExponentialBackoff { get; set; }
}

public class ScheduleConstraint
{
    public string CronExpression { get; set; }  // Cron-based scheduling
}

// Node enhancements for partitioned processing
public class Node
{
    public MultiPartitionDefinition? PartitionDefinition { get; set; }
}
```

**Impact:**
- New graph capabilities for data orchestration and temporal scheduling
- Existing graphs continue to work (opt-in feature)
- New methods: `IGraphOrchestrator.MaterializeAsync`, `BackfillAsync`, `DemandDrivenExecutionAsync`

---

## MCP & Capabilities

### ToolHarness MCP Support Added

**New APIs (Non-Breaking Additions):**
```csharp
// MCP Server attribute for exposing agent capabilities as MCP servers
[MCPServer(name: "my-agent-server", description: "My agent as MCP server")]
public class MyMCPServer
{
    [MCPTool]
    public async Task<string> MyTool(string input) { }
}

// MCPServerCapability for agent capabilities exported as MCP tools
public class MCPServerCapability : ICapability
{
    public string ServerId { get; set; }
    public List<MCPServerRegistration> Servers { get; set; }
}

// Source generator support
// Generates MCPServerAttribute handlers in SourceGenerator
```

**AgentBuilder Extensions:**
```csharp
builder.AddMCPServer<MyMCPServer>(config =>
{
    config.Endpoint = "http://localhost:3000";
});
```

**CapabilityType Enum:** Added `MCPServer` value.

---

## Provider & Tool Infrastructure

### Provider Assembly Discovery Changes

**Breaking Change:** Provider discovery mechanism enhanced with better error handling.

**New Classes:**
- `ModelNotFoundDetector` for improved error categorization
- `ErrorCategory` enum with expanded categories

**Modified Error Handlers:**
All provider error handlers updated:
- `AnthropicErrorHandler`
- `AzureAIErrorHandler`, `AzureAIInferenceErrorHandler`
- `BedrockErrorHandler`
- `GoogleAIErrorHandler`
- `HuggingFaceErrorHandler`
- `MistralErrorHandler`
- `OllamaErrorHandler`
- `OpenAIErrorHandler`
- `OpenRouterErrorHandler`

**Impact:** Error handling behavior improved; custom error handlers should be reviewed for compatibility with new patterns.

### External Tool Scoping

**Breaking Change:** Tool scoping wrapper modifications.

**Modified Class:**
- `ExternalToolScopingWrapper.cs` now includes `using HPD.Agent` directive

**New Namespace Requirement:**
- `ValidationError` and related types now in `HPD.Agent` namespace (was elsewhere)

---

## Dependencies & Observability

### Observability Layer (OpenTelemetry Integration)

**New Major Feature (Non-Breaking Additions):**
```csharp
// OTel tracing support
public class TracingObserver : IAgentEventObserver
{
    // Maps agent events to Activity spans with parent/child linking
}

// Agent event propagation changes
public abstract class AgentEvent
{
    public string? TraceId { get; set; }      // 128-bit trace identifier
    public string? SpanId { get; set; }       // 64-bit span identifier
    public string? ParentSpanId { get; set; } // Parent span reference
}

// Builder support
builder.WithTracing()  // Enables OTel tracing
```

**New Dependency:** `OpenTelemetry.Extensions.Hosting` added to AspNetCore support.

### ObserverDispatcher Pattern

**New Infrastructure:**
```csharp
// Replaces bare Task.Run dispatch for event observers
public class ObserverDispatcher
{
    // Per-observer FIFO channel ensuring ordered delivery
    // Eliminates race conditions in event propagation
}
```

### Evaluation Framework Additions

**New APIs (Non-Breaking):**
```csharp
// Evaluation framework
public interface IEvaluationMiddleware
{
    // Judge agent invocation with cycle prevention
}

// New AgentRunConfig flags:
public bool DisableEvaluators { get; set; }
public bool IsInternalEvalJudgeCall { get; set; }

// New project: HPD-Agent.Evaluations (tests: HPD-Agent.Evaluations.Tests)
```

### HPD.VCS Project Addition

**New Project (Non-Breaking):**
- `HPD.VCS` project added to solution
- `HPD.VCS.Tests` added to solution
- Provides version control integration abstractions

---

## Migration Guide

### Priority 1: Session/Branch Architecture

**Action Items:**

1. **Update Session Loading Code:**
   ```csharp
   // Before
   var session = await store.LoadSessionAsync(sessionId);
   var messages = session.Messages;  // ❌ No longer exists

   // After
   var session = await store.LoadSessionAsync(sessionId);
   var branchId = session.Metadata.TryGetValue("defaultBranch", out var bid)
       ? bid.ToString()
       : "main";
   var branch = await store.LoadBranchAsync(sessionId, branchId);
   var messages = branch.Messages;
   ```

2. **Update Session Persistence:**
   ```csharp
   // Before
   await store.SaveSessionAsync(agentSession);

   // After
   var session = new Session { Id = sessionId, Metadata = ... };
   await store.SaveSessionAsync(session);

   var branch = new Branch { Id = branchId, SessionId = sessionId, Messages = ... };
   await store.SaveBranchAsync(sessionId, branch);
   ```

3. **Remove Checkpoint Code:**
   ```csharp
   // Before
   var checkpoint = await store.LoadCheckpointAsync(sessionId);
   var config = new DurableExecutionConfig { ... };

   // After
   var uncommittedTurn = await store.LoadUncommittedTurnAsync(sessionId);
   // Framework handles UncommittedTurn automatically
   ```

4. **Update Custom Middleware:**
   - Declare state scope with `[MiddlewareState(scope: StateScope.Session)]`
   - Remove checkpoint save/restore logic
   - Use UncommittedTurn pattern if needed

### Priority 2: Content Storage Migration

**Action Items:**

1. **Replace DynamicMemory:**
   ```csharp
   // Before
   services.AddDynamicMemory(opts => {
       opts.Store = new InMemoryDynamicMemoryStore();
   });

   // After
   builder.UseDefaultContentStore();  // Creates /skills, /knowledge, /memory
   // Use IContentStore directly for memory operations
   ```

2. **Replace StaticMemory:**
   ```csharp
   // Before
   services.AddStaticMemory(opts => {
       opts.MemoryStrategy = MemoryStrategy.Persistent;
       opts.Documents.Add(new StaticMemoryDocument { ... });
   });

   // After
   // Use IContentStore with /knowledge folder
   var folder = contentStore.GetFolder("/knowledge", agentName);
   await folder.WriteAsync("doc-name", data, "text/plain");
   ```

3. **Update Skill Documents:**
   ```csharp
   // Before
   [Skill]
   public class MySkill
   {
       public SkillDocument[] SkillDocuments => new[] {
           new SkillDocument { Id = "intro", Content = "..." }
       };
   }

   // After
   [Skill]
   public partial class MySkill
   {
       public async Task InitializeDocumentsAsync(IContentStore store)
       {
           await store.PutAsync(
               scope: "agentName",
               data: Encoding.UTF8.GetBytes("..."),
               contentType: "text/plain",
               metadata: new ContentMetadata { Name = "intro", Folder = "/skills" }
           );
       }
   }
   ```

4. **Update Document Queries:**
   ```csharp
   // Before
   var docs = await docStore.GetDocumentsByTagAsync("memory", "important");

   // After
   var metadata = await contentStore.QueryAsync(
       scope: sessionId,
       query: new ContentQuery { FolderFilter = "/memory", TagFilter = "important" }
   );
   ```

### Priority 3: Agent Invocation Updates

**Action Items:**

1. **Replace AgentRunOptions:**
   ```csharp
   // Before
   var options = new AgentRunOptions
   {
       Temperature = 0.7,
       MaxTokens = 4000
   };

   // After
   var config = new AgentRunConfig
   {
       Chat = new ChatRunConfig
       {
           Temperature = 0.7,
           MaxTokens = 4000
       }
   };
   ```

2. **Update Provider Switching:**
   ```csharp
   // Before
   var options = new AgentRunOptions { ProviderKey = "openai" };

   // After (same pattern, different class)
   var config = new AgentRunConfig { ProviderKey = "openai" };
   ```

### Priority 4: Custom Implementations

**If You've Implemented:**

- **Custom ISessionStore:** Must implement new branch-focused methods:
  ```csharp
  public Task<Branch?> LoadBranchAsync(string sessionId, string branchId, ...) { }
  public Task SaveBranchAsync(string sessionId, Branch branch, ...) { }
  public Task<List<string>> ListBranchIdsAsync(string sessionId, ...) { }
  public Task DeleteBranchAsync(string sessionId, string branchId, ...) { }
  ```
  Remove checkpoint-related methods.

- **Custom Middleware:** Declare state scope:
  ```csharp
  [MiddlewareState(scope: StateScope.Session)]  // or StateScope.Branch
  public class MyMiddleware { }
  ```

- **Custom Error Handlers:** Review for compatibility with `ErrorCategory` enum changes and new error detection patterns.

- **Custom Agents Using Memory:** Migrate to IContentStore-based approach.

---

## Summary Table

| Category | Type | Change | Status |
|----------|------|--------|--------|
| Session Architecture | Breaking | AgentSession → Session + Branch | Must Update |
| Checkpoint System | Breaking | Removed entirely, use UncommittedTurn | Must Update |
| Memory Modules | Breaking | HPD-Agent.Memory project deleted | Must Update |
| DocumentStore | Breaking | Replaced with IContentStore | Must Update |
| AgentRunOptions | Breaking | Renamed to AgentRunConfig | Must Update |
| Port-Based Routing | Addition | New Edge.FromPort/ToPort/CloningPolicy | Optional |
| Artifacts & Partitioning | Addition | New graph features, new IArtifactRegistry | Optional |
| MCP Support | Addition | New MCPServer capability | Optional |
| OpenTelemetry | Addition | New TracingObserver, traceId/spanId on events | Optional |
| Evaluation Framework | Addition | New HPD-Agent.Evaluations project | Optional |

---

## References

**Key Commits:**
- `e4f256f` - SubAgents: branch-aware thread modes
- `8e99dfc` - Advanced graph features
- `b6f585d` - Port-based routing
- `5ad570e` - ToolHarness MCP support
- `0416e97` - HPD-Agent.Memory removal
- `04c0a1f` - Observability layer (OTel)
- `6c65e90` - Core API refactoring (AgentRunConfig)
- `37cd679` - Session/Branch separation
- `dc963b0` - UncommittedTurn (checkpoint replacement)

**Files Modified:**
- `HPD-Agent/Agent/AgentRunConfig.cs` (formerly AgentRunOptions.cs)
- `HPD-Agent/Session/Branch.cs` (new)
- `HPD-Agent/Session/Session.cs` (new, metadata-only)
- `HPD-Agent/Session/ISessionStore.cs` (redesigned)
- `HPD-Agent/Session/UncommittedTurn.cs` (new)
- `HPD-Agent/Content/IContentStore.cs` (redesigned)
- `HPD-Agent/Content/InMemoryContentStore.cs` (new)
- `HPD-Agent/Content/LocalFileContentStore.cs` (new)
- `HPD.Graph/HPD.Graph.Abstractions/Graph/Edge.cs` (ports, cloning policy)
- `HPD-Agent.MCP/MCPServerCapability.cs` (new)
- `HPD-Agent/Observability/TracingObserver.cs` (new)

---

**Document Version:** 1.0
**Generated:** 2026-03-22
**Coverage:** v0.2.0 → v0.3.3 (82 commits)
