using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseJsonOptionsProvider : IBaseJsonOptionsProvider
{
    public DefaultBaseJsonOptionsProvider(
        IBaseJsonTypeInfoResolverComposer composer,
        IEnumerable<IBaseJsonTypeInfoContributor> contributors)
    {
        Options = composer.ComposeAndFreeze(contributors);
    }

    public JsonSerializerOptions Options { get; }
}
