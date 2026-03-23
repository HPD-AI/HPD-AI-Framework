# Bulk Test Migration Script

This document contains find/replace patterns to migrate V1 tests to V2.

## Automated Replacements

### 1. Using Statements
Add to top of files:
```csharp
using HPD.Agent.Tests.Middleware.V2;
using static HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers;
```

### 2. Context Creation Method Replacements

**Pattern 1**: Simple context creation
```csharp
// OLD (V1)
private static AgentMiddlewareContext CreateContext()
{
    var context = new AgentMiddlewareContext
    {
        Messages = new List<ChatMessage>(),
        Options = new ChatOptions(),
        State = AgentLoopState.Initial(...)
    };
    return context;
}

// NEW (V2)
private static BeforeIterationContext CreateContext()
{
    return CreateBeforeIterationContext();
}
```

### 3. Hook Signature Replacements

Run these replacements in order:

#### Before/After Iteration
```bash
# BeforeIterationAsync
Find: BeforeIterationAsync\(AgentMiddlewareContext context
Replace: BeforeIterationAsync(BeforeIterationContext context

# AfterIterationAsync
Find: AfterIterationAsync\(AgentMiddlewareContext context
Replace: AfterIterationAsync(AfterIterationContext context
```

#### Before/After Function
```bash
# BeforeFunctionAsync (V1 had different name)
Find: BeforeSequentialFunctionAsync\(AgentMiddlewareContext context
Replace: BeforeFunctionAsync(BeforeFunctionContext context

# AfterFunctionAsync
Find: AfterFunctionAsync\(AgentMiddlewareContext context
Replace: AfterFunctionAsync(AfterFunctionContext context
```

#### Before/After Message Turn
```bash
# BeforeMessageTurnAsync
Find: BeforeMessageTurnAsync\(AgentMiddlewareContext context
Replace: BeforeMessageTurnAsync(BeforeMessageTurnContext context

# AfterMessageTurnAsync
Find: AfterMessageTurnAsync\(AgentMiddlewareContext context
Replace: AfterMessageTurnAsync(AfterMessageTurnContext context
```

#### Tool Execution
```bash
# BeforeToolExecutionAsync
Find: BeforeToolExecutionAsync\(AgentMiddlewareContext context
Replace: BeforeToolExecutionAsync(BeforeToolExecutionContext context
```

### 4. Property Access Replacements

Most properties work as-is, but some need changes:

```bash
# Remove null checks (no longer needed)
Find: if \(context\.Messages != null\)
Replace: // V2: No null check needed

# Remove null coalescing (no longer needed)
Find: context\.Messages \?\? new List<ChatMessage>\(\)
Replace: context.Messages
```

### 5. Test Helper Class Replacements

```bash
# Change private to public (for IAgentMiddleware)
Find: private class (\w+Middleware) : IAgentMiddleware
Replace: public class $1 : IAgentMiddleware
```

## Files to Migrate (Priority Order)

1. **AgentMiddlewarePipelineTests.cs** - 471 lines
2. **ErrorTrackingMiddlewareTests.cs** - Partially done (has V2 version)
3. **CircuitBreakerMiddlewareTests.cs** - Error handling
4. **FunctionRetryMiddlewareTests.cs** - Wrap hooks
5. **FunctionTimeoutMiddlewareTests.cs** - Wrap hooks
6. **LoggingMiddlewareTests.cs** - All hooks
7. **PIIMiddlewareTests.cs** - Prompt filtering
8. **ScopedMiddlewareSystemTests.cs** - Scoping
9. **SkillInstructionMiddlewareTests.cs** - Instructions
10. **ToolScopingMiddlewareTests.cs** - Tool scoping
11. **IterationFilterTestHelpers.cs** - Helper file

## Manual Review Required

After automated replacement, manually review:

1. **Context creation** - Ensure correct typed context is used
2. **Property access** - Verify properties exist on typed context
3. **Test assertions** - Update assertions for new property names
4. **Pipeline creation** - Verify middleware array initialization

## VSCode Multi-File Find/Replace

1. Open search (Cmd+Shift+F)
2. Enable regex mode (.*)
3. Set "files to include": `test/HPD-Agent.Tests/Middleware/**/*.cs`
4. Set "files to exclude": `**/V2/**`
5. Run each pattern from above

## Verification

After migration:
```bash
cd test/HPD-Agent.Tests
dotnet build --no-restore 2>&1 | grep -c "error CS"
# Should be 0 errors
```

## Example Migration

See [PipelineV2Tests.cs](PipelineV2Tests.cs:1-377) for a complete V2 test example.
