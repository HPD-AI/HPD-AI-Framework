using Cronos;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Hosting.Data;

namespace HPDAgent.Graph.Hosting.Lifecycle;

public interface IScheduleProvider
{
    string Name { get; }

    Task RegisterAsync(ScheduledGraph scheduledGraph, CancellationToken ct = default);

    Task UpdateAsync(ScheduledGraph scheduledGraph, CancellationToken ct = default);

    Task UnregisterAsync(string graphId, CancellationToken ct = default);

    Task<IReadOnlyList<ScheduleProviderStatus>> GetStatusAsync(CancellationToken ct = default);
}

public interface IScheduleTriggerProvider : IScheduleProvider
{
    Task<ScheduleTriggerResult> TriggerAsync(string graphId, CancellationToken ct = default);
}

public sealed record ScheduleProviderStatus
{
    public required string ProviderName { get; init; }
    public required string GraphId { get; init; }
    public bool Registered { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public string? Message { get; init; }
}

public sealed record ScheduleTriggerResult
{
    public required string GraphId { get; init; }
    public string? ExecutionId { get; init; }
    public required ScheduleTriggerStatus Status { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
}

public enum ScheduleTriggerStatus
{
    Started,
    Queued,
    Skipped,
    Disabled,
    NotFound,
    Failed
}

public sealed class SchedulingManager
{
    private const int MaxBackfillOccurrencesPerRun = 100;

    private readonly IScheduledGraphStore _scheduleStore;
    private readonly GraphManager _graphManager;
    private readonly IScheduleProvider _scheduleProvider;
    private readonly IScheduleTriggerProvider? _triggerProvider;
    private readonly TimeProvider _timeProvider;

    public SchedulingManager(
        IScheduledGraphStore scheduleStore,
        GraphManager graphManager,
        IScheduleProvider scheduleProvider,
        TimeProvider? timeProvider = null)
    {
        _scheduleStore = scheduleStore ?? throw new ArgumentNullException(nameof(scheduleStore));
        _graphManager = graphManager ?? throw new ArgumentNullException(nameof(graphManager));
        _scheduleProvider = scheduleProvider ?? throw new ArgumentNullException(nameof(scheduleProvider));
        _triggerProvider = scheduleProvider as IScheduleTriggerProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ScheduledGraphDto> CreateScheduleAsync(
        string graphId,
        CreateScheduleRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentNullException.ThrowIfNull(request);

        _ = await _graphManager.GetDefinitionAsync(graphId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Graph definition '{graphId}' was not found.");

        var now = _timeProvider.GetUtcNow();
        var scheduled = new ScheduledGraph
        {
            GraphId = graphId,
            Schedule = request.Schedule,
            Enabled = request.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
            NextRunAt = request.Enabled ? CalculateNextRun(request.Schedule, now) : null,
            Metadata = request.Schedule.Metadata
        };

        await _scheduleStore.SaveAsync(scheduled, ct).ConfigureAwait(false);
        await _scheduleProvider.RegisterAsync(scheduled, ct).ConfigureAwait(false);
        return WorkflowDtoMapper.ToScheduledGraphDto(scheduled);
    }

    public async Task<ScheduledGraphDto?> GetScheduleAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);

        var scheduled = await _scheduleStore.LoadAsync(graphId, ct).ConfigureAwait(false);
        return scheduled is null ? null : WorkflowDtoMapper.ToScheduledGraphDto(scheduled);
    }

    public async Task<IReadOnlyList<ScheduledGraphDto>> ListSchedulesAsync(CancellationToken ct = default)
    {
        var schedules = await _scheduleStore.ListAsync(ct).ConfigureAwait(false);
        return schedules.Select(WorkflowDtoMapper.ToScheduledGraphDto).ToList();
    }

    public async Task<ScheduledGraphDto> UpdateScheduleAsync(
        string graphId,
        UpdateScheduleRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentNullException.ThrowIfNull(request);

        var existing = await _scheduleStore.LoadAsync(graphId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Schedule for graph '{graphId}' was not found.");

        var now = _timeProvider.GetUtcNow();
        var enabled = request.Enabled ?? existing.Enabled;
        var updated = existing with
        {
            Schedule = request.Schedule,
            Enabled = enabled,
            UpdatedAt = now,
            NextRunAt = enabled ? CalculateNextRun(request.Schedule, now) : null,
            Metadata = request.Schedule.Metadata
        };

        await _scheduleStore.SaveAsync(updated, ct).ConfigureAwait(false);
        await _scheduleProvider.UpdateAsync(updated, ct).ConfigureAwait(false);
        return WorkflowDtoMapper.ToScheduledGraphDto(updated);
    }

    public async Task DeleteScheduleAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);

        await _scheduleStore.DeleteAsync(graphId, ct).ConfigureAwait(false);
        await _scheduleProvider.UnregisterAsync(graphId, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ScheduleProviderStatus>> GetProviderStatusAsync(CancellationToken ct = default)
    {
        return _scheduleProvider.GetStatusAsync(ct);
    }

    public Task<ScheduleTriggerResult> TriggerAsync(string graphId, CancellationToken ct = default)
    {
        if (_triggerProvider is null)
        {
            throw new NotSupportedException($"Schedule provider '{_scheduleProvider.Name}' does not support direct triggering.");
        }

        return _triggerProvider.TriggerAsync(graphId, ct);
    }

    public async Task<IReadOnlyList<ScheduleTriggerResult>> RunDueSchedulesAsync(CancellationToken ct = default)
    {
        if (_triggerProvider is null)
        {
            throw new NotSupportedException($"Schedule provider '{_scheduleProvider.Name}' does not support direct triggering.");
        }

        var now = _timeProvider.GetUtcNow();
        var schedules = await _scheduleStore.ListAsync(ct).ConfigureAwait(false);
        var results = new List<ScheduleTriggerResult>();

        foreach (var schedule in schedules)
        {
            ct.ThrowIfCancellationRequested();

            if (!schedule.Enabled)
            {
                continue;
            }

            var nextRunAt = schedule.NextRunAt ?? CalculateNextRun(schedule.Schedule, now);
            if (nextRunAt is null || nextRunAt > now)
            {
                continue;
            }

            if (schedule.NextRunAt is null)
            {
                await _scheduleStore.SaveAsync(schedule with
                {
                    UpdatedAt = now,
                    NextRunAt = nextRunAt
                }, ct).ConfigureAwait(false);
            }

            switch (schedule.Schedule.MisfirePolicy)
            {
                case ScheduleMisfirePolicyConfig.Skip when nextRunAt < now:
                    results.Add(await SkipMissedScheduleAsync(schedule, now, ct).ConfigureAwait(false));
                    break;

                case ScheduleMisfirePolicyConfig.RunAllMissed:
                    var missedOccurrences = CountMissedOccurrences(schedule.Schedule, nextRunAt.Value, now);
                    for (var i = 0; i < missedOccurrences; i++)
                    {
                        results.Add(await _triggerProvider.TriggerAsync(schedule.GraphId, ct).ConfigureAwait(false));
                    }
                    break;

                default:
                    results.Add(await _triggerProvider.TriggerAsync(schedule.GraphId, ct).ConfigureAwait(false));
                    break;
            }
        }

        return results;
    }

    private async Task<ScheduleTriggerResult> SkipMissedScheduleAsync(
        ScheduledGraph schedule,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var nextRunAt = CalculateNextRun(schedule.Schedule, now);
        var updated = schedule with
        {
            UpdatedAt = now,
            NextRunAt = nextRunAt
        };

        await _scheduleStore.SaveAsync(updated, ct).ConfigureAwait(false);

        return new ScheduleTriggerResult
        {
            GraphId = schedule.GraphId,
            Status = ScheduleTriggerStatus.Skipped,
            Message = "Missed schedule occurrence skipped by misfire policy.",
            NextRunAt = nextRunAt
        };
    }

    internal static DateTimeOffset? CalculateNextRun(GraphScheduleConfig schedule, DateTimeOffset now)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var expression = CronExpression.Parse(schedule.CronExpression);
        var next = expression.GetNextOccurrence(now.UtcDateTime, timeZone);

        if (next is null)
        {
            return null;
        }

        return new DateTimeOffset(next.Value, timeZone.GetUtcOffset(next.Value));
    }

    private static int CountMissedOccurrences(
        GraphScheduleConfig schedule,
        DateTimeOffset firstMissedOccurrence,
        DateTimeOffset now)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var expression = CronExpression.Parse(schedule.CronExpression);
        var count = 0;
        var current = firstMissedOccurrence;

        while (current <= now && count < MaxBackfillOccurrencesPerRun)
        {
            count++;

            var next = expression.GetNextOccurrence(current.UtcDateTime, timeZone);
            if (next is null)
            {
                break;
            }

            current = new DateTimeOffset(next.Value, timeZone.GetUtcOffset(next.Value));
        }

        return count;
    }
}

public sealed class InProcessCronScheduleProvider : IScheduleTriggerProvider
{
    private readonly IScheduledGraphStore _scheduleStore;
    private readonly IWorkflowExecutionStore _executionStore;
    private readonly GraphManager _graphManager;
    private readonly ExecutionManager _executionManager;
    private readonly TimeProvider _timeProvider;

    public InProcessCronScheduleProvider(
        IScheduledGraphStore scheduleStore,
        IWorkflowExecutionStore executionStore,
        GraphManager graphManager,
        ExecutionManager executionManager,
        TimeProvider? timeProvider = null)
    {
        _scheduleStore = scheduleStore ?? throw new ArgumentNullException(nameof(scheduleStore));
        _executionStore = executionStore ?? throw new ArgumentNullException(nameof(executionStore));
        _graphManager = graphManager ?? throw new ArgumentNullException(nameof(graphManager));
        _executionManager = executionManager ?? throw new ArgumentNullException(nameof(executionManager));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "in-process-cronos";

    public Task RegisterAsync(ScheduledGraph scheduledGraph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scheduledGraph);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ScheduledGraph scheduledGraph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scheduledGraph);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ScheduleProviderStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        var schedules = await _scheduleStore.ListAsync(ct).ConfigureAwait(false);
        return schedules.Select(schedule => new ScheduleProviderStatus
        {
            ProviderName = Name,
            GraphId = schedule.GraphId,
            Registered = true,
            Enabled = schedule.Enabled,
            LastRunAt = schedule.LastRunAt,
            NextRunAt = schedule.NextRunAt
        }).ToList();
    }

    public async Task<ScheduleTriggerResult> TriggerAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);

        var scheduled = await _scheduleStore.LoadAsync(graphId, ct).ConfigureAwait(false);
        if (scheduled is null)
        {
            return new ScheduleTriggerResult
            {
                GraphId = graphId,
                Status = ScheduleTriggerStatus.NotFound,
                Message = $"Schedule for graph '{graphId}' was not found."
            };
        }

        if (!scheduled.Enabled)
        {
            return new ScheduleTriggerResult
            {
                GraphId = graphId,
                Status = ScheduleTriggerStatus.Disabled,
                Message = $"Schedule for graph '{graphId}' is disabled.",
                NextRunAt = scheduled.NextRunAt
            };
        }

        var activeExecution = await GetActiveExecutionAsync(graphId, ct).ConfigureAwait(false);
        var startImmediately = true;

        if (activeExecution is not null)
        {
            switch (scheduled.Schedule.ConcurrencyPolicy)
            {
                case ScheduleConcurrencyPolicyConfig.SkipIfRunning:
                    return await SaveTriggeredScheduleAsync(
                        scheduled,
                        ScheduleTriggerStatus.Skipped,
                        executionId: null,
                        message: $"Skipped because execution '{activeExecution.ExecutionId}' is already active.",
                        ct).ConfigureAwait(false);

                case ScheduleConcurrencyPolicyConfig.Queue:
                    startImmediately = false;
                    break;

                case ScheduleConcurrencyPolicyConfig.CancelPrevious:
                    await _executionManager.CancelAsync(graphId, activeExecution.ExecutionId, ct).ConfigureAwait(false);
                    break;
            }
        }

        WorkflowExecutionDto execution;
        try
        {
            execution = await _graphManager.CreateExecutionAsync(
                graphId,
                new ExecuteWorkflowRequest
                {
                    Input = scheduled.Schedule.DefaultInput,
                    Timeout = scheduled.Schedule.Timeout,
                    TriggeredBy = $"schedule:{Name}",
                    Mode = WorkflowExecutionMode.Background,
                    StartImmediately = startImmediately
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await SaveFailedTriggerAsync(scheduled, ex.Message, ct).ConfigureAwait(false);
        }

        return await SaveTriggeredScheduleAsync(
            scheduled,
            startImmediately ? ScheduleTriggerStatus.Started : ScheduleTriggerStatus.Queued,
            execution.ExecutionId,
            startImmediately ? "Scheduled execution started." : "Scheduled execution queued.",
            ct).ConfigureAwait(false);
    }

    private async Task<WorkflowExecution?> GetActiveExecutionAsync(string graphId, CancellationToken ct)
    {
        var executions = await _executionStore.ListAsync(graphId, ct).ConfigureAwait(false);
        return executions
            .Where(static execution => execution.Status is
                WorkflowExecutionStatus.Created or
                WorkflowExecutionStatus.Running or
                WorkflowExecutionStatus.Suspended or
                WorkflowExecutionStatus.Polling)
            .OrderByDescending(static execution => execution.StartedAt ?? execution.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<ScheduleTriggerResult> SaveTriggeredScheduleAsync(
        ScheduledGraph scheduled,
        ScheduleTriggerStatus status,
        string? executionId,
        string message,
        CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var nextRunAt = SchedulingManager.CalculateNextRun(scheduled.Schedule, now);
        var updated = scheduled with
        {
            LastRunAt = now,
            NextRunAt = nextRunAt,
            UpdatedAt = now
        };

        await _scheduleStore.SaveAsync(updated, ct).ConfigureAwait(false);

        return new ScheduleTriggerResult
        {
            GraphId = scheduled.GraphId,
            ExecutionId = executionId,
            Status = status,
            Message = message,
            NextRunAt = nextRunAt
        };
    }

    private async Task<ScheduleTriggerResult> SaveFailedTriggerAsync(
        ScheduledGraph scheduled,
        string message,
        CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var nextRunAt = scheduled.Schedule.MaxRetries > 0
            ? now + (scheduled.Schedule.RetryAfter ?? TimeSpan.Zero)
            : SchedulingManager.CalculateNextRun(scheduled.Schedule, now);

        var updated = scheduled with
        {
            UpdatedAt = now,
            NextRunAt = nextRunAt
        };

        await _scheduleStore.SaveAsync(updated, ct).ConfigureAwait(false);

        return new ScheduleTriggerResult
        {
            GraphId = scheduled.GraphId,
            Status = ScheduleTriggerStatus.Failed,
            Message = message,
            NextRunAt = nextRunAt
        };
    }
}
