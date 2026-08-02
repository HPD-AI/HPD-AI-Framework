using System.Text.Json;

namespace HPD.Base;

/// <summary>Defines the ibase JSON type info resolver composer contract.</summary>
public interface IBaseJsonTypeInfoResolverComposer
{
    /// <summary>Executes the compose and freeze operation.</summary>
    JsonSerializerOptions ComposeAndFreeze(
        IEnumerable<IBaseJsonTypeInfoContributor> contributors);
}
