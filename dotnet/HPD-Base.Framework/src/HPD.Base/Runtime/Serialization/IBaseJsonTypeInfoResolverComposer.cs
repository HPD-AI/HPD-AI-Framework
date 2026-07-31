using System.Text.Json;
using HPD.Base.Serialization;

namespace HPD.Base.Runtime.Serialization;

public interface IBaseJsonTypeInfoResolverComposer
{
    JsonSerializerOptions ComposeAndFreeze(
        IEnumerable<IBaseJsonTypeInfoContributor> contributors);
}
