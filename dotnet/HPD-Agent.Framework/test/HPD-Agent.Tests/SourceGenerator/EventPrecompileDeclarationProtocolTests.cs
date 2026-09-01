public sealed class EventPrecompileDeclarationProtocolTests
{
    [Fact]
    public void Ordinary_compile_items_do_not_enter_the_protocol()
    {
        var source = SourceItem.Parse("/tmp/Event.cs|Event.cs");

        Assert.False(source.IsDeclaration);
        Assert.Empty(PrecompileDeclarationProtocol.Validate([source]));
    }

    [Fact]
    public void Stable_distinct_declarations_are_accepted()
    {
        var first = SourceItem.Parse("/tmp/A.cs|Generated/A.cs|example.events.a|Example/1");
        var second = SourceItem.Parse("/tmp/B.cs|Generated/B.cs|example.events.b|Example/1");

        Assert.Empty(PrecompileDeclarationProtocol.Validate([first, second]));
    }

    [Fact]
    public void Conflicting_identity_is_rejected_deterministically()
    {
        var first = SourceItem.Parse("/tmp/A.cs|Generated/A.cs|example.events|Example/1");
        var second = SourceItem.Parse("/tmp/B.cs|Generated/B.cs|example.events|Other/1");

        var errors = PrecompileDeclarationProtocol.Validate([second, first]);

        var error = Assert.Single(errors);
        Assert.Contains("example.events", error, StringComparison.Ordinal);
        Assert.Contains("conflicting", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/tmp/A.cs|A.cs||Example/1", "invalid DeclarationId")]
    [InlineData("/tmp/A.cs|A.cs|not allowed|Example/1", "invalid DeclarationId")]
    [InlineData("/tmp/A.cs|A.cs|example.events|", "Producer")]
    public void Invalid_metadata_is_rejected(string line, string reason)
    {
        var error = Assert.Single(PrecompileDeclarationProtocol.Validate([SourceItem.Parse(line)]));
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }
}
