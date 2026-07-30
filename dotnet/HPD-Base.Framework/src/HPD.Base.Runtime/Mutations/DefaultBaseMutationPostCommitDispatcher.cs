using System.Diagnostics;
using HPD.Base.Events;
using HPD.Base.Observability;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Events;
using HPD.Base.Runtime.Observability;
using HPD.Base.Runtime.Policy;

namespace HPD.Base.Runtime.Mutations;

internal interface IBaseMutationPostCommitDispatcher
{
    ValueTask<BaseRecordBatchItemResult> DispatchAsync(
        BaseMutationAttempt attempt,
        PrincipalContext principal);
}

internal sealed class DefaultBaseMutationPostCommitDispatcher(
    IBaseRecordRedactor redactor,
    IBaseEventFactory eventFactory,
    IBaseEventDispatcher eventDispatcher) : IBaseMutationPostCommitDispatcher
{
    public async ValueTask<BaseRecordBatchItemResult> DispatchAsync(
        BaseMutationAttempt attempt,
        PrincipalContext principal)
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

        var operation = mutation.CommittedOperation switch
        {
            BaseCommittedRecordMutationKind.Create => BaseOperationKind.Create,
            BaseCommittedRecordMutationKind.Patch => BaseOperationKind.Patch,
            BaseCommittedRecordMutationKind.Replace => BaseOperationKind.Replace,
            BaseCommittedRecordMutationKind.Delete => BaseOperationKind.Delete,
            _ => throw new InvalidOperationException("Unsupported committed mutation kind.")
        };
        var @event = eventFactory.CreateRecordMutationEvent(
            operation,
            command.Context,
            principal,
            command.Collection,
            before,
            after,
            mutation.ChangedFields,
            mutation.Event.EventId);
        var guarantee = mutation.Event.Guarantee;
        using var activity = HPDBaseRuntimeTelemetry.StartEventDispatch(command.Context, @event.Type);
        var startedAt = Stopwatch.GetTimestamp();
        var dispatched = await eventDispatcher.DispatchMutationAsync(
            @event,
            guarantee,
            CancellationToken.None).ConfigureAwait(false);
        dispatched = HPDBaseRuntimeTelemetry.FinishEventDispatch(
            activity,
            dispatched,
            command.Context,
            startedAt);

        var events = Merge(mutation.Event, dispatched.Value);
        var warnings = dispatched.Warnings;
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
            Warnings = warnings
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
