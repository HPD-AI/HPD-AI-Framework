# Thread Execution Lifecycle

A thread execution is one accepted input that owns a thread's exclusive execution
slot. It is distinct from a message turn, model request, tool call, workflow run,
and the long-lived thread runtime.

## Lifecycle

1. A client submits an input for a session thread.
2. The coordinating runtime reserves that thread and assigns a
   `ThreadExecutionId`. A thread with an active execution rejects overlapping
   input as busy.
3. The coordinator commits `THREAD_EXECUTION_STARTED` before dispatching the
   accepted input.
4. The input flows through the normal HPD Agent event pipeline. Persistable
   events enter the journal and live observers receive the emitted events.
5. The coordinator commits exactly one `THREAD_EXECUTION_FINISHED` with a
   terminal outcome.
6. The coordinator releases the thread only after the finish event is durable.

The terminal outcome is one of:

- `Succeeded`, with no error;
- `Cancelled`, with no error;
- `Failed`, with a required structured `ThreadExecutionError`.

`FinishedAt` records when the accepted input reached its terminal outcome. A
finish event must correlate to its start through the same `ThreadExecutionId`.

## Live ownership and durable history

The runtime coordinator's `ActiveExecution` is authoritative for present-tense
ownership. Journal lifecycle events are authoritative historical facts used by
projections and rehydration; replaying an old start does not reserve the live
thread.

If recovery finds a committed start without a matching finish, the historical
projection reports `Interrupted`. `Interrupted` is a recovery projection, not a
normal `ThreadExecutionOutcome` emitted by a functioning coordinator. A process
restart does not silently restore live ownership from that dangling start.

The coordinator preserves ownership when finish persistence fails. Releasing the
slot before the terminal fact is durable could allow a second execution to
overlap an execution whose outcome was never recorded.

## Interruption

An interruption request carries `expectedThreadExecutionId`. The coordinator
accepts it only when that identifier matches the live active execution.

- no live execution produces `no_active_execution`;
- a different live execution produces `active_execution_mismatch`;
- a matching execution receives cancellation and eventually commits a
  `Cancelled` or `Failed` finish, according to its actual terminal result.

This correlation prevents a delayed cancellation request from affecting a newer
execution on the same thread.

## Background correlation

Work created during an execution may carry `OriginatingThreadExecutionId` after
the execution releases the thread. This field identifies provenance; it does not
extend ownership or make the originating execution active again.

## Hosting API

Hosting exposes historical executions through the thread `/executions` route and
present runtime state through `activeExecution`. These serve different questions:

- `/executions` describes what happened;
- `activeExecution` describes what owns the thread now.

TUI and other clients may project execution events for history, but cancellation
authority and present-tense busy state always come from authoritative runtime
state.
