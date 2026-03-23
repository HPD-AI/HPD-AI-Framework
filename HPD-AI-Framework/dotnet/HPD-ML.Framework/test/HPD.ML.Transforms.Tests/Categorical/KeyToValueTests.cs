namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class KeyToValueTests
{
    private static readonly string[] KeyValues = ["A", "B", "C"];

    [Fact]
    public void K2V_MapsIntToString()
    {
        var data = TestHelper.Data(("V", new int[] { 0, 1, 2 }));
        var transform = new KeyToValueTransform("V", KeyValues);
        var result = transform.Apply(data);
        var values = TestHelper.CollectString(result, "V");
        Assert.Equal(["A", "B", "C"], values);
    }

    [Fact]
    public void K2V_OutOfRange_ReturnsEmpty()
    {
        var data = TestHelper.Data(("V", new int[] { 99 }));
        var transform = new KeyToValueTransform("V", KeyValues);
        var result = transform.Apply(data);
        var values = TestHelper.CollectString(result, "V");
        Assert.Equal([""], values);
    }

    [Fact]
    public void K2V_NegativeKey_ReturnsEmpty()
    {
        var data = TestHelper.Data(("V", new int[] { -1 }));
        var transform = new KeyToValueTransform("V", KeyValues);
        var result = transform.Apply(data);
        var values = TestHelper.CollectString(result, "V");
        Assert.Equal([""], values);
    }

    [Fact]
    public void K2V_RoundTrip_WithV2K()
    {
        var mapping = new Dictionary<string, int> { ["X"] = 0, ["Y"] = 1, ["Z"] = 2 };
        var keyValues = new[] { "X", "Y", "Z" };
        var data = TestHelper.Data(("V", new string[] { "X", "Y", "Z", "Y" }));

        var v2k = new ValueToKeyTransform("V", mapping);
        var keyed = v2k.Apply(data);
        var k2v = new KeyToValueTransform("V", keyValues);
        var restored = k2v.Apply(keyed);

        var values = TestHelper.CollectString(restored, "V");
        Assert.Equal(["X", "Y", "Z", "Y"], values);
    }
}
