using System.Text.Json;
using HPD.Base.Serialization;

namespace HPD.Base.Runtime.Serialization;

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
