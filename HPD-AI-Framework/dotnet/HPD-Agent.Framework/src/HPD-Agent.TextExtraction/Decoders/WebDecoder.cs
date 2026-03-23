using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent.TextExtraction.Extensions;
using HPD.Agent.TextExtraction.Interfaces;
using HPD.Agent.TextExtraction.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.TextExtraction.Decoders
{
    /// <summary>
    /// Web content decoder - extracts text from HTML/web pages
    /// Currently uses basic HTML stripping - can be enhanced with HtmlAgilityPack
    /// </summary>
    public sealed class WebDecoder : IContentDecoder, IWebDecoder
    {
        private readonly IHtmlParser _htmlParser;
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebDecoder> _log;

        public WebDecoder(IHtmlParser? htmlParser = null, HttpClient? httpClient = null, ILoggerFactory? loggerFactory = null)
        {
            _htmlParser = htmlParser ?? new BasicHtmlParser();
            _httpClient = httpClient ?? new HttpClient();
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<WebDecoder>();
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null && (
                mimeType.StartsWith(Models.MimeTypes.Html, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.XHTML, StringComparison.OrdinalIgnoreCase) ||
                mimeType.StartsWith(Models.MimeTypes.WebPageUrl, StringComparison.OrdinalIgnoreCase)
            );
        }

        public async Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from HTML file '{0}'", filename);

            // Check if it's a URL or a file path
            if (IsUrl(filename))
            {
                return await DecodeFromUrlAsync(filename, cancellationToken).ConfigureAwait(false);
            }

            // Read HTML from file
            var html = await File.ReadAllTextAsync(filename, cancellationToken).ConfigureAwait(false);
            return await ParseHtmlContent(html, cancellationToken).ConfigureAwait(false);
        }

        public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from HTML stream");

            using var reader = new StreamReader(data);
            var html = await reader.ReadToEndAsync().ConfigureAwait(false);
            return await ParseHtmlContent(html, cancellationToken).ConfigureAwait(false);
        }

        public async Task<FileContent> DecodeFromUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Downloading and extracting text from URL '{0}'", url);

            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return await ParseHtmlContent(html, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _log.LogError(ex, "Error downloading content from URL: {0}", url);
                var result = new FileContent(MimeTypes.PlainText);
                result.Sections.Add(new Chunk($"Error downloading content: {ex.Message}", 1));
                return result;
            }
        }

        private async Task<FileContent> ParseHtmlContent(string html, CancellationToken cancellationToken)
        {
            var result = new FileContent(MimeTypes.PlainText);

            var text = await _htmlParser.ExtractTextFromHtmlAsync(html, cancellationToken).ConfigureAwait(false);

            // Split into reasonable chunks (could be enhanced with better sectioning)
            var cleanedText = text.NormalizeNewlines(true);

            if (!string.IsNullOrWhiteSpace(cleanedText))
            {
                result.Sections.Add(new Chunk(cleanedText, 1, Chunk.Meta(sentencesAreComplete: true)));
            }

            return result;
        }

        private static bool IsUrl(string input)
        {
            return Uri.TryCreate(input, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }
    }

    /// <summary>
    /// Interface for HTML parsing - allows swapping implementations
    /// </summary>
    public interface IHtmlParser
    {
        Task<string> ExtractTextFromHtmlAsync(string html, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Basic HTML parser using regex - can be replaced with HtmlAgilityPack for better parsing
    /// </summary>
    public class BasicHtmlParser : IHtmlParser
    {
        private static readonly Regex ScriptRegex = new(@"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StyleRegex = new(@"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex HtmlEntityRegex = new(@"&[a-zA-Z][a-zA-Z0-9]*;", RegexOptions.Compiled);

        public Task<string> ExtractTextFromHtmlAsync(string html, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return Task.FromResult(string.Empty);
            }

            // Remove script and style elements
            var text = ScriptRegex.Replace(html, " ");
            text = StyleRegex.Replace(text, " ");

            // Remove HTML tags
            text = TagRegex.Replace(text, " ");

            // Decode HTML entities
            text = System.Net.WebUtility.HtmlDecode(text);

            // Replace multiple whitespaces with single space
            text = WhitespaceRegex.Replace(text, " ");

            return Task.FromResult(text.Trim());
        }
    }

    /// <summary>
    /// Enhanced HTML parser using HtmlAgilityPack (requires HtmlAgilityPack NuGet package)
    /// Uncomment when HtmlAgilityPack is installed
    /// </summary>
    /*
    public class HtmlAgilityPackParser : IHtmlParser
    {
        public Task<string> ExtractTextFromHtmlAsync(string html, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return Task.FromResult(string.Empty);
            }

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Remove script and style nodes
            doc.DocumentNode.Descendants()
                .Where(n => n.Name == "script" || n.Name == "style")
                .ToList()
                .ForEach(n => n.Remove());

            // Get the text
            var text = doc.DocumentNode.InnerText;

            // Clean up whitespace
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return Task.FromResult(text);
        }
    }
    */
}