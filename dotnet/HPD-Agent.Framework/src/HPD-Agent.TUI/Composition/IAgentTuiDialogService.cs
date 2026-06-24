using HPD.Agent.TUI.Models;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiDialogService
{
    bool HasOpenDialog { get; }

    Task<TResult?> ShowAsync<TResult>(
        string key,
        Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
        CancellationToken cancellationToken = default);

    bool Close(string key);

    bool CloseTop();

    Task<bool?> ConfirmAsync(
        string title,
        bool? defaultValue = null,
        CancellationToken cancellationToken = default);

    Task<T?> SelectAsync<T>(
        string title,
        IReadOnlyList<T> options,
        Func<T, string> titleSelector,
        CancellationToken cancellationToken = default);

    Task<string?> InputAsync(
        string title,
        string? defaultValue = null,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default);

    Task<string?> SecretInputAsync(
        string title,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default);
}

public sealed class AgentTuiDialogContext<TResult>
{
    private readonly Action<TResult?> _complete;

    public AgentTuiDialogContext(
        string key,
        AgentTuiNavigationModel navigation,
        Action<TResult?> complete)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    }

    public string Key { get; }

    public AgentTuiNavigationModel Navigation { get; }

    public void Submit(TResult? result) => _complete(result);

    public void Cancel() => _complete(default);
}
