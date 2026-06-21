using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HPD.TextExtract.Interfaces;
using HPD.TextExtract.Models;
using HPD.TextExtract.Extensions;

namespace HPD.TextExtract
{
    /// <summary>
    /// Text extraction result containing extracted text and metadata
    /// </summary>
    public sealed class TextExtractionResult
    {
        public bool IsSuccess { get; init; }
        public string ExtractedText { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
        public string? ErrorMessage { get; init; }
        public TimeSpan ProcessingTime { get; init; }
        public long FileSizeBytes { get; init; }
        public string MimeType { get; init; } = string.Empty;
        public ExtractionResult? Extraction { get; init; }

        public static TextExtractionResult Success(string extractedText, string fileName, string filePath,
            TimeSpan processingTime, long fileSizeBytes, string mimeType, ExtractionResult extraction) =>
            new()
            {
                IsSuccess = true,
                ExtractedText = extractedText,
                FileName = fileName,
                FilePath = filePath,
                ProcessingTime = processingTime,
                FileSizeBytes = fileSizeBytes,
                MimeType = mimeType,
                Extraction = extraction
            };

        public static TextExtractionResult Failure(string fileName, string filePath, string errorMessage) =>
            new()
            {
                IsSuccess = false,
                FileName = fileName,
                FilePath = filePath,
                ErrorMessage = errorMessage
            };
    }

    /// <summary>
    /// Main text extraction utility that uses the new decoder architecture
    /// </summary>
    public sealed class TextExtractionUtility : IDisposable
    {
        private readonly IDecoderFactory _decoderFactory;
        private readonly IMimeTypeDetection _mimeTypeDetection;
        private readonly ILogger<TextExtractionUtility> _log;

        public TextExtractionUtility(
            IDecoderFactory? decoderFactory = null,
            IMimeTypeDetection? mimeTypeDetection = null,
            ILoggerFactory? loggerFactory = null)
        {
            _decoderFactory = decoderFactory ?? new DecoderFactory(mimeTypeDetection, loggerFactory);
            _mimeTypeDetection = mimeTypeDetection ?? new MimeTypesDetection();
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TextExtractionUtility>();
        }

        /// <summary>
        /// Extract text from a file or URL
        /// </summary>
        public async Task<TextExtractionResult> ExtractTextAsync(string urlOrFilePath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(urlOrFilePath);

            var startTime = DateTime.UtcNow;
            bool isUrl = IsUrl(urlOrFilePath);
            string fileName;
            string? mimeType;
            long fileSize = 0;

            if (isUrl)
            {
                var uri = new Uri(urlOrFilePath);
                fileName = Path.GetFileName(uri.LocalPath) ?? uri.Host;
                mimeType = Models.MimeTypes.WebPageUrl;
                _log.LogDebug("Processing URL: {Url}", urlOrFilePath);
            }
            else
            {
                var fi = new FileInfo(urlOrFilePath);
                if (!fi.Exists)
                {
                    _log.LogWarning("File not found: {FilePath}", urlOrFilePath);
                    return TextExtractionResult.Failure(fi.Name, urlOrFilePath, $"File not found: {urlOrFilePath}");
                }

                fileName = fi.Name;
                fileSize = fi.Length;

                if (!_mimeTypeDetection.TryGetFileType(fileName, out mimeType))
                {
                    mimeType = Models.MimeTypes.PlainText; // Default fallback
                }

                _log.LogDebug("Processing file: {FilePath} ({FileSize} bytes), MIME type: {MimeType}",
                    urlOrFilePath, fileSize, mimeType);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Get appropriate decoder
            var decoder = isUrl
                ? _decoderFactory.GetDecoder(Models.MimeTypes.WebPageUrl)
                : _decoderFactory.GetDecoderForFile(urlOrFilePath);

            if (decoder == null)
            {
                var errorMsg = $"No decoder found for {(isUrl ? "URL" : "file type")} '{urlOrFilePath}'";
                _log.LogError(errorMsg);
                return TextExtractionResult.Failure(fileName, urlOrFilePath, errorMsg);
            }

            try
            {
                _log.LogDebug("Using decoder: {DecoderType}", decoder.GetType().Name);

                ContentInput input;
                if (isUrl && decoder is IWebDecoder webDecoder)
                {
                    input = ContentInput.FromUrl(new Uri(urlOrFilePath), mimeType);
                    var extraction = await webDecoder.DecodeFromUrlAsync(input.Url!, ExtractionOptions.Default, cancellationToken).ConfigureAwait(false);
                    var extractedTextFromUrl = ConvertFileContentToString(extraction.Content);
                    var urlProcessingTime = DateTime.UtcNow - startTime;

                    _log.LogInformation("Successfully extracted {CharCount} characters from '{FileName}' in {ProcessingTime}ms",
                        extractedTextFromUrl.Length, fileName, urlProcessingTime.TotalMilliseconds);

                    return TextExtractionResult.Success(extractedTextFromUrl, fileName, urlOrFilePath,
                        urlProcessingTime, fileSize, mimeType ?? Models.MimeTypes.PlainText, extraction);
                }
                else
                {
                    input = ContentInput.FromPath(urlOrFilePath, mimeType);
                }

                var extractionResult = await decoder.DecodeAsync(input, ExtractionOptions.Default, cancellationToken).ConfigureAwait(false);
                var extractedText = ConvertFileContentToString(extractionResult.Content);
                var processingTime = DateTime.UtcNow - startTime;

                _log.LogInformation("Successfully extracted {CharCount} characters from '{FileName}' in {ProcessingTime}ms",
                    extractedText.Length, fileName, processingTime.TotalMilliseconds);

                return TextExtractionResult.Success(extractedText, fileName, urlOrFilePath,
                    processingTime, fileSize, mimeType ?? Models.MimeTypes.PlainText, extractionResult);
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _log.LogError(ex, "Failed to extract text from '{FilePath}' after {ProcessingTime}ms",
                    urlOrFilePath, processingTime.TotalMilliseconds);

                return TextExtractionResult.Failure(fileName, urlOrFilePath,
                    $"Text extraction failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract text from binary data (e.g., from DataContent in Microsoft.Extensions.AI)
        /// </summary>
        /// <param name="data">Binary data to extract text from</param>
        /// <param name="mimeType">MIME type of the data</param>
        /// <param name="fileName">Optional file name (used for logging and result)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Text extraction result</returns>
        public async Task<TextExtractionResult> ExtractTextAsync(
            ReadOnlyMemory<byte> data,
            string? mimeType = null,
            string? fileName = null,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            fileName ??= "binary-data";
            mimeType ??= Models.MimeTypes.PlainText;
            long fileSize = data.Length;

            _log.LogDebug("Processing binary data ({FileSize} bytes), MIME type: {MimeType}",
                fileSize, mimeType);

            cancellationToken.ThrowIfCancellationRequested();

            // Get appropriate decoder
            var decoder = _decoderFactory.GetDecoder(mimeType);

            if (decoder == null)
            {
                var errorMsg = $"No decoder found for MIME type '{mimeType}'";
                _log.LogError(errorMsg);
                return TextExtractionResult.Failure(fileName, "binary-data", errorMsg);
            }

            try
            {
                _log.LogDebug("Using decoder: {DecoderType}", decoder.GetType().Name);

                var input = ContentInput.FromBytes(data, fileName, mimeType);
                var extractionResult = await decoder.DecodeAsync(input, ExtractionOptions.Default, cancellationToken).ConfigureAwait(false);

                var extractedText = ConvertFileContentToString(extractionResult.Content);
                var processingTime = DateTime.UtcNow - startTime;

                _log.LogInformation("Successfully extracted {CharCount} characters from binary data in {ProcessingTime}ms",
                    extractedText.Length, processingTime.TotalMilliseconds);

                return TextExtractionResult.Success(extractedText, fileName, "binary-data",
                    processingTime, fileSize, mimeType, extractionResult);
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _log.LogError(ex, "Failed to extract text from binary data after {ProcessingTime}ms",
                    processingTime.TotalMilliseconds);

                return TextExtractionResult.Failure(fileName, "binary-data",
                    $"Text extraction failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a custom decoder
        /// </summary>
        public void RegisterDecoder(IContentDecoder decoder, params string[] mimeTypes)
        {
            _decoderFactory.RegisterDecoder(decoder, mimeTypes);
        }

        /// <summary>
        /// Get all available decoders
        /// </summary>
        public IEnumerable<IContentDecoder> GetAvailableDecoders()
        {
            return _decoderFactory.GetAllDecoders();
        }

        private static string ConvertFileContentToString(FileContent fileContent)
        {
            if (fileContent.Sections.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var section in fileContent.Sections)
            {
                var sectionContent = section.Content.Trim();
                if (string.IsNullOrEmpty(sectionContent)) continue;

                sb.Append(sectionContent);
                if (section.SentencesAreComplete)
                {
                    sb.AppendLineNix();
                    sb.AppendLineNix();
                }
                else
                {
                    sb.AppendLineNix();
                }
            }
            return sb.ToString().Trim() ?? string.Empty;
        }

        public static bool IsUrl(string input)
        {
            return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public void Dispose()
        {
            // Dispose any resources if needed
        }
    }

    /// <summary>
    /// Extension methods for easier usage
    /// </summary>
    public static class TextExtractionUtilityExtensions
    {
        public static async Task<string> ExtractTextStringAsync(this TextExtractionUtility utility,
            string urlOrFilePath, CancellationToken cancellationToken = default)
        {
            var result = await utility.ExtractTextAsync(urlOrFilePath, cancellationToken);
            return result.IsSuccess ? result.ExtractedText : throw new InvalidOperationException(result.ErrorMessage);
        }
    }
}
