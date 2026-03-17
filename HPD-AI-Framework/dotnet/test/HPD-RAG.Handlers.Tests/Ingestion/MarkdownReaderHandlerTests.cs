using HPD.RAG.Core.DTOs;
using HPD.RAG.Handlers.Tests.Shared;
using HPD.RAG.Ingestion.Readers;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Ingestion;

/// <summary>
/// Tests T-083 through T-086 — MarkdownReaderHandler document parsing.
/// </summary>
public sealed class MarkdownReaderHandlerTests
{
    private static string CreateMarkdownFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact] // T-083
    public async Task ExecuteAsync_ProducesAtLeastOneDocument()
    {
        var path = CreateMarkdownFile("## Heading\n\nSome paragraph text.");
        try
        {
            var handler = new MarkdownReaderHandler();
            var ctx = HandlerTestContext.Create();

            var output = await handler.ExecuteAsync(
                FilePaths: [path],
                context: ctx);

            Assert.NotEmpty(output.Documents);
        }
        finally { File.Delete(path); }
    }

    [Fact] // T-084
    public async Task ExecuteAsync_DocumentId_IsFilePath()
    {
        var path = CreateMarkdownFile("## Heading\n\nSome paragraph text.");
        try
        {
            var handler = new MarkdownReaderHandler();
            var ctx = HandlerTestContext.Create();

            var output = await handler.ExecuteAsync(
                FilePaths: [path],
                context: ctx);

            Assert.Contains(output.Documents, d => d.Id == path);
        }
        finally { File.Delete(path); }
    }

    [Fact] // T-085
    public async Task ExecuteAsync_Elements_ContainParagraphType()
    {
        var path = CreateMarkdownFile("## Heading\n\nSome paragraph text.");
        try
        {
            var handler = new MarkdownReaderHandler();
            var ctx = HandlerTestContext.Create();

            var output = await handler.ExecuteAsync(
                FilePaths: [path],
                context: ctx);

            var allElements = output.Documents.SelectMany(d => d.Elements);
            Assert.Contains(allElements, e => e.Type == "paragraph");
        }
        finally { File.Delete(path); }
    }

    [Fact] // T-086
    public async Task ExecuteAsync_Headers_ProduceHeaderElement_WithLevel2()
    {
        var path = CreateMarkdownFile("## Heading\n\nSome paragraph text.");
        try
        {
            var handler = new MarkdownReaderHandler();
            var ctx = HandlerTestContext.Create();

            var output = await handler.ExecuteAsync(
                FilePaths: [path],
                context: ctx);

            var allElements = output.Documents.SelectMany(d => d.Elements);
            Assert.Contains(allElements, e => e.Type == "header" && e.HeaderLevel == 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFilePaths_ReturnsEmptyDocuments()
    {
        var handler = new MarkdownReaderHandler();
        var ctx = HandlerTestContext.Create();

        var output = await handler.ExecuteAsync(
            FilePaths: [],
            context: ctx);

        Assert.Empty(output.Documents);
    }
}
