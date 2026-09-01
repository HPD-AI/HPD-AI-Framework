using System.Text.Json;
using HPDOS.ToolHarnesses.Middleware.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class LanguageProtocolGenerationTests
{
    [Fact]
    public void Pinned_inventory_is_complete_and_unique()
    {
        Assert.Equal("3.18.0", LanguageProtocolSource.Version);
        Assert.Equal(69, LanguageProtocolSource.RequestCount);
        Assert.Equal(26, LanguageProtocolSource.NotificationCount);
        Assert.Equal(95, LanguageProtocolFeatureInventory.All.Count);
        Assert.Equal(95, LanguageProtocolFeatureInventory.All.Select(x => x.Method).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Current_document_open_payload_uses_generated_contract()
    {
        var value = new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = "file:///workspace/example.cs",
                LanguageId = new LanguageKind("csharp"),
                Version = 3,
                Text = "class Example {}"
            }
        };

        var json = JsonSerializer.Serialize(value, LspJsonContext.Default.DidOpenTextDocumentParams);
        Assert.Contains("\"languageId\":\"csharp\"", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":3", json, StringComparison.Ordinal);
        Assert.Equal("textDocument/didOpen", LanguageProtocolDescriptors.DidOpenTextDocumentNotification.Method);
    }
}
