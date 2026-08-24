using System.Text;
using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioContractMapTests
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
        BaseStudioNamedTypeContract error = Type("error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}");
        BaseStudioNamedTypeContract message = Type("message", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}");
        BaseStudioNamedTypeContract request = Type("request", "{\"kind\":\"object\",\"properties\":[{\"name\":\"message\",\"wireName\":\"message\",\"typeId\":\"message\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        BaseStudioNamedTypeContract result = Type("result", "{\"kind\":\"object\",\"properties\":[{\"name\":\"message\",\"wireName\":\"message\",\"typeId\":\"message\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        BaseStudioEndpointContract endpoint = BaseStudioEndpointContract.Create("base.echo", 1, BaseStudioTransportMethod.Post, "/echo",
            BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp, "request", request.NodeChecksum,
            "result", result.NodeChecksum, "error", error.NodeChecksum, 1024, 1024, TimeSpan.FromSeconds(1));
        BaseStudioMethodBinding method = BaseStudioMethodBinding.Create("base.echo.invoke", BaseStudioMethodKind.Execute,
            "base", "base.echo", "base.echo", "request", "result");

        BaseStudioContractMap map = BaseStudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result], [endpoint], [method], new HashSet<(string, string)> { ("base", "base.echo") });
        Assert.Single(map.Methods);

        Assert.Throws<ArgumentException>(() => BaseStudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result], [endpoint], [method], new HashSet<(string, string)> { ("other", "base.echo") }));
        BaseStudioNamedTypeContract unused = Type("unused", "{\"kind\":\"boolean\"}");
        Assert.Throws<ArgumentException>(() => BaseStudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result, unused], [endpoint], [method], new HashSet<(string, string)> { ("base", "base.echo") }));
        BaseStudioMethodBinding substituted = BaseStudioMethodBinding.Create("base.echo.invoke", BaseStudioMethodKind.Execute,
            "base", "base.echo", "base.echo", "error", "result");
        Assert.Throws<ArgumentException>(() => BaseStudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [error, message, request, result], [endpoint], [substituted], new HashSet<(string, string)> { ("base", "base.echo") }));

        BaseStudioNamedTypeContract a = Type("a", "{\"kind\":\"array\",\"elementTypeId\":\"b\",\"minItems\":0,\"maxItems\":1}");
        BaseStudioNamedTypeContract b = Type("b", "{\"kind\":\"array\",\"elementTypeId\":\"a\",\"minItems\":0,\"maxItems\":1}");
        BaseStudioEndpointContract recursiveEndpoint = BaseStudioEndpointContract.Create("base.recursive", 1, BaseStudioTransportMethod.Post, "/recursive",
            BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp, "a", a.NodeChecksum, "b", b.NodeChecksum,
            "error", error.NodeChecksum, 1024, 1024, TimeSpan.FromSeconds(1));
        BaseStudioMethodBinding recursiveMethod = BaseStudioMethodBinding.Create("base.recursive.invoke", BaseStudioMethodKind.Execute,
            "base", "base.recursive", "base.recursive", "a", "b");
        Assert.Throws<ArgumentException>(() => BaseStudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            Digest(1), Digest(2), [a, b, error], [recursiveEndpoint], [recursiveMethod], new HashSet<(string, string)> { ("base", "base.recursive") }));
    }

    private static BaseStudioNamedTypeContract Type(string id, string json) => BaseStudioNamedTypeContract.Create(id, Encoding.UTF8.GetBytes(json));
    private static BaseStudioSha256 Digest(byte value) => BaseStudioSha256.Compute(Enumerable.Repeat(value, 32).ToArray());
}
