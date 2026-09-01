using System.Text.Json;
using HPD.Agent.Permissions;

namespace HPD.Agent.TUI.Interactions;

/// <summary>Marks a TUI renderer whose presentation identity is generated from its generic contract.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PermissionPresentationRendererAttribute : Attribute;

/// <summary>Renders one typed permission presentation while retaining the standard event protocol.</summary>
/// <typeparam name="TPresentation">The exact presentation type declared by the backend policy.</typeparam>
public interface IPermissionPresentationRenderer<TPresentation>
{
    /// <summary>Renders the presentation and returns one normalized server-choice decision.</summary>
    ValueTask<PermissionDecision> RenderAsync(
        TPresentation presentation,
        PermissionChoiceSet choices,
        CancellationToken cancellationToken);
}

internal interface IPermissionPresentationRendererAdapter
{
    Type PresentationType { get; }
    ValueTask<PermissionDecision> RenderAsync(
        JsonElement payload,
        PermissionChoiceSet choices,
        CancellationToken cancellationToken);
}

internal sealed class PermissionPresentationRendererAdapter<TPresentation>(
    IPermissionPresentationRenderer<TPresentation> renderer) : IPermissionPresentationRendererAdapter
{
    public Type PresentationType => typeof(TPresentation);

    public ValueTask<PermissionDecision> RenderAsync(
        JsonElement payload,
        PermissionChoiceSet choices,
        CancellationToken cancellationToken)
    {
        var presentation = payload.Deserialize<TPresentation>() ??
            throw new InvalidOperationException(
                $"Permission presentation '{typeof(TPresentation)}' could not be decoded.");
        return renderer.RenderAsync(presentation, choices, cancellationToken);
    }
}

internal sealed class PermissionPresentationRendererRegistry
{
    private readonly Dictionary<string, IPermissionPresentationRendererAdapter> _renderers =
        new(StringComparer.Ordinal);
    private bool _frozen;

    public void Add<TPresentation>(
        string presentationId,
        IPermissionPresentationRenderer<TPresentation> renderer)
    {
        if (_frozen) throw new InvalidOperationException("Permission presentation composition is frozen.");
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationId);
        ArgumentNullException.ThrowIfNull(renderer);
        var declaredId = typeof(TPresentation)
            .GetCustomAttributes(typeof(PermissionPresentationAttribute), inherit: false)
            .OfType<PermissionPresentationAttribute>()
            .SingleOrDefault()?.Id ?? throw new InvalidOperationException(
                $"Permission presentation type '{typeof(TPresentation)}' has no presentation identity.");
        if (!string.Equals(declaredId, presentationId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Permission renderer ID '{presentationId}' does not match presentation ID '{declaredId}'.");
        if (!_renderers.TryAdd(
                presentationId,
                new PermissionPresentationRendererAdapter<TPresentation>(renderer)))
            throw new InvalidOperationException(
                $"A permission renderer is already registered for '{presentationId}'.");
    }

    public void Freeze() => _frozen = true;

    public bool TryGet(string presentationId, out IPermissionPresentationRendererAdapter renderer) =>
        _renderers.TryGetValue(presentationId, out renderer!);
}
