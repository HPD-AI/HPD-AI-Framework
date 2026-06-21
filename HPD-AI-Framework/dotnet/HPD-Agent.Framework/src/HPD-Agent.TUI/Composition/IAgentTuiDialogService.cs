using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiDialogService
{
    bool HasOpenDialog { get; }

    void Show(string key, IComponent component);

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
