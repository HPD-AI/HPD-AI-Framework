using System.Text.Json.Serialization.Metadata;

namespace HPD.Graph.Abstractions.Serialization;

public interface IGraphJsonTypeInfoResolverContributor
{
    IJsonTypeInfoResolver Resolver { get; }
}
