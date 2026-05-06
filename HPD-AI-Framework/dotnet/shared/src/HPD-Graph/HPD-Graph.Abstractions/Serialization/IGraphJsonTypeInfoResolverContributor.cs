using System.Text.Json.Serialization.Metadata;

namespace HPDAgent.Graph.Abstractions.Serialization;

public interface IGraphJsonTypeInfoResolverContributor
{
    IJsonTypeInfoResolver Resolver { get; }
}
