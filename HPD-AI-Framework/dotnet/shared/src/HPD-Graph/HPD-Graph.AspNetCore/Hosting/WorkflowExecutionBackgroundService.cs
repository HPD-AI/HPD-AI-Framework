using HPDAgent.Graph.Hosting.Lifecycle;
using Microsoft.Extensions.Hosting;

namespace HPDAgent.Graph.AspNetCore.Hosting;

internal sealed class WorkflowExecutionBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly SchedulingManager _schedulingManager;

    public WorkflowExecutionBackgroundService(
        IWorkflowExecutionRunner executionRunner,
        SchedulingManager schedulingManager)
    {
        _executionRunner = executionRunner ?? throw new ArgumentNullException(nameof(executionRunner));
        _schedulingManager = schedulingManager ?? throw new ArgumentNullException(nameof(schedulingManager));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _executionRunner.RequeueInterruptedAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _schedulingManager.RunDueSchedulesAsync(stoppingToken).ConfigureAwait(false);
                await _executionRunner.RunQueuedAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // A failing schedule or queued execution should not stop the dispatcher.
            }

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
