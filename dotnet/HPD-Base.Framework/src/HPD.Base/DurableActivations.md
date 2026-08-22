# Durable activations

HPD.Base can store delayed work, scheduled occurrences, retries, claims, leases,
results, and operation receipts beside application records. The store—not an
in-process queue—is the authority. A worker can disappear after claiming work;
another worker later recovers the expired claim without losing the activation or
weakening idempotency.

Activation definitions are immutable application-graph assets. They bind the input
and result codecs, handler version, retry and lease limits, execution class,
authorization grants, and semantic checksum. Register a definition during application
construction:

```csharp
services.AddHPDBase(hpd => hpd
    .AddActivation(SendInvoiceActivation.Registration)
    .AddSchedule(NightlyInvoiceSchedule.Definition));
```

Static declarations are inert identities. Resolve executable authority from a
principal-bound session, then supply an identified request:

```csharp
BaseInstalledActivationHandle<SendInvoice, SendInvoiceResult> activation =
    session.Activations.Get(SendInvoiceActivation.Identity);

BaseActivationEnqueueResult created = (await activation.EnqueueAsync(
    new SendInvoice(invoiceId),
    BaseMutationRequestIdentity.Create(
        "billing", "send-invoice", requestId,
        BaseMutationRequestFingerprint.Create(requestFingerprint))))
    .RequireValue();
```

Service and System workers resolve a separate worker handle. Observation does not
claim or advance work; claiming is an explicit atomic operation over one finite due
observation:

```csharp
BaseInstalledActivationWorkerHandle<SendInvoice, SendInvoiceResult> worker =
    workerSession.Activations.GetWorker(SendInvoiceActivation.Identity);

BaseActivationDueObservation due = (await worker.ObserveDueAsync()).RequireValue();
BaseActivationDelivery<SendInvoice>? delivery = (await worker.TryClaimAsync(
    due.Token,
    claimIdentity)).RequireValue();

if (delivery is not null)
    await worker.CompleteAsync(delivery, new SendInvoiceResult("sent"), completionIdentity);
```

Handler dispatch can instead be hosted by calling `AddHPDBaseActivationWorkers`.
Handlers receive a restricted `BaseActivationContext`; it exposes claim-safe renewal
and deterministic child-operation identity, not SQL, provider transactions, service
location, or authority-minting primitives.

Three execution classes are available:

- `TransactionalOperation` runs a registered BASE operation and terminalizes the
  activation in the same provider transaction.
- `AtLeastOnceWorker` runs ordinary handler code. Durable child operations and
  receipts make retries safe; external side effects may repeat.
- `AtMostOnceEffect` persists effect-start authority before invoking the external
  effect. If the result cannot be known after executor loss, the activation becomes
  `OutcomeUnknown` and requires explicit reconciliation.

Schedules support one-shot, fixed-interval, cron, and calendar expressions. Occurrence
identity, misfire handling, overlap handling, priority aging, and retry jitter are
deterministic and provider-independent. Named zones use the graph-installed compiled
time-zone authority; providers never consult host time-zone databases.

Worker authority is available only to Service/System audiences. Interactive browser
and React clients cannot acquire claim, payload, fence, renewal, completion,
checkpoint, acknowledgement, or worker-result authority. Control-plane inspection is
a separate, currently authorized and disclosure-filtered surface.
