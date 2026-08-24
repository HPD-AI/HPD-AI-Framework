using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioAssetContractTests
{
    [Fact]
    public void Manifest_is_ordered_deeply_owned_and_deterministic()
    {
        byte[] entryBytes = Enumerable.Repeat((byte)2, 100).ToArray();
        byte[] cssBytes = Enumerable.Repeat((byte)3, 50).ToArray();
        BaseStudioAssetSource entry = BaseStudioAssetSource.Create(
            "assets/module.js", BaseStudioAssetMediaType.JavaScriptModule, entryBytes);
        BaseStudioAssetSource css = BaseStudioAssetSource.Create(
            "assets/module.css", BaseStudioAssetMediaType.Css, cssBytes);

        BaseStudioAssetManifest first = BaseStudioAssetManifest.Create(
            "assets/module.js", BaseStudioModuleNecessity.Required, BaseStudioShellContract.Current, [css, entry]);
        BaseStudioAssetManifest second = BaseStudioAssetManifest.Create(
            "assets/module.js", BaseStudioModuleNecessity.Required, BaseStudioShellContract.Current, [css, entry]);

        entryBytes[0] = 99;
        cssBytes[0] = 99;
        Assert.Equal(["assets/module.css", "assets/module.js"], first.Assets.Select(static asset => asset.Path));
        Assert.True(BaseStudioSha256.FixedTimeEquals(first.AssetGraphChecksum, second.AssetGraphChecksum));
        Assert.True(BaseStudioSha256.FixedTimeEquals(
            first.ShellContractChecksum,
            BaseStudioShellContract.Current.Checksum));
        Assert.True(BaseStudioSha256.FixedTimeEquals(
            first.Assets[1].Digest,
            BaseStudioSha256.Compute(Enumerable.Repeat((byte)2, 100).ToArray())));
        Assert.Throws<ArgumentException>(() => BaseStudioAssetManifest.Create(
            "assets/module.js", BaseStudioModuleNecessity.Required, BaseStudioShellContract.Current, [entry, css]));
    }

    [Theory]
    [InlineData("/absolute.js")]
    [InlineData("../escape.js")]
    [InlineData("assets//module.js")]
    [InlineData("assets\\module.js")]
    public void Asset_paths_fail_closed(string path)
    {
        Assert.Throws<ArgumentException>(() => BaseStudioAssetSource.Create(
            path, BaseStudioAssetMediaType.JavaScriptModule, new byte[] { 1 }));
    }

    [Fact]
    public void Entry_module_must_be_declared_as_javascript()
    {
        BaseStudioAssetSource css = BaseStudioAssetSource.Create(
            "assets/module.css", BaseStudioAssetMediaType.Css, new byte[] { 1 });
        Assert.Throws<ArgumentException>(() => BaseStudioAssetManifest.Create(
            "assets/module.js", BaseStudioModuleNecessity.Optional, BaseStudioShellContract.Current, [css]));
    }

    [Theory]
    [InlineData("assets/module.js?alternate")]
    [InlineData("assets/module.js#fragment")]
    [InlineData("assets/%2e%2e/module.js")]
    [InlineData("assets/module.js.map")]
    public void Browser_special_and_source_map_paths_fail_closed(string path)
        => Assert.Throws<ArgumentException>(() => BaseStudioAssetSource.Create(
            path, BaseStudioAssetMediaType.JavaScriptModule, new byte[] { 1 }));
}
