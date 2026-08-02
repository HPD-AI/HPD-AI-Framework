using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseJsonOptionsProvider : IBaseJsonOptionsProvider
{
    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseJsonOptionsProvider(
        IBaseJsonTypeInfoResolverComposer composer,
        IEnumerable<IBaseJsonTypeInfoContributor> contributors)
    {
        Options = composer.ComposeAndFreeze(contributors);
    }

    /// <summary>Gets the options.</summary>
    public JsonSerializerOptions Options { get; }
}
