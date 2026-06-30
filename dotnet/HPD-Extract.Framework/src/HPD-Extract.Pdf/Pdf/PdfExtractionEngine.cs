using System.Diagnostics;
using HPD.Extract.Models;

namespace HPD.Extract.Pdf
{
    public sealed class PdfExtractionEngine : IPdfExtractionEngine
    {
        private readonly IPdfBackend _backend;
        private readonly IPdfPageSelector _pageSelector;
        private readonly IPdfPageQualityAnalyzer _qualityAnalyzer;
        private readonly IPdfLayoutProjector _layoutProjector;
        private readonly IPdfOcrExecutor _ocrExecutor;
        private readonly IPdfOcrTextMerger _ocrTextMerger;

        public PdfExtractionEngine(
            IPdfBackend? backend = null,
            IPdfPageSelector? pageSelector = null,
            IPdfPageQualityAnalyzer? qualityAnalyzer = null,
            IPdfLayoutProjector? layoutProjector = null,
            IPdfOcrExecutor? ocrExecutor = null,
            IPdfOcrTextMerger? ocrTextMerger = null)
        {
            _backend = backend ?? new PdfiumBackend();
            _pageSelector = pageSelector ?? new DefaultPdfPageSelector();
            _qualityAnalyzer = qualityAnalyzer ?? new DefaultPdfPageQualityAnalyzer();
            _layoutProjector = layoutProjector ?? new DefaultPdfLayoutProjector();
            _ocrExecutor = ocrExecutor ?? new NoOpPdfOcrExecutor();
            _ocrTextMerger = ocrTextMerger ?? new DefaultPdfOcrTextMerger();
        }

        public async ValueTask<PdfExtractionResult> ExtractAsync(
            ContentInput input,
            PdfExtractionOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(options);

            var diagnostics = new ExtractionDiagnostics();
            using var ownedOcrExecutor = _ocrExecutor is NoOpPdfOcrExecutor && options.OcrEndpoint is not null
                ? new HttpPdfOcrExecutor()
                : null;
            var ocrExecutor = ownedOcrExecutor ?? _ocrExecutor;
            var context = new PdfPipelineContext
            {
                Options = options,
                Diagnostics = diagnostics,
                CancellationToken = cancellationToken
            };
            var total = Stopwatch.StartNew();
            var pages = new List<PdfPage>();
            var assets = new List<ExtractedAsset>();
            var targetPages = _pageSelector.Parse(options.TargetPages);
            var renderedOcrPageCount = 0;
            var renderFailedOcrPageCount = 0;
            var attemptedOcrPageCount = 0;
            var succeededOcrPageCount = 0;
            var usedOcrPageCount = 0;
            var failedOcrPageCount = 0;
            var strictOcrRequiredPageCount = 0;
            var strictOcrFailurePageCount = 0;
            string? firstOcrFailure = null;

            using var document = await _backend.OpenAsync(input, options, cancellationToken).ConfigureAwait(false);
            diagnostics.Metrics["pdfBackend"] = CreateBackendDiagnostics(document.Capabilities);
            diagnostics.Metrics["pdfBackendName"] = document.Capabilities.Name;
            diagnostics.Metrics["pdfOcrExecutor"] = CreateOcrExecutorDiagnostics(ocrExecutor, options);
            var processed = 0;

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (targetPages is not null && !targetPages.Contains(page.Number))
                {
                    continue;
                }

                if (processed >= options.MaxPages)
                {
                    break;
                }

                var pageTimer = Stopwatch.StartNew();
                var snapshot = page.ExtractSnapshot(context);
                var size = snapshot.Size;
                var nativeItems = snapshot.NativeTextItems;
                var imageRegions = snapshot.ImageRegions;
                var pageAssets = new List<ExtractedAsset>(snapshot.Assets);
                var quality = _qualityAnalyzer.Analyze(new PdfPageQualityInput
                {
                    PageSize = size,
                    TextItems = nativeItems,
                    ImageRegions = imageRegions
                }, context);
                var ocrDecision = _qualityAnalyzer.PlanOcr(quality, context);
                var textItemsForProjection = nativeItems;
                PdfOcrMergeResult? ocrMerge = null;
                PdfOcrPageResult? ocrResult = null;

                if (ocrDecision.ShouldRun)
                {
                    diagnostics.OcrPlanned = true;
                    if (ocrDecision.FailurePolicy == PdfOcrFailurePolicy.FailIfAllOcrFails)
                    {
                        strictOcrRequiredPageCount++;
                    }

                    var renderResult = await page.RenderAsync(new PdfPageRenderRequest
                    {
                        Purpose = PdfRenderPurpose.Ocr,
                        Dpi = options.Dpi,
                        Format = PdfRenderImageFormat.Bmp
                    }, context).ConfigureAwait(false);
                    if (!renderResult.Succeeded)
                    {
                        renderFailedOcrPageCount++;
                        failedOcrPageCount++;
                        diagnostics.OcrFailed = true;
                        var errorMessage = renderResult.Error?.Message ?? "unknown render error";
                        firstOcrFailure ??= errorMessage;
                        if (ocrDecision.FailurePolicy == PdfOcrFailurePolicy.FailIfAllOcrFails)
                        {
                            strictOcrFailurePageCount++;
                        }

                        diagnostics.Warnings.Add(
                            $"Page {page.Number} needs OCR ({string.Join(", ", ocrDecision.Reasons)}), but PDF page rendering failed: {errorMessage}");
                    }
                    else
                    {
                        var renderedPage = renderResult.RenderedPage!;
                        diagnostics.OcrRendered = true;
                        renderedOcrPageCount++;
                        attemptedOcrPageCount++;
                        diagnostics.OcrAttempted = true;

                        ocrResult = await ocrExecutor.RecognizeAsync(renderedPage, context).ConfigureAwait(false);
                        if (!ocrResult.Succeeded)
                        {
                            failedOcrPageCount++;
                            diagnostics.OcrFailed = true;
                            var errorMessage = ocrResult.Error?.Message ?? "unknown error";
                            firstOcrFailure ??= errorMessage;
                            if (ocrDecision.FailurePolicy == PdfOcrFailurePolicy.FailIfAllOcrFails)
                            {
                                strictOcrFailurePageCount++;
                            }

                            diagnostics.Warnings.Add($"Page {page.Number} OCR failed: {errorMessage}");
                        }
                        else
                        {
                            diagnostics.OcrSucceeded = true;
                            succeededOcrPageCount++;
                            ocrMerge = _ocrTextMerger.Merge(new PdfOcrMergeInput
                            {
                                PageNumber = page.Number,
                                PageSize = size,
                                NativeItems = nativeItems,
                                Quality = quality,
                                Decision = ocrDecision,
                                RenderedPage = renderedPage,
                                OcrResult = ocrResult
                            }, context);
                            textItemsForProjection = ocrMerge.TextItems;
                            var mergedOcrItemCount = GetMetricAsInt32(ocrMerge.Metrics, "mergedOcrItemCount");
                            if (mergedOcrItemCount > 0)
                            {
                                diagnostics.OcrUsed = true;
                                usedOcrPageCount++;
                            }
                        }
                    }
                }

                if (options.IncludeScreenshots)
                {
                    var screenshotResult = await page.RenderAsync(new PdfPageRenderRequest
                    {
                        Purpose = PdfRenderPurpose.Screenshot,
                        Dpi = options.Dpi,
                        Format = options.ScreenshotFormat
                    }, context).ConfigureAwait(false);
                    if (screenshotResult.Succeeded)
                    {
                        pageAssets.Add(CreateScreenshotAsset(snapshot, screenshotResult.RenderedPage!));
                    }
                    else
                    {
                        var errorMessage = screenshotResult.Error?.Message ?? "unknown render error";
                        diagnostics.Warnings.Add($"Page {page.Number} screenshot rendering failed: {errorMessage}");
                    }
                }

                var projection = _layoutProjector.Project(new PdfLayoutProjectionInput
                {
                    PageNumber = snapshot.Number,
                    PageSize = size,
                    Rotation = snapshot.Rotation,
                    TextItems = textItemsForProjection,
                    Options = options
                }, context);

                var pdfPage = new PdfPage
                {
                    Number = snapshot.Number,
                    Size = size,
                    Text = projection.Text,
                    TextItems = options.IncludeTextItems ? projection.ProjectedItems : Array.Empty<PdfTextItem>(),
                    Assets = pageAssets,
                    Quality = quality,
                    OcrDecision = ocrDecision,
                    Metadata =
                    {
                        ["rotation"] = snapshot.Rotation,
                        ["backend"] = snapshot.Metadata.TryGetValue("backend", out var backend) ? backend : null,
                        ["pageLoadFailed"] = snapshot.Metadata.TryGetValue("pageLoadFailed", out var pageLoadFailed) ? pageLoadFailed : null,
                        ["failureKind"] = snapshot.Metadata.TryGetValue("failureKind", out var failureKind) ? failureKind : null,
                        ["backendErrorCode"] = snapshot.Metadata.TryGetValue("backendErrorCode", out var backendErrorCode) ? backendErrorCode : null,
                        ["nativeItemCount"] = nativeItems.Count,
                        ["imageRegionCount"] = imageRegions.Count,
                        ["ocrRelevantImageCount"] = imageRegions.Count(static image => image.IsOcrRelevant),
                        ["ocrResultRegionCount"] = ocrResult?.Regions.Count,
                        ["ocrMerge"] = ocrMerge?.Metrics,
                        ["projection"] = projection.Metrics,
                        ["elapsedMs"] = pageTimer.Elapsed.TotalMilliseconds
                    }
                };

                pages.Add(pdfPage);
                assets.AddRange(pageAssets);
                processed++;
                diagnostics.Timings.Add(ExtractionTiming.FromStopwatch($"pdf.page.{page.Number}", pageTimer));
            }

            total.Stop();
            diagnostics.Timings.Add(new ExtractionTiming("pdf.total", total.Elapsed));
            diagnostics.OcrCandidatePageCount = pages.Count(static page => page.OcrDecision.ShouldRun);
            diagnostics.OcrRenderedPageCount = renderedOcrPageCount;
            diagnostics.OcrRenderFailedPageCount = renderFailedOcrPageCount;
            diagnostics.OcrAttemptedPageCount = attemptedOcrPageCount;
            diagnostics.OcrSucceededPageCount = succeededOcrPageCount;
            diagnostics.OcrUsedPageCount = usedOcrPageCount;
            diagnostics.OcrFailedPageCount = failedOcrPageCount;
            diagnostics.OcrStrictRequiredPageCount = strictOcrRequiredPageCount;
            diagnostics.OcrStrictFailurePageCount = strictOcrFailurePageCount;
            diagnostics.Metrics["pageCount"] = pages.Count;
            diagnostics.Metrics["assetCount"] = assets.Count;
            diagnostics.Metrics["embeddedImageAssetsIncluded"] = options.IncludeEmbeddedImages;
            diagnostics.Metrics["ocrCandidatePageCount"] = diagnostics.OcrCandidatePageCount;
            diagnostics.Metrics["ocrRenderedPageCount"] = diagnostics.OcrRenderedPageCount;
            diagnostics.Metrics["ocrRenderFailedPageCount"] = diagnostics.OcrRenderFailedPageCount;
            diagnostics.Metrics["ocrAttemptedPageCount"] = attemptedOcrPageCount;
            diagnostics.Metrics["ocrSucceededPageCount"] = succeededOcrPageCount;
            diagnostics.Metrics["ocrUsedPageCount"] = usedOcrPageCount;
            diagnostics.Metrics["ocrFailedPageCount"] = failedOcrPageCount;
            diagnostics.Metrics["ocrStrictRequiredPageCount"] = strictOcrRequiredPageCount;
            diagnostics.Metrics["ocrStrictFailurePageCount"] = strictOcrFailurePageCount;

            if (strictOcrRequiredPageCount > 0 &&
                strictOcrFailurePageCount == strictOcrRequiredPageCount)
            {
                throw new InvalidOperationException(
                    $"OCR failed for all {strictOcrRequiredPageCount} strict PDF page(s): {firstOcrFailure ?? "unknown error"}");
            }

            return new PdfExtractionResult
            {
                Pages = pages,
                Assets = assets,
                Text = string.Join("\n\n", pages.Select(static page => page.Text)),
                Diagnostics = diagnostics
            };
        }

        private static ExtractedAsset CreateScreenshotAsset(PdfPageSnapshot snapshot, PdfRenderedPage renderedPage) => new()
        {
            Kind = ExtractedAssetKind.PageScreenshot,
            Name = $"page-{snapshot.Number}-screenshot.{RenderedImageExtension(renderedPage.EncodedFormat)}",
            MimeType = renderedPage.Image.MimeType,
            PageNumber = snapshot.Number,
            BoundingBox = new BoundingBox(0, 0, snapshot.Size.Width, snapshot.Size.Height),
            Data = renderedPage.Image.Data,
            Metadata =
            {
                ["width"] = renderedPage.Image.Width,
                ["height"] = renderedPage.Image.Height,
                ["dpi"] = renderedPage.Dpi,
                ["encodedFormat"] = renderedPage.EncodedFormat.ToString(),
                ["backend"] = renderedPage.Metadata.TryGetValue("backend", out var backend) ? backend : null,
                ["renderPurpose"] = PdfRenderPurpose.Screenshot.ToString(),
                ["render"] = renderedPage.Metadata
            }
        };

        private static string RenderedImageExtension(PdfRenderImageFormat format) => format switch
        {
            PdfRenderImageFormat.Bmp => "bmp",
            PdfRenderImageFormat.Png => "png",
            PdfRenderImageFormat.Jpeg => "jpg",
            _ => "bin"
        };

        private static Dictionary<string, object?> CreateBackendDiagnostics(PdfBackendCapabilities capabilities) =>
            new()
            {
                ["name"] = capabilities.Name,
                ["version"] = capabilities.Version,
                ["canExtractNativeText"] = capabilities.CanExtractNativeText,
                ["canExtractGlyphBounds"] = capabilities.CanExtractGlyphBounds,
                ["canExtractImageRegions"] = capabilities.CanExtractImageRegions,
                ["canExtractEmbeddedImages"] = capabilities.CanExtractEmbeddedImages,
                ["canRenderPages"] = capabilities.CanRenderPages,
                ["canReportFontMetadata"] = capabilities.CanReportFontMetadata,
                ["canReportMarkedContent"] = capabilities.CanReportMarkedContent,
                ["canReportTextRenderMode"] = capabilities.CanReportTextRenderMode,
                ["canReportTextColors"] = capabilities.CanReportTextColors,
                ["supportedRenderFormats"] = capabilities.SupportedRenderFormats
                    .Select(static format => format.ToString())
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                ["metadata"] = capabilities.Metadata
            };

        private static Dictionary<string, object?> CreateOcrExecutorDiagnostics(
            IPdfOcrExecutor executor,
            PdfExtractionOptions options) =>
            new()
            {
                ["kind"] = GetOcrExecutorKind(executor),
                ["typeName"] = executor.GetType().Name,
                ["enabled"] = options.OcrEnabled,
                ["endpointConfigured"] = options.OcrEndpoint is not null,
                ["language"] = options.OcrLanguage
            };

        private static string GetOcrExecutorKind(IPdfOcrExecutor executor) => executor switch
        {
            NoOpPdfOcrExecutor => "NoOp",
            HttpPdfOcrExecutor => "Http",
            _ => "Custom"
        };

        private static int GetMetricAsInt32(IReadOnlyDictionary<string, object?> metrics, string key)
        {
            if (!metrics.TryGetValue(key, out var value) || value is null)
            {
                return 0;
            }

            return value switch
            {
                int intValue => intValue,
                long longValue => checked((int)longValue),
                float floatValue => (int)floatValue,
                double doubleValue => (int)doubleValue,
                _ => 0
            };
        }
    }
}
