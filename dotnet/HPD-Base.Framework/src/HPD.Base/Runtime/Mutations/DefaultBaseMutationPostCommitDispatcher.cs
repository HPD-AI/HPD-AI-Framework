using System.Diagnostics;

namespace HPD.Base;

internal interface IBaseMutationPostCommitDispatcher
{
    /// <summary>Executes the dispatch async operation.</summary>
    ValueTask<BaseRecordBatchItemResult> DispatchAsync(
        BaseMutationAttempt attempt,
        PrincipalContext principal);

    /// <summary>Projects a stored committed receipt without publishing post-commit work.</summary>
    ValueTask<BaseRecordBatchItemResult> ReplayAsync(BaseMutationAttempt attempt, PrincipalContext principal);
}

internal sealed class DefaultBaseMutationPostCommitDispatcher(
    IBaseRecordRedactor redactor,
    IBaseEventFactory eventFactory,
    IBaseEventDispatcher eventDispatcher,
    BaseSubjectLifecycleHintHub lifecycleHints,
    BaseSubjectRetirementControlDispatcher retirementControls) : IBaseMutationPostCommitDispatcher
{
    /// <summary>Executes the dispatch async operation.</summary>
    public async ValueTask<BaseRecordBatchItemResult> DispatchAsync(
        BaseMutationAttempt attempt,
        PrincipalContext principal)
        => await ProjectAsync(attempt, principal, dispatch: true).ConfigureAwait(false);

    public ValueTask<BaseRecordBatchItemResult> ReplayAsync(BaseMutationAttempt attempt, PrincipalContext principal) =>
        ProjectAsync(attempt, principal, dispatch: false);

    private async ValueTask<BaseRecordBatchItemResult> ProjectAsync(BaseMutationAttempt attempt, PrincipalContext principal, bool dispatch)
    {
        var command = attempt.Command;
        var mutation = attempt.Mutation!;
        var view = BasePolicyRuntimeSimulation.ViewFor(principal, command.Context);
        var before = mutation.Before;
        var after = mutation.After;
        if (attempt.Policy is not null)
        {
            if (before is not null)
                before = redactor.RedactRecord(before, command.Collection, attempt.Policy, view);
            if (after is not null)
                after = redactor.RedactRecord(after, command.Collection, attempt.Policy, view);
        }
        var record = after;

        DeleteResult? delete = null;
        if (mutation.CommittedOperation == BaseCommittedRecordMutationKind.Delete)
        {
            var previous = command.Delete?.ReturnPrevious == true ? before : null;
            delete = mutation.Delete is null
                ? new DeleteResult { Id = mutation.Before!.Id, Deleted = true, Previous = previous }
                : mutation.Delete with { Previous = previous };
        }

        EventReference[] events;
        OperationWarning[]? warnings;
        if (dispatch)
        {
            var operation = mutation.CommittedOperation switch
            {
                BaseCommittedRecordMutationKind.Create => BaseOperationKind.Create,
                BaseCommittedRecordMutationKind.Patch => BaseOperationKind.Patch,
                BaseCommittedRecordMutationKind.Replace => BaseOperationKind.Replace,
                BaseCommittedRecordMutationKind.Delete => BaseOperationKind.Delete,
                _ => throw new InvalidOperationException("Unsupported committed mutation kind.")
            };
            var @event = eventFactory.CreateRecordMutationEvent(operation, command.Context, principal, command.Collection, before, after, mutation.ChangedFields, mutation.Event.EventId);
            using var activity = HPDBaseRuntimeTelemetry.StartEventDispatch(command.Context, @event.Type);
            var startedAt = Stopwatch.GetTimestamp();
            var dispatched = await eventDispatcher.DispatchMutationAsync(@event, mutation.Event.Guarantee, CancellationToken.None).ConfigureAwait(false);
            dispatched = HPDBaseRuntimeTelemetry.FinishEventDispatch(activity, dispatched, command.Context, startedAt);
            events = Merge(mutation.Event, dispatched.Value);
            warnings = dispatched.Warnings;
            if (mutation.SubjectLifecycle is { } lifecycle
                && lifecycle.PreviousState != lifecycle.ResultingState)
            {
                lifecycleHints.Publish(lifecycle);
                try { await retirementControls.ReconcileAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) { }
            }
        }
        else
        {
            events = [mutation.Event];
            warnings = null;
        }
        return new BaseRecordBatchItemResult
        {
            ItemId = command.ItemId,
            Index = command.Index,
            Kind = command.Kind,
            Disposition = BaseRecordBatchItemDisposition.Committed,
            Status = attempt.Status,
            Record = command.Kind == BaseRecordMutationKind.Upsert ? null : record,
            Delete = delete,
            Upsert = command.Kind == BaseRecordMutationKind.Upsert
                ? new RecordUpsertResult
                {
                    Outcome = mutation.UpsertOutcome!.Value,
                    Record = record!
                }
                : null,
            Revision = attempt.Revision,
            Events = events,
            Warnings = warnings,
            SubjectLifecycle = mutation.SubjectLifecycle
        };
    }

    private static EventReference[] Merge(EventReference committed, EventReference[]? published)
    {
        if (published is null or { Length: 0 }) return [committed];
        return new[] { committed }.Concat(published)
            .GroupBy(static reference => reference.EventId, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(reference => reference.Guarantee).First())
            .ToArray();
    }

    private static OperationWarning[]? Combine(OperationWarning[]? first, OperationWarning[]? second)
    {
        if (first is null or { Length: 0 }) return second is { Length: > 0 } ? second : null;
        if (second is null or { Length: 0 }) return first;
        return [.. first, .. second];
    }
}
