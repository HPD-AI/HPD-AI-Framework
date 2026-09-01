using System.Text;
using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioShellAssetGraphTests
{
    [Fact]
    public void Embedded_shell_graph_is_content_addressed_and_deeply_owned()
    {
        var graph = new BaseStudioShellAssetGraph(BaseStudioShellContract.Current);

        Assert.True(graph.TryResolve("assets/hpd-studio-shell.js", out BaseStudioShellAsset javaScript));
        Assert.True(graph.TryResolve("assets/hpd-studio-shell.css", out BaseStudioShellAsset css));
        Assert.Equal("text/javascript; charset=utf-8", javaScript.ContentType);
        Assert.Equal("text/css; charset=utf-8", css.ContentType);
        byte[] first = javaScript.GetContent();
        first[0] ^= 0xff;
        Assert.NotEqual(first[0], javaScript.GetContent()[0]);
        Assert.Equal(32, graph.Checksum.ToArray().Length);
    }

    [Fact]
    public void Entry_document_binds_the_host_owned_route_prefix()
    {
        var graph = new BaseStudioShellAssetGraph(BaseStudioShellContract.Current);
        string document = Encoding.UTF8.GetString(graph.CreateEntryDocument("/studio"));

        Assert.Contains("<base href=\"/studio/\"", document, StringComparison.Ordinal);
        Assert.Matches("hpd-studio-shell\\.js\\?v=[a-f0-9]{64}", document);
        Assert.Matches("hpd-studio-shell\\.css\\?v=[a-f0-9]{64}", document);
        Assert.DoesNotContain("studio-config.js", document, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => graph.CreateEntryDocument("/../other"));
    }

    [Fact]
    public void Edition_catalog_is_explicit_canonical_and_independent_of_application_graph()
    {
        var catalog = new BaseStudioEditionAssetCatalog();
        BaseStudioAssetManifest asset = BaseStudioAssetManifest.Create("module.js", BaseStudioModuleNecessity.Required,
            BaseStudioShellContract.Current,
            [BaseStudioAssetSource.Create("module.js", BaseStudioAssetMediaType.JavaScriptModule, "export{}"u8)]);
        catalog.Add(BaseStudioEditionModuleAssetContribution.Create("zeta", 1,
            BaseStudioSha256.FromDigest(new byte[32]), asset));
        catalog.Add(BaseStudioEditionModuleAssetContribution.Create("alpha", 2,
            BaseStudioSha256.FromDigest(new byte[32]), asset));

        var provider = new BaseStudioEditionAssetCatalogProvider(catalog);
        Assert.Equal(["alpha", "zeta"], provider.GetRequiredCatalog().Select(static value => value.ModuleId));
        Assert.Throws<InvalidOperationException>(() => catalog.Add(
            BaseStudioEditionModuleAssetContribution.Create("later", 1, BaseStudioSha256.FromDigest(new byte[32]), asset)));
    }
}
