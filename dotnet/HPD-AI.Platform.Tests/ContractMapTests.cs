using System.Text;
using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class StudioContractMapTests
{
    [Fact]
    public void L41_identifiers_and_text_bounds_match_the_browser_runtime()
    {
        Assert.Throws<ArgumentException>(() => Type("Message", "{\"kind\":\"boolean\"}"));
        Assert.Throws<ArgumentException>(() => Type("request", "{\"kind\":\"array\",\"elementTypeId\":\"Message\",\"minItems\":0,\"maxItems\":1}"));
        Assert.Throws<ArgumentException>(() => Type("request", $"{{\"kind\":\"string\",\"minLength\":0,\"maxLength\":1,\"format\":\"{new string('a', 129)}\"}}"));
    }

    [Fact]
    public void Contract_map_validates_reachability_node_checksums_and_qualified_ownership()
    {
        StudioNamedTypeContract error = Type("error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}");
        StudioNamedTypeContract message = Type("message", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}");
        StudioNamedTypeContract request = Type("request", "{\"kind\":\"object\",\"properties\":[{\"name\":\"message\",\"wireName\":\"message\",\"typeId\":\"message\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        StudioNamedTypeContract result = Type("result", "{\"kind\":\"object\",\"properties\":[{\"name\":\"message\",\"wireName\":\"message\",\"typeId\":\"message\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        StudioEndpointContract endpoint = StudioEndpointContract.Create("base.echo", 1, StudioTransportMethod.Post, "/echo",
            StudioEndpointAudience.ControlPlane, StudioTransportKind.SameOriginHttp, "request", request.NodeChecksum,
            "result", result.NodeChecksum, "error", error.NodeChecksum, 1024, 1024, TimeSpan.FromSeconds(1));
        StudioMethodBinding method = StudioMethodBinding.Create("base.echo.invoke", StudioMethodKind.Execute,
            "base", "base.echo", "base.echo", "request", "result");

        StudioContractMap map = StudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result], [endpoint], [method], new HashSet<(string, string)> { ("base", "base.echo") });
        Assert.Single(map.Methods);

        Assert.Throws<ArgumentException>(() => StudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result], [endpoint], [method], new HashSet<(string, string)> { ("other", "base.echo") }));
        StudioNamedTypeContract unused = Type("unused", "{\"kind\":\"boolean\"}");
        Assert.Throws<ArgumentException>(() => StudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result, unused], [endpoint], [method], new HashSet<(string, string)> { ("base", "base.echo") }));
        StudioMethodBinding substituted = StudioMethodBinding.Create("base.echo.invoke", StudioMethodKind.Execute,
            "base", "base.echo", "base.echo", "error", "result");
        Assert.Throws<ArgumentException>(() => StudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result], [endpoint], [substituted], new HashSet<(string, string)> { ("base", "base.echo") }));

        StudioNamedTypeContract a = Type("a", "{\"kind\":\"array\",\"elementTypeId\":\"b\",\"minItems\":0,\"maxItems\":1}");
        StudioNamedTypeContract b = Type("b", "{\"kind\":\"array\",\"elementTypeId\":\"a\",\"minItems\":0,\"maxItems\":1}");
        StudioEndpointContract recursiveEndpoint = StudioEndpointContract.Create("base.recursive", 1, StudioTransportMethod.Post, "/recursive",
            StudioEndpointAudience.ControlPlane, StudioTransportKind.SameOriginHttp, "a", a.NodeChecksum, "b", b.NodeChecksum,
            "error", error.NodeChecksum, 1024, 1024, TimeSpan.FromSeconds(1));
        StudioMethodBinding recursiveMethod = StudioMethodBinding.Create("base.recursive.invoke", StudioMethodKind.Execute,
            "base", "base.recursive", "base.recursive", "a", "b");
        Assert.Throws<ArgumentException>(() => StudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [a, b, error], [recursiveEndpoint], [recursiveMethod], new HashSet<(string, string)> { ("base", "base.recursive") }));
    }

    private static StudioNamedTypeContract Type(string id, string json) => StudioNamedTypeContract.Create(id, Encoding.UTF8.GetBytes(json));
    private static StudioSha256 Digest(byte value) => StudioSha256.Compute(Enumerable.Repeat(value, 32).ToArray());
}
