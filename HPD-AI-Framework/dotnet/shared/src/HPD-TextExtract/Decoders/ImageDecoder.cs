using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HPD.TextExtract.Extensions;
using HPD.TextExtract.Interfaces;
using HPD.TextExtract.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.TextExtract.Decoders
{
    /// <summary>
    /// Image decoder with OCR support
    /// </summary>
    public sealed class ImageDecoder : IContentDecoder, IOcrDecoder
    {
        private readonly IOcrEngine? _ocrEngine;
        private readonly ILogger<ImageDecoder> _log;
        private string _ocrLanguage = "eng";
        private bool _ocrEnabled = true;

        public ImageDecoder(IOcrEngine? ocrEngine = null, ILoggerFactory? loggerFactory = null)
        {
            _ocrEngine = ocrEngine;
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ImageDecoder>();
        }

        public string OcrLanguage
        {
            get => _ocrLanguage;
            set => _ocrLanguage = value ?? "eng";
        }

        public bool OcrEnabled
        {
            get => _ocrEnabled && _ocrEngine != null;
            set => _ocrEnabled = value;
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null && (
                mimeType.StartsWith(Models.MimeTypes.ImageJpeg, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImagePng, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageTiff, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageBmp, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageGif, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.ImageWebP, StringComparison.OrdinalIgnoreCase)
            );
        }

        public async ValueTask<ExtractionResult> DecodeAsync(
            ContentInput input,
            ExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= ExtractionOptions.Default;
            _log.LogDebug("Extracting text from image input {InputKind}", input.Kind);

            var result = new FileContent(MimeTypes.PlainText);

            if (!OcrEnabled)
            {
                _log.LogWarning("OCR is disabled or not available. Returning empty content for image file.");
                result.Sections.Add(new Chunk(string.Empty, 1, Chunk.Meta(sentencesAreComplete: true)));
                return new ExtractionResult(result);
            }

            await using var stream = await input.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            var regions = await ImageToTextRegionsAsync(stream, input.MimeType ?? MimeTypes.ImagePng, options, cancellationToken).ConfigureAwait(false);
            var content = BuildText(regions);
            result.Sections.Add(new Chunk(content.Trim(), 1, Chunk.Meta(sentencesAreComplete: true, source: TextExtractionSource.Ocr)));

            return new ExtractionResult(result)
            {
                Pages = new[]
                {
                    new ExtractedPage
                    {
                        Number = 1,
                        Text = content,
                        TextItems = regions.Select(region => new ExtractedTextItem
                        {
                            Text = region.Text,
                            BoundingBox = region.BoundingBox,
                            Confidence = region.Confidence,
                            Source = TextExtractionSource.Ocr
                        }).ToArray()
                    }
                },
                Diagnostics =
                {
                    OcrPlanned = true,
                    OcrAttempted = true,
                    OcrSucceeded = regions.Count > 0,
                    OcrUsed = regions.Count > 0,
                    OcrFailed = regions.Count == 0,
                    OcrCandidatePageCount = 1,
                    OcrAttemptedPageCount = 1,
                    OcrSucceededPageCount = regions.Count > 0 ? 1 : 0,
                    OcrUsedPageCount = regions.Count > 0 ? 1 : 0,
                    OcrFailedPageCount = regions.Count == 0 ? 1 : 0
                }
            };
        }

        private async Task<IReadOnlyList<OcrTextRegion>> ImageToTextRegionsAsync(
            Stream data,
            string mimeType,
            ExtractionOptions options,
            CancellationToken cancellationToken = default)
        {
            if (_ocrEngine == null)
            {
                _log.LogWarning("OCR engine is not configured. Unable to extract text from image.");
                return Array.Empty<OcrTextRegion>();
            }

            try
            {
                using var ms = new MemoryStream();
                await data.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                var frame = new ImageFrame(ms.ToArray(), Width: 0, Height: 0, mimeType);
                var ocrOptions = new OcrOptions { Language = options.OcrLanguage };
                return await _ocrEngine.RecognizeAsync(frame, ocrOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error extracting text from image using OCR");
                return Array.Empty<OcrTextRegion>();
            }
        }

        private static string BuildText(IReadOnlyList<OcrTextRegion> regions)
        {
            if (regions.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var region in regions)
            {
                if (!string.IsNullOrWhiteSpace(region.Text))
                {
                    sb.AppendLineNix(region.Text.Trim());
                }
            }

            return sb.ToString().Trim();
        }
    }

    /// <summary>
    /// Interface for OCR engines - allows swapping implementations
    /// </summary>
    public interface IOcrEngine
    {
        ValueTask<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
            ImageFrame image,
            OcrOptions options,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Placeholder OCR engine for when Kernel Memory's OCR is not available
    /// Can be replaced with Tesseract, Windows.Media.Ocr, IronOCR, etc.
    /// </summary>
    public class PlaceholderOcrEngine : IOcrEngine
    {
        public ValueTask<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
            ImageFrame image,
            OcrOptions options,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<OcrTextRegion>>(
                new[]
                {
                    new OcrTextRegion
                    {
                        Text = "[OCR text extraction not configured]",
                        Confidence = 0,
                        Language = options.Language
                    }
                });
        }
    }
}
