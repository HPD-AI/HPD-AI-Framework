using System;
using System.Threading;
using System.Threading.Tasks;
using HPD.Extract.Interfaces;
using HPD.Extract.Models;
using HPD.Extract.Pdf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Extract.Decoders
{
    public sealed class PdfDecoder : IContentDecoder
    {
        private readonly IPdfExtractionEngine _engine;
        private readonly ILogger<PdfDecoder> _log;

        public PdfDecoder(IPdfExtractionEngine? engine = null, ILoggerFactory? loggerFactory = null)
        {
            _engine = engine ?? new PdfExtractionEngine();
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<PdfDecoder>();
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null && mimeType.StartsWith(MimeTypes.Pdf, StringComparison.OrdinalIgnoreCase);
        }

        public async ValueTask<ExtractionResult> DecodeAsync(
            ContentInput input,
            ExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= ExtractionOptions.Default;
            _log.LogDebug("Extracting text from PDF file");

            var pdfResult = await _engine.ExtractAsync(
                input,
                PdfExtractionOptions.FromExtractionOptions(options),
                cancellationToken).ConfigureAwait(false);

            var content = new FileContent(MimeTypes.PlainText);
            var pages = new List<ExtractedPage>(pdfResult.Pages.Count);
            foreach (var page in pdfResult.Pages)
            {
                content.Sections.Add(new Chunk(
                    page.Text,
                    page.Number,
                    Chunk.Meta(
                        sentencesAreComplete: false,
                        pageNumber: page.Number,
                        source: TextExtractionSource.Native)));

                pages.Add(new ExtractedPage
                {
                    Number = page.Number,
                    Size = page.Size,
                    Text = page.Text,
                    TextItems = page.TextItems.Select(static item => new ExtractedTextItem
                    {
                        Text = item.Text,
                        BoundingBox = item.BoundingBox,
                        Rotation = item.Rotation,
                        FontName = item.Font?.Name,
                        FontSize = item.Font?.Size,
                        Confidence = item.Confidence,
                        Source = item.Layer == PdfTextLayerKind.Ocr ? TextExtractionSource.Ocr : TextExtractionSource.Native,
                        Metadata =
                        {
                            ["layer"] = item.Layer.ToString(),
                            ["renderMode"] = item.RenderMode,
                            ["font"] = item.Font
                        }
                    }).ToArray(),
                    Metadata =
                    {
                        ["quality"] = page.Quality,
                        ["ocrDecision"] = page.OcrDecision
                    }
                });
            }

            return new ExtractionResult(content)
            {
                Pages = pages,
                Assets = pdfResult.Assets,
                Diagnostics = pdfResult.Diagnostics,
                RichResult = pdfResult
            };
        }
    }
}
