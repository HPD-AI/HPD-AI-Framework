# HPD-Agent.FFI

HPD Agent FFI package for HPD applications.

## Install

```bash
dotnet add package HPD-Agent.FFI
```

## Thread-routed event subscriptions

Include [`hpd_agent_events.h`](./hpd_agent_events.h) to create a persistent native event
subscription. The subscription key is the complete UTF-8 `(session_id, thread_id)` pair.
`HPD_AGENT_EVENT_EXACT_THREAD` is the narrowest scope; child and descendant delivery must
be selected explicitly with one of the other frozen hierarchy values.

The callback receives one UTF-8 JSON `AgentEventDelivery` envelope containing `event` and
`route`. The route contains the origin thread, its root-to-origin path, and the optional
thread execution ID. The callback buffer is borrowed and valid only until the callback
returns, so native callers must copy it if they need to retain it.

The returned `hpd_subscription*` is caller-owned. External disposal prevents new callback
admission and waits for any callback already in progress. Disposal from the subscription's
own callback returns `HPD_SUBSCRIPTION_DISPOSE_FROM_CALLBACK` without changing the handle;
schedule disposal after that callback returns. Calls that mutate the same handle address
must be externally serialized.

Every failure returns a closed `hpd_subscribe_status`, leaves the output handle null, and
invokes no callback. Input buffers are borrowed only for the subscribe call and must contain
strict, non-empty UTF-8 keys.
