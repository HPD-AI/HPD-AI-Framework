using HPD.Base;

namespace HPD.Base.AspNetCore;

internal sealed class BaseRealtimeSubjectLifecycleHintOwner(
    BaseSubjectLifecycleHintHub.Lease lease,
    IBaseSubjectLifecycleRuntime runtime,
    BaseSession session,
    BaseInstalledSubjectLifecycleConsumer installed,
    CancellationToken connectionToken,
    Func<BaseSubjectLifecycleCheckpoint, CancellationToken, Task> send,
    Func<Exception, CancellationToken, Task> fail) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
    private Task? _pump;
    internal bool IsCompleted => _pump?.IsCompleted ?? false;
    internal void Activate() => _pump = PumpAsync();

    private async Task PumpAsync()
    {
        try
        {
            await foreach (BaseSubjectLifecycleCommitEvidence evidence in lease.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                if (!installed.Definition.ObservedStates.Contains(evidence.ResultingState)) continue;
                BaseResult<BaseSubjectLifecycleCheckpoint> checkpoint = await runtime.CreateHintCheckpointAsync(
                    session, installed, evidence, _stop.Token).ConfigureAwait(false);
                if (checkpoint is not BaseSuccess<BaseSubjectLifecycleCheckpoint> success)
                    throw new BaseRealtimeFeedException(BaseRealtimeErrorCodes.ChannelUnauthorized, "The lifecycle hint is no longer authorized.");
                await send(success.Value, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception) { await fail(exception, CancellationToken.None).ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); lease.Dispose();
        if (_pump is not null) try { await _pump.ConfigureAwait(false); } catch { }
        _stop.Dispose();
    }
}
