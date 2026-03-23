using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent.TextExtraction.Extensions;
using HPD.Agent.TextExtraction.Interfaces;
using HPD.Agent.TextExtraction.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace HPD.Agent.TextExtraction.Decoders
{
    public sealed class PdfDecoder : IContentDecoder
    {
        private readonly ILogger<PdfDecoder> _log;

        public PdfDecoder(ILoggerFactory? loggerFactory = null)
        {
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<PdfDecoder>();
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null && mimeType.StartsWith(MimeTypes.Pdf, StringComparison.OrdinalIgnoreCase);
        }

        public Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
        {
            using var stream = File.OpenRead(filename);
            return DecodeAsync(stream, cancellationToken);
        }

        public Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from PDF file");

            var result = new FileContent(MimeTypes.PlainText);
            using PdfDocument? pdfDocument = PdfDocument.Open(data);
            if (pdfDocument == null) { return Task.FromResult(result); }

            foreach (Page? page in pdfDocument.GetPages().Where(x => x != null))
            {
                // Note: no trimming, use original spacing when working with pages
                string pageContent = ContentOrderTextExtractor.GetText(page).NormalizeNewlines(false) ?? string.Empty;

                result.Sections.Add(new Chunk(pageContent, page.Number, Chunk.Meta(sentencesAreComplete: false, pageNumber: page.Number)));
            }

            return Task.FromResult(result);
        }
    }
}