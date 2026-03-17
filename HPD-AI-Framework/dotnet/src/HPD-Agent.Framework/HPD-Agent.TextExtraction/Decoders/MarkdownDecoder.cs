using System.Text;
using HPD.Agent.TextExtraction;

internal class MarkdownTextDecoder : ITextDecoder
{
    public string Description => "Markdown Text Content Decoder";

    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
    // Use FileStream and StreamReader to adhere to analyzer rules
    await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var reader = new StreamReader(fs, Encoding.UTF8);
    return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}