using System.Text;
using System.Text.Json;
using HPD.Extract.Extensions;
using HPD.Extract.Interfaces;
using HPD.Extract.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Extract.Decoders
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
                    mimeType.StartsWith(MimeTypes.Json, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.XML, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.XML2, StringComparison.OrdinalIgnoreCase));
        }

        public async ValueTask<ExtractionResult> DecodeAsync(
            ContentInput input,
            ExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from {InputKind} input", input.Kind);

            await using var stream = await input.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            var mimeType = input.MimeType;
            if (string.IsNullOrWhiteSpace(mimeType) && input.FileName is not null)
            {
                mimeType = new MimeTypesDetection().TryGetFileType(input.FileName, out var detected)
                    ? detected ?? MimeTypes.PlainText
                    : MimeTypes.PlainText;
            }
            else
            {
                mimeType ??= MimeTypes.PlainText;
            }

            var content = await DecodeContentAsync(stream, mimeType, cancellationToken).ConfigureAwait(false);
            return new ExtractionResult(content)
            {
                Pages = content.Sections.Select(section => new ExtractedPage
                {
                    Number = section.Number,
                    Text = section.Content
                }).ToArray()
            };
        }

        private static async Task<FileContent> DecodeContentAsync(
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
                    Chunk.Meta(sentencesAreComplete: false, source: TextExtractionSource.Native)));
            }

            return result;
        }
    }
}
