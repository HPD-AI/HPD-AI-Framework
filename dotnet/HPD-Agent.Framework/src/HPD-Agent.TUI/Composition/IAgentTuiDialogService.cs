using HPD.Agent.TUI.Models;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiDialogService
{
    bool HasOpenDialog { get; }

    Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
        string key,
        Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
        CancellationToken cancellationToken = default);

    bool Close(string key);

    bool CloseTop();

    Task<AgentTuiDialogResult<bool>> ConfirmAsync(
        string title,
        bool? defaultValue = null,
        CancellationToken cancellationToken = default);

    Task<AgentTuiDialogResult<T>> SelectAsync<T>(
        string title,
        IReadOnlyList<T> options,
        Func<T, string> titleSelector,
        CancellationToken cancellationToken = default);

    Task<AgentTuiDialogResult<string>> InputAsync(
        string title,
        string? defaultValue = null,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default);

    Task<AgentTuiDialogResult<string>> SecretInputAsync(
        string title,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default);

    async Task<TResult?> RunFlowAsync<TResult>(
        Func<AgentTuiDialogFlowContext, CancellationToken, ValueTask<TResult?>> flow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return await flow(new AgentTuiDialogFlowContext(this), cancellationToken).ConfigureAwait(false);
    }
}

public sealed class AgentTuiDialogContext<TResult>
{
    private readonly Action<AgentTuiDialogResult<TResult>> _complete;

    public AgentTuiDialogContext(
        string key,
        AgentTuiNavigationModel navigation,
        Action<AgentTuiDialogResult<TResult>> complete)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public string Key { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public void Submit(TResult result) => _complete(AgentTuiDialogResult<TResult>.Submitted(result));

    public void Cancel() => _complete(AgentTuiDialogResult<TResult>.Canceled());
}

public readonly record struct AgentTuiDialogResult<T>(
    AgentTuiDialogResultStatus Status,
    T? Value)
{
    public bool IsSubmitted => Status == AgentTuiDialogResultStatus.Submitted;

    public bool IsBack => Status == AgentTuiDialogResultStatus.Back;

    public bool IsCanceled => Status == AgentTuiDialogResultStatus.Canceled;

    public bool IsDismissed => Status == AgentTuiDialogResultStatus.Dismissed;

    public static AgentTuiDialogResult<T> Submitted(T value)
        => new(AgentTuiDialogResultStatus.Submitted, value);

    public static AgentTuiDialogResult<T> Back()
        => new(AgentTuiDialogResultStatus.Back, default);

    public static AgentTuiDialogResult<T> Canceled()
        => new(AgentTuiDialogResultStatus.Canceled, default);

    public static AgentTuiDialogResult<T> Dismissed()
        => new(AgentTuiDialogResultStatus.Dismissed, default);
}

public enum AgentTuiDialogResultStatus
{
    Submitted,
    Back,
    Canceled,
    Dismissed
}

public sealed class AgentTuiDialogFlowContext
{
    private readonly IAgentTuiDialogService _dialogs;

    public AgentTuiDialogFlowContext(IAgentTuiDialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public async ValueTask<AgentTuiDialogStepResult<T>> SelectAsync<T>(
        string title,
        IReadOnlyList<T> options,
        Func<T, string> titleSelector,
        CancellationToken cancellationToken = default)
    {
        var selected = await _dialogs.SelectAsync(title, options, titleSelector, cancellationToken)
            .ConfigureAwait(false);
        return Complete(selected);
    }

    public async ValueTask<AgentTuiDialogStepResult<string>> InputAsync(
        string title,
        string? defaultValue = null,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
    {
        var value = await _dialogs.InputAsync(title, defaultValue, allowEmpty, cancellationToken)
            .ConfigureAwait(false);
        return Complete(value);
    }

    public async ValueTask<AgentTuiDialogStepResult<string>> SecretInputAsync(
        string title,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
    {
        var value = await _dialogs.SecretInputAsync(title, allowEmpty, cancellationToken)
            .ConfigureAwait(false);
        return Complete(value);
    }

    public async ValueTask<AgentTuiDialogStepResult<bool>> ConfirmAsync(
        string title,
        bool? defaultValue = null,
        CancellationToken cancellationToken = default)
    {
        var value = await _dialogs.ConfirmAsync(title, defaultValue, cancellationToken)
            .ConfigureAwait(false);
        return Complete(value);
    }

    private AgentTuiDialogStepResult<T> Complete<T>(AgentTuiDialogResult<T> result)
    {
        if (result.IsSubmitted && result.Value is not null)
        {
            return AgentTuiDialogStepResult<T>.Submitted(result.Value);
        }

        return result.IsCanceled
            ? AgentTuiDialogStepResult<T>.Canceled()
            : AgentTuiDialogStepResult<T>.Back();
    }
}

public readonly record struct AgentTuiDialogStepResult<T>(
    AgentTuiDialogStepStatus Status,
    T? Value)
{
    public bool IsSubmitted => Status == AgentTuiDialogStepStatus.Submitted;

    public bool IsBack => Status == AgentTuiDialogStepStatus.Back;

    public bool IsCanceled => Status == AgentTuiDialogStepStatus.Canceled;

    public static AgentTuiDialogStepResult<T> Submitted(T value)
        => new(AgentTuiDialogStepStatus.Submitted, value);

    public static AgentTuiDialogStepResult<T> Back()
        => new(AgentTuiDialogStepStatus.Back, default);

    public static AgentTuiDialogStepResult<T> Canceled()
        => new(AgentTuiDialogStepStatus.Canceled, default);
}

public enum AgentTuiDialogStepStatus
{
    Submitted,
    Back,
    Canceled
}
