# Error handling middleware

Error handling combines function and model wrappers, iteration safety middleware,
error events, and application-level handling around `RunAsync`.

## Choose the boundary

| Failure | Primary mechanism |
| --- | --- |
| Transient model/provider failure | `RetryMiddleware` model wrapper, then `ErrorFormattingMiddleware` |
| Function-body exception | Function wrappers, `OnErrorAsync`, then `AfterFunctionAsync` formatting |
| Slow function | `FunctionTimeoutMiddleware` |
| Repeated tool loops | `CircuitBreakerMiddleware` |
| Consecutive or cumulative tool errors | `ErrorTrackingMiddleware` and `TotalErrorThresholdMiddleware` |
| Unhandled turn failure | `MessageTurnErrorEvent` and the exception from `RunAsync` |

`OnErrorAsync` observes each terminal model, function, iteration, message-turn,
input, runtime, and thread-fork failure exactly once. Caller cancellation stays on
the cancellation lifecycle instead. The original failure continues to propagate
after error handlers complete.

## Recommended wrapper order

The first registered wrapper is outermost:

```text
RetryMiddleware(FunctionTimeoutMiddleware(ErrorFormattingMiddleware(core)))
```

Register that stack explicitly:

```csharp
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Middleware.Function;

var errorHandling = new ErrorHandlingConfig
{
    IncludeDetailedErrorsInChat = false,
    ModelRetries = new()
    {
        MaxRetries = 2
    },
    FunctionRetries = new()
    {
        MaxRetries = 2,
        RetryDelay = TimeSpan.FromSeconds(1)
    },
    SingleFunctionTimeout = TimeSpan.FromSeconds(10)
};

await using var agent = await new AgentBuilder(new AgentConfig())
    .WithChatClient(chatClient)
    .WithMiddleware(new RetryMiddleware(errorHandling))
    .WithMiddleware(new FunctionTimeoutMiddleware(
        errorHandling.SingleFunctionTimeout.Value))
    .WithMiddleware(new ErrorFormattingMiddleware(errorHandling))
    .BuildAsync();
```

The builder helpers are equivalent:

```csharp
var agent = await new AgentBuilder(new AgentConfig())
    .WithChatClient(chatClient)
    .WithRetry(config =>
    {
        config.ModelRetries.MaxRetries = 2;
        config.FunctionRetries.MaxRetries = 2;
        config.IncludeDetailedErrorsInChat = false;
    })
    .WithFunctionTimeout(TimeSpan.FromSeconds(10))
    .WithErrorFormatting()
    .BuildAsync();
```

Retry registration is explicit. A positive retry budget does not install retry
middleware and does not make a function replay-safe.

## Function retry

Functions are retried only when their effective replay policy is
`ReplayPolicy.Idempotent`, the function budget is available, and no exclusion matches.
Use the source-generated function declaration:

```csharp
[AIFunction(Replay = ReplayPolicy.Idempotent)]
public Task<Order> LookUpOrder(string id) => orders.FindAsync(id);
```

Non-idempotent tools remain `Never` unless explicitly declared otherwise. Exact
function, harness, and function/action exclusions always win:

```csharp
config.ErrorHandling.FunctionRetries.Exclude = new()
{
    Harnesses = ["ComputerHarness"],
    Functions = ["SendPayment"],
    Actions =
    [
        new ActionRetryExclusion
        {
            Function = "Files",
            Action = "delete"
        }
    ]
};
```

`RetryMiddleware` wraps both model streaming and function execution. Function retries
emit `FunctionRetryEvent`; model retries emit `ModelCallRetryEvent`. A model retry can
follow partially streamed text, so streaming UIs should clear or mark the failed
partial response before rendering the retry.

## Function timeout

`SingleFunctionTimeout` defaults to `null`, which disables function timeouts.
`FunctionTimeoutMiddleware` wraps only function execution. It does not impose a
conversation deadline or cancel model streaming.

Place timeout inside retry to give each retry attempt its own deadline. The
parameterless `WithFunctionTimeout()` helper uses the configured value when present
and otherwise applies its explicit 30-second convenience default.

## Error formatting

`ErrorHandlingConfig.IncludeDetailedErrorsInChat` defaults to `true`. With detailed
errors enabled, the exception message is included in model-facing errors so the model
can attempt recovery. This may expose secrets, filesystem paths, connection strings,
or internal implementation details.

Set it to `false` for production-facing agents when exception text is not already
safe:

```csharp
var config = new AgentConfig
{
    ErrorHandling = new ErrorHandlingConfig
    {
        IncludeDetailedErrorsInChat = false
    }
};

var agent = await new AgentBuilder(config)
    .WithChatClient(chatClient)
    .WithErrorFormatting()
    .BuildAsync();
```

With sanitization enabled, function errors sent to the model use a generic message.
Model-call errors remain exceptions but use a sanitized provider-aware message. The
original exception remains available to middleware and observability handlers.

## Structured public errors

Use `ProviderErrorInfo` when an event or API returns a provider failure to an
application. It provides an immutable, serialization-safe shape with:

- stable error code and message;
- normalized `ErrorCategory`;
- retryability, HTTP status, and provider error type;
- provider request ID and retry delay when available.

```csharp
using var subscription = agent.Subscribe<UserAudioTranscriptFailedEvent>(evt =>
{
    if (evt.ErrorCode == "rate_limit_exceeded" && evt.IsRetryable)
    {
        queueTranscriptRetry(evt.ProviderItemId);
    }
});
```

Provider adapters should classify errors once and expose the normalized fields
directly. Applications should not need to parse free-form messages or inspect
provider-specific metadata to decide whether an operation is retryable.

## Iteration safety

`ErrorTrackingMiddleware`, `TotalErrorThresholdMiddleware`, and
`CircuitBreakerMiddleware` apply iteration/tool safety policies. They update
middleware state and can terminate the run when their thresholds are reached.

```csharp
var agent = await new AgentBuilder(new AgentConfig())
    .WithChatClient(chatClient)
    .WithCircuitBreaker(maxConsecutiveCalls: 3)
    .WithErrorTracking(maxConsecutiveErrors: 3)
    .BuildAsync();
```

Use `UpdateMiddlewareState<TState>(...)` for one middleware-state record and
`UpdateState(...)` when changing core loop state such as `IsTerminated`.

Error middleware improves recovery and presentation; it is not a transactional
guarantee. A tool that partially mutates an external system before throwing may be
retried. Use idempotency keys, replay constraints, and confirmation boundaries for
side-effecting tools.

For the broader lifecycle, see
[Middleware Lifecycle](https://hpd-ai.github.io/HPD-Agent/concepts/middleware-lifecycle)
and
[Error Handling Middleware](https://hpd-ai.github.io/HPD-Agent/guides/middleware/error-handling).
