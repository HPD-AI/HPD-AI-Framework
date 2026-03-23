# Test Migration Plan: V1 → V2 Middleware

## Current Status

**81 compilation errors** due to `AgentMiddlewareContext` (V1) being removed.

All tests need migration to V2 typed contexts.

## Migration Strategy

### Phase 1: Create Test Helpers  
Create shared test utilities for V2 context creation.

**File**: `test/HPD-Agent.Tests/Middleware/V2/TestHelpers.cs`

```csharp
public static class MiddlewareTestHelpers
{
    public static AgentContext CreateAgentContext(
        string agentName = "TestAgent",
        string? conversationId = "test-conv",
        AgentLoopState? state = null)
    {
        state ??= AgentLoopState.Initial(
            new List<ChatMessage>(),
            "test-run",
            conversationId ?? "test-conv",
            agentName);

        return new AgentContext(
            agentName,
            conversationId,
            state,
            new BidirectionalEventCoordinator(),
            CancellationToken.None);
    }

    public static BeforeIterationContext CreateBeforeIterationContext(
        int iteration = 0,
        List<ChatMessage>? messages = null,
        ChatOptions? options = null)
    {
        var context = CreateAgentContext();
        return context.AsBeforeIteration(
            iteration,
            messages ?? new List<ChatMessage>(),
            options ?? new ChatOptions());
    }

    // ... more helpers
}
```

### Phase 2: Fix Tests File by File

#### Priority Order
1. **AgentMiddlewarePipelineTests.cs** (core pipeline tests)
2. **ErrorTrackingMiddlewareTests.cs** (already has V2 version)
3. **CircuitBreakerMiddlewareTests.cs** (error handling)
4. **FunctionRetryMiddlewareTests.cs** (wrap hooks)
5. **FunctionTimeoutMiddlewareTests.cs** (wrap hooks)
6. **LoggingMiddlewareTests.cs** (all hooks)
7. **PIIMiddlewareTests.cs** (prompt filtering)
8. **ScopedMiddlewareSystemTests.cs** (scoping)
9. **SkillInstructionMiddlewareTests.cs** (instructions)
10. **ToolScopingMiddlewareTests.cs** (tool scoping)

### Phase 3: Pattern Reference

#### V1 → V2 Hook Signature Mapping

| V1 Hook | V2 Hook | Context Type |
|---------|---------|--------------|
| `BeforeMessageTurnAsync(AgentMiddlewareContext)` | `BeforeMessageTurnAsync(BeforeMessageTurnContext)` | Typed |
| `AfterMessageTurnAsync(AgentMiddlewareContext)` | `AfterMessageTurnAsync(AfterMessageTurnContext)` | Typed |
| `BeforeIterationAsync(AgentMiddlewareContext)` | `BeforeIterationAsync(BeforeIterationContext)` | Typed |
| `ExecuteLLMCallAsync(context, next, ct)` | `WrapModelCallAsync(request, handler, ct)` | Immutable request |
| `BeforeToolExecutionAsync(AgentMiddlewareContext)` | `BeforeToolExecutionAsync(BeforeToolExecutionContext)` | Typed |
| `AfterIterationAsync(AgentMiddlewareContext)` | `AfterIterationAsync(AfterIterationContext)` | Typed |
| `BeforeFunctionAsync(AgentMiddlewareContext)` | `BeforeFunctionAsync(BeforeFunctionContext)` | Typed |
| `ExecuteFunctionAsync(context, next, ct)` | `WrapFunctionCallAsync(request, handler, ct)` | Immutable request |
| `AfterFunctionAsync(AgentMiddlewareContext)` | `AfterFunctionAsync(AfterFunctionContext)` | Typed |

#### Context Property Mapping

| V1 Access Pattern | V2 Access Pattern |
|-------------------|-------------------|
| `context.Messages` (nullable) | `context.Messages` (non-null in BeforeIterationContext) |
| `context.Options` (nullable) | `context.Options` (non-null in BeforeIterationContext) |
| `context.Function` (nullable) | `context.Function` (non-null in BeforeFunctionContext) |
| `context.FinalResponse` (nullable) | `context.FinalResponse` (non-null in AfterMessageTurnContext) |
| `context.UpdateState(...)` (scheduled) | `context.UpdateState(...)` (immediate) |
| `context.GetPendingState()` | **REMOVED** - use `context.State` directly |

### Phase 4: Common Test Patterns

#### Creating Test Middleware (V1 vs V2)

**V1 Pattern**:
```csharp
private class TestMiddleware : IAgentMiddleware
{
    public Task BeforeIterationAsync(AgentMiddlewareContext context, CancellationToken ct)
    {
        // Must check for NULL!
        if (context.Messages != null)
            context.Messages.Add(systemMessage);
        return Task.CompletedTask;
    }
}
```

**V2 Pattern**:
```csharp
public class TestMiddleware : IAgentMiddleware
{
    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken ct)
    {
        // No NULL check needed!
        context.Messages.Add(systemMessage);
        return Task.CompletedTask;
    }
}
```

#### Creating Test Contexts (V1 vs V2)

**V1 Pattern**:
```csharp
private static AgentMiddlewareContext CreateContext()
{
    return new AgentMiddlewareContext
    {
        Messages = new List<ChatMessage>(),
        Options = new ChatOptions(),
        State = AgentLoopState.Initial(...)
        // ... many nullable properties
    };
}
```

**V2 Pattern**:
```csharp
private static BeforeIterationContext CreateContext()
{
    var agentContext = MiddlewareTestHelpers.CreateAgentContext();
    return agentContext.AsBeforeIteration(0, new List<ChatMessage>(), new ChatOptions());
}
```

### Phase 5: Execution Plan

1.   Create `TestHelpers.cs` with context creation utilities
2. 🔄 Fix `AgentMiddlewarePipelineTests.cs` (most errors)
3. ⏸️ Fix remaining test files in priority order
4. ⏸️ Run full test suite
5. ⏸️ Document V2 test patterns in README

## Notes

- **Breaking Change**: This is acceptable for v0
- **Test Coverage**: Maintain 100% coverage during migration
- **Pattern Consistency**: Use typed contexts consistently across all tests
- **Documentation**: Update test README with V2 patterns
