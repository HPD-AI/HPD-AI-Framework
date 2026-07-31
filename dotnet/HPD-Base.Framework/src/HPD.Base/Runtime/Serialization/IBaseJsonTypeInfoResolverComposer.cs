using System.Text.Json;

namespace HPD.Base;

public interface IBaseJsonTypeInfoResolverComposer
{
    JsonSerializerOptions ComposeAndFreeze(
        IEnumerable<IBaseJsonTypeInfoContributor> contributors);
}
