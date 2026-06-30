using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using HPD.Extract.Extensions;
using HPD.Extract.Interfaces;
using HPD.Extract.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using A = DocumentFormat.OpenXml.Drawing;

namespace HPD.Extract.Decoders
{
    public sealed class MsPowerPointDecoder : IContentDecoder
    {
        private readonly ILogger<MsPowerPointDecoder> _log;

        public MsPowerPointDecoder(ILoggerFactory? loggerFactory = null)
        {
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MsPowerPointDecoder>();
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null &&
                   (mimeType.StartsWith(MimeTypes.MsPowerPointX, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.MsPowerPoint, StringComparison.OrdinalIgnoreCase));
        }

        public async ValueTask<ExtractionResult> DecodeAsync(
            ContentInput input,
            ExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from MS PowerPoint file");

            var result = new FileContent(MimeTypes.PlainText);
            var pages = new List<ExtractedPage>();
            await using var data = await input.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            using var presentationDocument = PresentationDocument.Open(data, false);

            PresentationPart? presentationPart = presentationDocument.PresentationPart;
            if (presentationPart is null || presentationPart.Presentation is null)
            {
                throw new InvalidOperationException("The presentation part is missing.");
            }

            // Get the slide identifiers in order
            var slideIdList = presentationPart.Presentation.SlideIdList;
            if (slideIdList != null)
            {
                int slideNumber = 1;
                foreach (SlideId slideId in slideIdList.Elements<SlideId>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (slideId.RelationshipId?.Value == null) continue;
                    SlidePart? slidePart = presentationPart.GetPartById(slideId.RelationshipId.Value) as SlidePart;
                    if (slidePart != null)
                    {
                        var slideText = GetSlideText(slidePart);
                        if (!string.IsNullOrWhiteSpace(slideText))
                        {
                            result.Sections.Add(new Chunk(
                                slideText.NormalizeNewlines(false),
                                slideNumber,
                                Chunk.Meta(sentencesAreComplete: true, pageNumber: slideNumber, source: TextExtractionSource.Native)
                            ));
                            pages.Add(new ExtractedPage
                            {
                                Number = slideNumber,
                                Text = slideText.NormalizeNewlines(false)
                            });
                        }
                        slideNumber++;
                    }
                }
            }

            return new ExtractionResult(result)
            {
                Pages = pages
            };
        }

        private static string GetSlideText(SlidePart slidePart)
        {
            var sb = new StringBuilder();

            // Extract text from all shapes in the slide
            if (slidePart.Slide != null)
            {
                var shapes = slidePart.Slide.Descendants<Shape>();
                foreach (var shape in shapes)
                {
                    // Extract text from text body
                    var textBody = shape.TextBody;
                    if (textBody != null)
                    {
                        foreach (var paragraph in textBody.Elements<A.Paragraph>())
                        {
                            foreach (var text in paragraph.Descendants<A.Text>())
                            {
                                sb.Append(text.Text);
                            }
                            sb.AppendLineNix();
                        }
                    }
                }

                // Also extract text from tables
                var graphicFrames = slidePart.Slide.Descendants<GraphicFrame>();
                foreach (var graphicFrame in graphicFrames)
                {
                    var tables = graphicFrame.Descendants<A.Table>();
                    foreach (var table in tables)
                    {
                        foreach (var row in table.Elements<A.TableRow>())
                        {
                            foreach (var cell in row.Elements<A.TableCell>())
                            {
                                foreach (var paragraph in cell.TextBody?.Elements<A.Paragraph>() ?? Enumerable.Empty<A.Paragraph>())
                                {
                                    foreach (var text in paragraph.Descendants<A.Text>())
                                    {
                                        sb.Append(text.Text);
                                        sb.Append('\t');
                                    }
                                }
                            }
                            sb.AppendLineNix();
                        }
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
