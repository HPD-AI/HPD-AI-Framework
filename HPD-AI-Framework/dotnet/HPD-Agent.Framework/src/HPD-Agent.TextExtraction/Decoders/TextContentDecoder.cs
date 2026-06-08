using System.Text;
using System.Text.Json;
using HPD.Agent.TextExtraction.Extensions;
using HPD.Agent.TextExtraction.Interfaces;
using HPD.Agent.TextExtraction.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.TextExtraction.Decoders
{
    public sealed class TextContentDecoder : IContentDecoder
    {
        private readonly ILogger<TextContentDecoder> _log;

        public TextContentDecoder(ILoggerFactory? loggerFactory = null)
        {
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TextContentDecoder>();
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null &&
                   (mimeType.StartsWith(MimeTypes.PlainText, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.MarkDown, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.MarkDownOld1, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.MarkDownOld2, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.Json, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.XML, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.XML2, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from text file '{Filename}'", filename);

            await using var stream = new FileStream(
                filename,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var mimeType = new MimeTypesDetection().TryGetFileType(filename, out var detected)
                ? detected ?? MimeTypes.PlainText
                : MimeTypes.PlainText;

            return await DecodeAsync(stream, mimeType, cancellationToken).ConfigureAwait(false);
        }

        public Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
        {
            return DecodeAsync(data, MimeTypes.PlainText, cancellationToken);
        }

        private static async Task<FileContent> DecodeAsync(
            Stream data,
            string mimeType,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(
                data,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);

            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (mimeType.StartsWith(MimeTypes.Json, StringComparison.OrdinalIgnoreCase))
            {
                using var _ = JsonDocument.Parse(text);
            }

            var result = new FileContent(MimeTypes.PlainText);
            var normalized = text.NormalizeNewlines(false);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Sections.Add(new Chunk(
                    normalized,
                    number: 1,
                    Chunk.Meta(sentencesAreComplete: false)));
            }

            return result;
        }
    }
}
