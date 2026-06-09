using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using HPD.TextExtract.Decoders;
using HPD.TextExtract.Models;
using HPD.TextExtract.Pdf;

namespace HPD.TextExtract.Tests.Content;

public sealed class PdfExtractionEngineTests
{
    [Fact]
    public async Task ExtractAsync_EmitsRichPagesTextItemsQualityAndDiagnostics()
    {
        var input = ContentInput.FromBytes(CreatePdfFixture("PDF rich sentinel"), "rich.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions { OcrEnabled = false });

        Assert.Contains("PDF rich sentinel", result.Text, StringComparison.Ordinal);
        var page = Assert.Single(result.Pages);
        Assert.Contains("PDF rich sentinel", page.Text, StringComparison.Ordinal);
        Assert.NotEmpty(page.TextItems);
        Assert.True(page.Quality.NativeTextLength > 0);
        Assert.False(page.Quality.LooksGarbled);
        Assert.False(page.OcrDecision.ShouldRun);
        Assert.Contains(result.Diagnostics.Timings, timing => timing.Name == "pdf.total");
        var backend = Assert.IsType<Dictionary<string, object?>>(result.Diagnostics.Metrics["pdfBackend"]);
        Assert.Equal("PDFium", backend["name"]);
        Assert.True(Assert.IsType<bool>(backend["canExtractNativeText"]));
        Assert.True(Assert.IsType<bool>(backend["canExtractGlyphBounds"]));
        Assert.True(Assert.IsType<bool>(backend["canExtractImageRegions"]));
        Assert.True(Assert.IsType<bool>(backend["canExtractEmbeddedImages"]));
        Assert.True(Assert.IsType<bool>(backend["canRenderPages"]));
        Assert.True(Assert.IsType<bool>(backend["canReportFontMetadata"]));
        Assert.True(Assert.IsType<bool>(backend["canReportMarkedContent"]));
        Assert.True(Assert.IsType<bool>(backend["canReportTextRenderMode"]));
        Assert.True(Assert.IsType<bool>(backend["canReportTextColors"]));
        var renderFormats = Assert.IsType<string[]>(backend["supportedRenderFormats"]);
        Assert.Contains(PdfRenderImageFormat.Bmp.ToString(), renderFormats);
        Assert.Contains(PdfRenderImageFormat.Png.ToString(), renderFormats);
        Assert.DoesNotContain(PdfRenderImageFormat.Jpeg.ToString(), renderFormats);
        Assert.Equal("PDFium", result.Diagnostics.Metrics["pdfBackendName"]);
        var ocrExecutor = Assert.IsType<Dictionary<string, object?>>(result.Diagnostics.Metrics["pdfOcrExecutor"]);
        Assert.Equal("NoOp", ocrExecutor["kind"]);
        Assert.False(Assert.IsType<bool>(ocrExecutor["endpointConfigured"]));
        Assert.Equal("eng", ocrExecutor["language"]);
    }

    [Fact]
    public async Task ExtractAsync_ReportsHttpOcrExecutorDiagnosticsWhenEndpointConfigured()
    {
        var input = ContentInput.FromBytes(CreatePdfFixture("endpoint diagnostics sentinel"), "ocr-endpoint.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            OcrEnabled = false,
            OcrEndpoint = new Uri("https://ocr.example.test/recognize"),
            OcrLanguage = "spa"
        });

        var ocrExecutor = Assert.IsType<Dictionary<string, object?>>(result.Diagnostics.Metrics["pdfOcrExecutor"]);
        Assert.Equal("Http", ocrExecutor["kind"]);
        Assert.False(Assert.IsType<bool>(ocrExecutor["enabled"]));
        Assert.True(Assert.IsType<bool>(ocrExecutor["endpointConfigured"]));
        Assert.Equal("spa", ocrExecutor["language"]);
    }

    [Fact]
    public async Task ExtractAsync_HonorsTargetPagesAndMaxPages()
    {
        var input = ContentInput.FromBytes(
            CreatePdfFixture("first page sentinel", "second page sentinel", "third page sentinel"),
            "multi.pdf",
            MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            TargetPages = "2-3",
            MaxPages = 1,
            OcrEnabled = false
        });

        var page = Assert.Single(result.Pages);
        Assert.Equal(2, page.Number);
        Assert.Contains("second page sentinel", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("first page sentinel", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_WithRealPdfiumMaterializesScreenshotAssetWithExpectedDimensions()
    {
        var input = ContentInput.FromBytes(CreatePdfFixture("screenshot sentinel"), "screenshot.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            OcrEnabled = false,
            IncludeScreenshots = true,
            Dpi = 144
        });

        var page = Assert.Single(result.Pages);
        var asset = Assert.Single(result.Assets);
        Assert.Same(asset, Assert.Single(page.Assets));
        Assert.Equal(ExtractedAssetKind.PageScreenshot, asset.Kind);
        Assert.Equal("page-1-screenshot.png", asset.Name);
        Assert.Equal(MimeTypes.ImagePng, asset.MimeType);
        Assert.Equal(1, asset.PageNumber);
        Assert.Equal(new BoundingBox(0, 0, 612, 792), asset.BoundingBox);
        Assert.Equal(1224, Assert.IsType<int>(asset.Metadata["width"]));
        Assert.Equal(1584, Assert.IsType<int>(asset.Metadata["height"]));
        Assert.Equal(144f, Assert.IsType<float>(asset.Metadata["dpi"]));
        Assert.Equal(PdfRenderImageFormat.Png.ToString(), asset.Metadata["encodedFormat"]);
        Assert.True(asset.Data.Length > 32);
        var bytes = asset.Data.ToArray();
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], bytes[..8]);
    }

    [Fact]
    public async Task ExtractAsync_WithRealPdfiumRecordsWarningWhenJpegScreenshotRequested()
    {
        var input = ContentInput.FromBytes(CreatePdfFixture("jpeg screenshot sentinel"), "jpeg-screenshot.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            OcrEnabled = false,
            IncludeScreenshots = true,
            ScreenshotFormat = PdfRenderImageFormat.Jpeg
        });

        Assert.Empty(result.Assets);
        Assert.Contains(result.Diagnostics.Warnings, static warning =>
            warning.Contains("screenshot rendering failed", StringComparison.Ordinal)
            && warning.Contains("Jpeg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_WithRealPdfiumReportsEmbeddedImageBounds()
    {
        var input = ContentInput.FromBytes(CreateImagePdfFixture(), "image-bounds.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            OcrEnabled = false,
            IncludeEmbeddedImages = true
        });

        var page = Assert.Single(result.Pages);
        var asset = Assert.Single(result.Assets);
        Assert.Same(asset, Assert.Single(page.Assets));
        Assert.Equal(ExtractedAssetKind.EmbeddedImage, asset.Kind);
        Assert.Equal(1, asset.PageNumber);
        var box = Assert.NotNull(asset.BoundingBox);
        AssertClose(50, box.X);
        AssertClose(50, box.Y);
        AssertClose(40, box.Width);
        AssertClose(30, box.Height);
        Assert.Equal(1, page.Metadata["imageRegionCount"]);
        Assert.Equal(2u, asset.Metadata["widthInSamples"]);
        Assert.Equal(2u, asset.Metadata["heightInSamples"]);
        Assert.NotEmpty(asset.Data.ToArray());
    }

    [Fact]
    public async Task ExtractAsync_WithRealPdfiumReportsNativeTextFontAndColorMetadata()
    {
        var input = ContentInput.FromBytes(CreateStyledTextPdfFixture(), "styled-text.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            OcrEnabled = false,
            IncludeTextItems = true
        });

        var page = Assert.Single(result.Pages);
        var item = Assert.Single(page.TextItems, static textItem => textItem.Text.Contains("styled sentinel", StringComparison.Ordinal));
        Assert.Equal(PdfTextLayerKind.Native, item.Layer);
        Assert.True(item.BoundingBox.Width > 0);
        Assert.True(item.BoundingBox.Height > 0);
        var font = item.Font;
        Assert.NotNull(font);
        AssertClose(18, font.Size.GetValueOrDefault(), tolerance: 0.1f);
        Assert.Contains("Helvetica", font.Name ?? font.BaseName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("#FFFF0000", item.FillColorArgb);
        Assert.False(item.HasUnicodeMapError.GetValueOrDefault());
        Assert.Equal(1f, item.Confidence);
    }

    [Fact]
    public async Task ExtractAsync_WithRealPdfiumFlagsBadToUnicodeCMapAsCorruptNativeText()
    {
        var input = ContentInput.FromBytes(CreateBadToUnicodePdfFixture(), "bad-cmap.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            OcrEnabled = true,
            IncludeTextItems = true
        });

        var page = Assert.Single(result.Pages);
        Assert.True(page.Quality.NativeTextLength >= 24);
        Assert.Equal(page.Quality.NativeTextLength, page.Quality.CorruptNativeTextLength);
        Assert.Equal(0, page.Quality.NonGarbledNativeTextLength);
        Assert.True(page.Quality.LooksGarbled);
        Assert.True(page.Quality.NeedsOcr);
        Assert.True(page.OcrDecision.ShouldRun);
        Assert.Contains(PdfOcrDecisionReason.GarbledNativeText, page.OcrDecision.Reasons);
        Assert.All(page.TextItems.Where(static item => item.Layer == PdfTextLayerKind.Native), static item =>
        {
            Assert.True(item.Font?.LooksCorrupt);
            Assert.Contains(item.Text, static ch => ch >= '\uE000' && ch <= '\uF8FF');
        });
    }

    [Fact]
    public async Task ExtractAsync_WithRealPdfiumReportsMarkedContentRenderModeAndStrokeColor()
    {
        var input = ContentInput.FromBytes(CreateMarkedContentTextPdfFixture(), "marked-content.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions
        {
            OcrEnabled = false,
            IncludeTextItems = true
        });

        var page = Assert.Single(result.Pages);
        var item = Assert.Single(page.TextItems, static textItem => textItem.Text.Contains("mcid stroke sentinel", StringComparison.Ordinal));
        Assert.Equal(7, item.MarkedContentId);
        Assert.Contains("STROKE", item.RenderMode ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("#FF0000FF", item.FillColorArgb);
        Assert.Equal("#FFFF0000", item.StrokeColorArgb);
    }

    [Fact]
    public async Task ExtractAsync_LocalPdfFixtureCorpusBaselineMatrixDoesNotRegressKnownNastyPdfs()
    {
        var cases = LocalPdfFixtureCorpusCases().Where(static testCase => File.Exists(testCase.Path)).ToArray();
        if (cases.Length == 0)
        {
            return;
        }

        var engine = new PdfExtractionEngine();
        foreach (var testCase in cases)
        {
            var input = ContentInput.FromBytes(File.ReadAllBytes(testCase.Path), Path.GetFileName(testCase.Path), MimeTypes.Pdf);

            var result = await engine.ExtractAsync(input, new PdfExtractionOptions
            {
                OcrEnabled = false,
                IncludeTextItems = true,
                IncludeEmbeddedImages = true,
                IncludeScreenshots = testCase.IncludeScreenshot,
                MaxPages = 1
            });

            var page = Assert.Single(result.Pages);
            Assert.NotEmpty(result.Pages);
            Assert.Contains(result.Diagnostics.Timings, static timing => timing.Name == "pdf.total");
            Assert.True(
                result.Text.Length >= testCase.MinTextLength && result.Text.Length <= testCase.MaxTextLength,
                $"{testCase.Name}: expected text length in [{testCase.MinTextLength}, {testCase.MaxTextLength}], actual {result.Text.Length}.");
            Assert.True(page.TextItems.Count >= testCase.MinTextItemCount, $"{testCase.Name}: expected at least {testCase.MinTextItemCount} text items, actual {page.TextItems.Count}.");
            Assert.True(result.Assets.Count >= testCase.MinAssetCount, $"{testCase.Name}: expected at least {testCase.MinAssetCount} assets, actual {result.Assets.Count}.");
            Assert.True(result.Diagnostics.Warnings.Count <= testCase.MaxWarningCount, $"{testCase.Name}: expected at most {testCase.MaxWarningCount} warnings, actual {result.Diagnostics.Warnings.Count}.");
            Assert.Equal(testCase.ExpectedOcrCandidatePageCount, result.Diagnostics.OcrCandidatePageCount);
            if (testCase.ExpectedRotation is not null)
            {
                Assert.Equal(testCase.ExpectedRotation.Value, page.Metadata["rotation"]);
            }

            var imageRegionCount = Assert.IsType<int>(page.Metadata["imageRegionCount"]);
            Assert.True(imageRegionCount >= testCase.MinImageRegionCount, $"{testCase.Name}: expected at least {testCase.MinImageRegionCount} image regions, actual {imageRegionCount}.");
            var rotatedItemCount = page.TextItems.Count(static item => CanonicalTestRotation(item.Rotation) != 0);
            var fontInfoItemCount = page.TextItems.Count(static item => item.Font is not null);
            var renderModeItemCount = page.TextItems.Count(static item => item.RenderMode is not null);
            var colorItemCount = page.TextItems.Count(static item => item.FillColorArgb is not null || item.StrokeColorArgb is not null);
            var unicodeMapErrorItemCount = page.TextItems.Count(static item => item.HasUnicodeMapError == true);
            var markedContentItemCount = page.TextItems.Count(static item => item.MarkedContentId is not null);
            Assert.True(rotatedItemCount >= testCase.MinRotatedItemCount, $"{testCase.Name}: expected at least {testCase.MinRotatedItemCount} rotated text items, actual {rotatedItemCount}.");
            Assert.True(fontInfoItemCount >= testCase.MinFontInfoItemCount, $"{testCase.Name}: expected at least {testCase.MinFontInfoItemCount} font metadata items, actual {fontInfoItemCount}.");
            Assert.True(renderModeItemCount >= testCase.MinRenderModeItemCount, $"{testCase.Name}: expected at least {testCase.MinRenderModeItemCount} render mode items, actual {renderModeItemCount}.");
            Assert.True(colorItemCount >= testCase.MinColorItemCount, $"{testCase.Name}: expected at least {testCase.MinColorItemCount} color metadata items, actual {colorItemCount}.");
            Assert.True(unicodeMapErrorItemCount >= testCase.MinUnicodeMapErrorItemCount, $"{testCase.Name}: expected at least {testCase.MinUnicodeMapErrorItemCount} Unicode-map error items, actual {unicodeMapErrorItemCount}.");
            Assert.True(markedContentItemCount >= testCase.MinMarkedContentItemCount, $"{testCase.Name}: expected at least {testCase.MinMarkedContentItemCount} marked-content items, actual {markedContentItemCount}.");
            if (testCase.ExpectedNeedsOcr is not null)
            {
                Assert.Equal(testCase.ExpectedNeedsOcr.Value, page.Quality.NeedsOcr);
            }

            if (testCase.IncludeScreenshot)
            {
                var screenshot = Assert.Single(page.Assets, static asset => asset.Kind == ExtractedAssetKind.PageScreenshot);
                Assert.Equal(MimeTypes.ImagePng, screenshot.MimeType);
                Assert.True(screenshot.Data.Length > 8, $"{testCase.Name}: screenshot asset was empty.");
            }

            var projection = Assert.IsType<Dictionary<string, object?>>(page.Metadata["projection"]);
            if (testCase.MinProjectionLineCount > 0)
            {
                Assert.True(GetMetricAsInt(projection, "lineCount", testCase.Name) >= testCase.MinProjectionLineCount, $"{testCase.Name}: projection line count regressed.");
            }

            if (testCase.RequiredText is not null)
            {
                Assert.Contains(testCase.RequiredText, result.Text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task ExtractAsync_OptionalFullPdfCorpusSweepDoesNotCrashNativeBackend()
    {
        var root = global::System.Environment.GetEnvironmentVariable("HPD_PDF_FULL_CORPUS_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        root = ResolveOptionalCorpusRoot(root);
        Assert.True(Directory.Exists(root), $"HPD_PDF_FULL_CORPUS_ROOT does not exist: {root}");
        var maxFiles = GetOptionalCorpusInt32("HPD_PDF_FULL_CORPUS_MAX_FILES", defaultValue: 250);
        var maxPages = GetOptionalCorpusInt32("HPD_PDF_FULL_CORPUS_MAX_PAGES", defaultValue: 3);
        var strict = IsTruthy(global::System.Environment.GetEnvironmentVariable("HPD_PDF_FULL_CORPUS_STRICT"));
        var pdfs = Directory
            .EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Take(maxFiles)
            .ToArray();

        Assert.NotEmpty(pdfs);

        var engine = new PdfExtractionEngine();
        var failures = new List<string>();
        var processed = 0;
        var totalPages = 0;
        var totalTextLength = 0;
        var totalTextItems = 0;
        var totalWarnings = 0;
        var totalOcrCandidates = 0;
        var totalAssets = 0;

        foreach (var path in pdfs)
        {
            var relativePath = Path.GetRelativePath(root, path);
            try
            {
                var input = ContentInput.FromPath(path, MimeTypes.Pdf);
                var result = await engine.ExtractAsync(input, new PdfExtractionOptions
                {
                    OcrEnabled = false,
                    IncludeTextItems = true,
                    IncludeEmbeddedImages = true,
                    MaxPages = maxPages
                });

                Assert.Contains(result.Diagnostics.Timings, static timing => timing.Name == "pdf.total");
                Assert.True(result.Pages.Count <= maxPages, $"{relativePath}: extracted more pages than MaxPages.");
                Assert.Equal(result.Pages.Count, Assert.IsType<int>(result.Diagnostics.Metrics["pageCount"]));
                Assert.Equal(result.Assets.Count, Assert.IsType<int>(result.Diagnostics.Metrics["assetCount"]));
                Assert.Contains("pdfBackend", result.Diagnostics.Metrics.Keys);
                Assert.Contains("pdfOcrExecutor", result.Diagnostics.Metrics.Keys);

                processed++;
                totalPages += result.Pages.Count;
                totalTextLength += result.Text.Length;
                totalTextItems += result.Pages.Sum(static page => page.TextItems.Count);
                totalWarnings += result.Diagnostics.Warnings.Count;
                totalOcrCandidates += result.Diagnostics.OcrCandidatePageCount;
                totalAssets += result.Assets.Count;
            }
            catch (Exception error) when (error is not Xunit.Sdk.XunitException)
            {
                failures.Add($"{relativePath}: {error.GetType().Name}: {error.Message}");
            }
        }

        var summary =
            $"Full PDF corpus sweep root='{root}', files={pdfs.Length}, processed={processed}, failures={failures.Count}, pages={totalPages}, textLength={totalTextLength}, textItems={totalTextItems}, assets={totalAssets}, warnings={totalWarnings}, ocrCandidates={totalOcrCandidates}.";

        Assert.True(processed > 0 || failures.Count > 0, summary);
        if (strict && failures.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(summary + global::System.Environment.NewLine + string.Join(global::System.Environment.NewLine, failures.Take(50)));
        }
    }

    [Fact]
    public async Task ExtractAsync_OptionalFullPdfCorpusBaselineMatchesManifest()
    {
        var root = global::System.Environment.GetEnvironmentVariable("HPD_PDF_FULL_CORPUS_ROOT");
        var baselinePath = global::System.Environment.GetEnvironmentVariable("HPD_PDF_FULL_CORPUS_BASELINE");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(baselinePath))
        {
            return;
        }

        root = ResolveOptionalCorpusRoot(root);
        baselinePath = ResolveOptionalCorpusFile(baselinePath);
        Assert.True(Directory.Exists(root), $"HPD_PDF_FULL_CORPUS_ROOT does not exist: {root}");
        Assert.True(File.Exists(baselinePath), $"HPD_PDF_FULL_CORPUS_BASELINE does not exist: {baselinePath}");

        var manifest = JsonSerializer.Deserialize<PdfCorpusBaselineManifest>(
            File.ReadAllText(baselinePath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(manifest);
        Assert.Equal(1, manifest.FormatVersion);
        Assert.NotEmpty(manifest.Entries);

        var maxFiles = GetOptionalCorpusInt32("HPD_PDF_FULL_CORPUS_MAX_FILES", defaultValue: int.MaxValue);
        var engine = new PdfExtractionEngine();
        var failures = new List<string>();
        foreach (var entry in manifest.Entries.Take(maxFiles))
        {
            if (!string.Equals(entry.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(entry.FailureType))
                    failures.Add($"{entry.Path}: failed baseline entry is missing failureType");
                if (string.IsNullOrWhiteSpace(entry.FailureCategory))
                    failures.Add($"{entry.Path}: failed baseline entry is missing failureCategory");
                if (string.IsNullOrWhiteSpace(entry.ReferenceExpectation))
                    failures.Add($"{entry.Path}: failed baseline entry is missing referenceExpectation");
                continue;
            }

            var path = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                failures.Add($"{entry.Path}: missing corpus PDF");
                continue;
            }

            try
            {
                var result = await engine.ExtractAsync(ContentInput.FromPath(path, MimeTypes.Pdf), new PdfExtractionOptions
                {
                    OcrEnabled = false,
                    IncludeTextItems = true,
                    IncludeEmbeddedImages = true,
                    MaxPages = manifest.MaxPages,
                    Password = GetBaselinePassword(entry)
                });

                AssertRange(entry.Path, "pageCount", entry.PageCount, result.Pages.Count);
                AssertRange(entry.Path, "textLength", entry.TextLength, result.Text.Length);
                AssertRange(entry.Path, "textItemCount", entry.TextItemCount, result.Pages.Sum(static page => page.TextItems.Count));
                AssertRange(entry.Path, "assetCount", entry.AssetCount, result.Assets.Count);
                AssertRange(entry.Path, "warningCount", entry.WarningCount, result.Diagnostics.Warnings.Count);
                AssertRange(entry.Path, "ocrCandidatePageCount", entry.OcrCandidatePageCount, result.Diagnostics.OcrCandidatePageCount);
                AssertRange(entry.Path, "projectionLineCount", entry.ProjectionLineCount, result.Pages.Sum(GetProjectionLineCountOrZero));
            }
            catch (Exception error) when (error is not Xunit.Sdk.XunitException)
            {
                failures.Add($"{entry.Path}: {error.GetType().Name}: {error.Message}");
            }
            catch (Xunit.Sdk.XunitException error)
            {
                failures.Add(error.Message);
            }
        }

        Assert.True(failures.Count == 0, string.Join(global::System.Environment.NewLine, failures.Take(50)));
    }

    [Fact]
    public async Task ExtractAsync_StrictSparsePageFailsWhenRendererUnavailable()
    {
        var input = ContentInput.FromBytes(CreatePdfFixture("x"), "sparse.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine(backend: new FakePdfBackend
        {
            Page = new FakePdfPage
            {
                NativeTextItems = Array.Empty<PdfTextItem>(),
                ImageRegions =
                [
                    new PdfImageRegion
                    {
                        BoundingBox = new BoundingBox(0, 0, 200, 100),
                        PageCoverage = 1,
                        IsOcrRelevant = true
                    }
                ],
                RenderError = new InvalidOperationException("rendering is not wired")
            }
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExtractAsync(input, new PdfExtractionOptions { OcrEnabled = true }).AsTask());

        Assert.Contains("OCR failed for all 1 strict PDF page", exception.Message, StringComparison.Ordinal);
        Assert.Contains("rendering is not wired", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PageQualityAnalyzer_FlagsGarbledNativeTextForOcr()
    {
        var items = new[]
        {
            new PdfTextItem
            {
                Text = "bcdfg hjklm npqrst vwxyz bcdfg hjklm",
                BoundingBox = new BoundingBox(0, 0, 40, 10),
                Layer = PdfTextLayerKind.Native
            }
        };

        var quality = PdfPageQualityAnalyzer.Analyze(new PdfPageQualityInput
        {
            PageSize = new PageSize(100, 100),
            TextItems = items
        });
        var decision = PdfPageQualityAnalyzer.PlanOcr(quality, new PdfExtractionOptions { OcrEnabled = true });

        Assert.True(quality.LooksGarbled);
        Assert.True(decision.ShouldRun);
        Assert.Contains(PdfOcrDecisionReason.GarbledNativeText, decision.Reasons);
        Assert.Equal(PdfOcrFailurePolicy.FailIfAllOcrFails, decision.FailurePolicy);
    }

    [Fact]
    public void PageQualityAnalyzer_FlagsCorruptNativeTextForOcr()
    {
        var items = new[]
        {
            new PdfTextItem
            {
                Text = "bad native text bad native text",
                BoundingBox = new BoundingBox(0, 0, 90, 80),
                Layer = PdfTextLayerKind.Native,
                Font = new PdfFontInfo { LooksCorrupt = true },
                HasUnicodeMapError = true
            }
        };

        var quality = PdfPageQualityAnalyzer.Analyze(new PdfPageQualityInput
        {
            PageSize = new PageSize(100, 100),
            TextItems = items
        });
        var decision = PdfPageQualityAnalyzer.PlanOcr(quality, new PdfExtractionOptions { OcrEnabled = true });

        Assert.Equal(items[0].Text.Length, quality.NativeTextLength);
        Assert.Equal(items[0].Text.Length, quality.CorruptNativeTextLength);
        Assert.Equal(items[0].Text.Length, quality.UnicodeMapErrorTextLength);
        Assert.Equal(0, quality.NonGarbledNativeTextLength);
        Assert.True(quality.LooksGarbled);
        Assert.Contains(PdfOcrDecisionReason.GarbledNativeText, decision.Reasons);
    }

    [Fact]
    public void PageQualityAnalyzer_TreatsImageOnlyNativeTextAsBestEffortEnrichment()
    {
        var items = new[]
        {
            new PdfTextItem
            {
                Text = "this page already has substantial native text coverage",
                BoundingBox = new BoundingBox(0, 0, 80, 80),
                Layer = PdfTextLayerKind.Native
            }
        };

        var quality = PdfPageQualityAnalyzer.Analyze(new PdfPageQualityInput
        {
            PageSize = new PageSize(100, 100),
            TextItems = items,
            ImageRegions =
            [
                new PdfImageRegion
                {
                    BoundingBox = new BoundingBox(0, 0, 80, 80),
                    PageCoverage = 0.64f,
                    IsOcrRelevant = true
                }
            ]
        });
        var decision = PdfPageQualityAnalyzer.PlanOcr(quality, new PdfExtractionOptions { OcrEnabled = true });

        Assert.True(quality.HasEmbeddedImages);
        Assert.True(quality.HasOcrRelevantImages);
        Assert.True(decision.ShouldRun);
        Assert.Contains(PdfOcrDecisionReason.EmbeddedImages, decision.Reasons);
        Assert.Equal(PdfOcrFailurePolicy.BestEffortEnrichment, decision.FailurePolicy);
    }

    [Fact]
    public void PageQualityAnalyzer_DoesNotPlanOcrForDecorativeImageOnly()
    {
        var items = new[]
        {
            new PdfTextItem
            {
                Text = "this page already has substantial native text coverage",
                BoundingBox = new BoundingBox(0, 0, 80, 80),
                Layer = PdfTextLayerKind.Native
            }
        };

        var quality = PdfPageQualityAnalyzer.Analyze(new PdfPageQualityInput
        {
            PageSize = new PageSize(100, 100),
            TextItems = items,
            ImageRegions =
            [
                new PdfImageRegion
                {
                    BoundingBox = new BoundingBox(0, 0, 10, 10),
                    PageCoverage = 0.01f,
                    IsOcrRelevant = false
                }
            ]
        });
        var decision = PdfPageQualityAnalyzer.PlanOcr(quality, new PdfExtractionOptions { OcrEnabled = true });

        Assert.True(quality.HasEmbeddedImages);
        Assert.False(quality.HasOcrRelevantImages);
        Assert.False(decision.ShouldRun);
    }

    [Fact]
    public void PageQualityAnalyzer_DoesNotPlanOcrWhenDisabled()
    {
        var quality = new PdfPageQuality
        {
            NativeTextLength = 0,
            NonGarbledNativeTextLength = 0,
            NativeTextCoverage = 0,
            NeedsOcr = true,
            LooksScanned = true
        };

        var decision = PdfPageQualityAnalyzer.PlanOcr(quality, new PdfExtractionOptions { OcrEnabled = false });

        Assert.False(decision.ShouldRun);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public async Task ExtractAsync_DoesNotMaterializeEmbeddedImageAssetsUnlessRequested()
    {
        var backend = new FakePdfBackend();
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new FakeLayoutProjector());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = false, IncludeEmbeddedImages = false });

        Assert.Empty(result.Assets);
        Assert.Equal(0, backend.Page.AssetExtractionCalls);
        Assert.Equal(false, result.Diagnostics.Metrics["embeddedImageAssetsIncluded"]);
    }

    [Fact]
    public async Task ExtractAsync_MaterializesEmbeddedImageAssetsWhenRequested()
    {
        var backend = new FakePdfBackend();
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new FakeLayoutProjector());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = false, IncludeEmbeddedImages = true });

        var asset = Assert.Single(result.Assets);
        Assert.Equal(ExtractedAssetKind.EmbeddedImage, asset.Kind);
        Assert.Equal(1, backend.Page.AssetExtractionCalls);
        Assert.Equal(true, result.Diagnostics.Metrics["embeddedImageAssetsIncluded"]);
    }

    [Fact]
    public async Task ExtractAsync_MaterializesPageScreenshotAssetsWhenRequested()
    {
        var backend = new FakePdfBackend
        {
            Page =
            {
                RenderedPage = FakeRenderedPage()
            }
        };
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new FakeLayoutProjector());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = false, IncludeScreenshots = true, Dpi = 144 });

        var asset = Assert.Single(result.Assets);
        Assert.Equal(ExtractedAssetKind.PageScreenshot, asset.Kind);
        Assert.Equal(MimeTypes.ImagePng, asset.MimeType);
        Assert.Equal(12, asset.PageNumber);
        Assert.Equal(new BoundingBox(0, 0, 200, 100), asset.BoundingBox);
        Assert.Equal("fake-render"u8.ToArray(), asset.Data.ToArray());
        Assert.Equal(144f, Assert.IsType<float>(asset.Metadata["dpi"]));
        Assert.Equal(PdfRenderImageFormat.Png.ToString(), asset.Metadata["encodedFormat"]);
        Assert.Equal(1, backend.Page.RenderRequests.Count);
        Assert.Equal(PdfRenderPurpose.Screenshot, backend.Page.RenderRequests[0].Purpose);
        Assert.Equal(PdfRenderImageFormat.Png, backend.Page.RenderRequests[0].Format);
    }

    [Fact]
    public async Task ExtractAsync_RecordsWarningWhenScreenshotRenderFails()
    {
        var backend = new FakePdfBackend();
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new FakeLayoutProjector());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = false, IncludeScreenshots = true });

        Assert.Empty(result.Assets);
        Assert.Contains(result.Diagnostics.Warnings, warning => warning.Contains("screenshot rendering failed", StringComparison.Ordinal));
        Assert.Equal(1, backend.Page.RenderRequests.Count);
        Assert.Equal(PdfRenderPurpose.Screenshot, backend.Page.RenderRequests[0].Purpose);
    }

    [Fact]
    public void PageQualityAnalyzer_ReportsInvisibleTextRatio()
    {
        var items = new[]
        {
            new PdfTextItem
            {
                Text = "visible text",
                BoundingBox = new BoundingBox(0, 0, 25, 10),
                Layer = PdfTextLayerKind.Native
            },
            new PdfTextItem
            {
                Text = "hidden text",
                BoundingBox = new BoundingBox(0, 0, 25, 10),
                Layer = PdfTextLayerKind.InvisibleOcrLayer
            }
        };

        var quality = PdfPageQualityAnalyzer.Analyze(new PdfPageQualityInput
        {
            PageSize = new PageSize(100, 100),
            TextItems = items
        });

        Assert.Equal(0.5f, quality.InvisibleTextRatio);
        Assert.Equal(items[0].Text.Length + items[1].Text.Length, quality.NativeTextLength);
    }

    [Fact]
    public void GarbledTextDetector_IgnoresShortAndNonLatinText()
    {
        Assert.False(PdfGarbledTextDetector.IsLikelyGarbled("bcdfg"));
        Assert.False(PdfGarbledTextDetector.IsLikelyGarbled("東京 12345"));
        Assert.True(PdfGarbledTextDetector.IsLikelyGarbled("bcdfg hjklm npqrst vwxyz"));
    }

    [Fact]
    public void GarbledTextDetector_ScoresNaturalLanguageAsClean()
    {
        var items = new[]
        {
            new PdfTextItem
            {
                Text = "This page contains ordinary readable English vowels.",
                BoundingBox = new BoundingBox(0, 0, 80, 10)
            }
        };

        Assert.False(PdfGarbledTextDetector.IsLikelyGarbled(items[0].Text));
        Assert.Equal(0, PdfGarbledTextDetector.ScorePage(items));
    }

    [Fact]
    public void CitationSearch_MatchesPhrasesAcrossAdjacentItemsAndUnionsBounds()
    {
        var page = new PdfPage
        {
            Number = 4,
            TextItems =
            [
                new PdfTextItem { Text = "0C", BoundingBox = new BoundingBox(10, 10, 10, 8), Font = TestFont(10) },
                new PdfTextItem { Text = "to", BoundingBox = new BoundingBox(30, 10, 8, 8), Font = TestFont(10) },
                new PdfTextItem { Text = "70C", BoundingBox = new BoundingBox(50, 10, 15, 8), Font = TestFont(10) }
            ]
        };
        var result = new PdfExtractionResult { Pages = [page] };
        var search = new PdfCitationSearch();

        var match = Assert.Single(search.Search(result, "0C to 70C"));

        Assert.Equal(4, match.PageNumber);
        Assert.Equal(new BoundingBox(10, 10, 55, 8), match.BoundingBox);
        Assert.Equal(3, match.Items.Count);
    }

    [Fact]
    public void CitationSearch_JoinsTouchingSameLineItemsWithoutSyntheticSpace()
    {
        var page = new PdfPage
        {
            Number = 1,
            TextItems =
            [
                new PdfTextItem { Text = "hel", BoundingBox = new BoundingBox(0, 0, 12, 8), Font = TestFont(10) },
                new PdfTextItem { Text = "lo", BoundingBox = new BoundingBox(12.5f, 0, 8, 8), Font = TestFont(10) }
            ]
        };

        var result = new PdfExtractionResult { Pages = [page] };
        var search = new PdfCitationSearch();

        var match = Assert.Single(search.Search(result, "hello"));

        Assert.Equal(new BoundingBox(0, 0, 20.5f, 8), match.BoundingBox);
    }

    [Fact]
    public void CitationSearch_RespectsCaseSensitivityAndReturnsEmptyForMissingPhrase()
    {
        var page = new PdfPage
        {
            Number = 1,
            TextItems =
            [
                new PdfTextItem { Text = "Hello", BoundingBox = new BoundingBox(0, 0, 20, 8), Font = TestFont(10) },
                new PdfTextItem { Text = "World", BoundingBox = new BoundingBox(30, 0, 25, 8), Font = TestFont(10) }
            ]
        };
        var search = new PdfCitationSearch();
        var result = new PdfExtractionResult { Pages = [page] };

        Assert.Empty(search.Search(result, "hello world", caseSensitive: true));
        Assert.Single(search.Search(result, "hello world"));
        Assert.Empty(search.Search(result, "not here"));
    }

    [Fact]
    public void LayoutProjector_InsertsSpacingAndVerticalBreaksFromGeometry()
    {
        var items = new[]
        {
            new PdfTextItem { Text = "Left", BoundingBox = new BoundingBox(0, 0, 20, 10) },
            new PdfTextItem { Text = "Right", BoundingBox = new BoundingBox(80, 0, 25, 10) },
            new PdfTextItem { Text = "Next", BoundingBox = new BoundingBox(0, 40, 20, 10) }
        };

        var text = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(100, 100),
            TextItems = items
        }).Text;

        Assert.Contains("Left", text, StringComparison.Ordinal);
        Assert.Contains("Right", text, StringComparison.Ordinal);
        Assert.Contains("\n\n", text, StringComparison.Ordinal);
        Assert.Matches("Left\\s{2,}Right", text);
    }

    [Fact]
    public void LayoutProjector_ReturnsEmptyForNoItems()
    {
        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(100, 100),
            TextItems = Array.Empty<PdfTextItem>()
        });

        Assert.Equal(string.Empty, result.Text);
        Assert.Empty(result.ProjectedItems);
    }

    [Fact]
    public void LayoutProjector_HandlesTextSparseZeroWidthItems()
    {
        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(612, 792),
            TextItems =
            [
                PdfItem("", 10, 10, 0, 10),
                PdfItem("", 10, 30, 0, 20)
            ]
        });

        Assert.Equal(string.Empty, result.Text);
        Assert.Empty(result.ProjectedItems);
        Assert.Equal(2, Assert.IsType<int>(result.Metrics["sourceItemCount"]));
    }

    [Fact]
    public void LayoutProjector_UnionsOriginalBoundsWhenWordItemsMerge()
    {
        const float y = 50.25f;

        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(612, 792),
            TextItems =
            [
                PdfItem("A", 10, y, 10, 8),
                PdfItem("B", 24, y + 0.01f, 8, 7.5f)
            ]
        });

        var item = Assert.Single(result.ProjectedItems, static item => item.Text == "A B");
        Assert.Equal(new BoundingBox(10, y, 22, 8), item.BoundingBox);
        Assert.True(item.Metadata.ContainsKey("projectionBounds"));
        Assert.True(GetMetric<int>(result, "wordMergeCount") > 0);
    }

    [Fact]
    public void LayoutProjector_UnionsOriginalBoundsWhenContinuousItemsMerge()
    {
        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(612, 792),
            TextItems =
            [
                PdfItem("ab", 40, 100, 10.2f, 9),
                PdfItem("cd", 50.2f, 100, 12.3f, 9)
            ]
        });

        var item = Assert.Single(result.ProjectedItems, static item => item.Text == "abcd");
        Assert.True(Math.Abs(item.BoundingBox.X - 40) < 0.001f);
        Assert.True(Math.Abs(item.BoundingBox.Y - 100) < 0.001f);
        Assert.True(Math.Abs(item.BoundingBox.Width - 22.5f) < 0.01f);
        Assert.True(Math.Abs(item.BoundingBox.Height - 9) < 0.01f);
    }

    [Fact]
    public void LayoutProjector_CanonicalRotationSnapsCardinalsAndNearCardinals()
    {
        Assert.Equal(0, InvokeCanonicalRotation(0));
        Assert.Equal(90, InvokeCanonicalRotation(90));
        Assert.Equal(180, InvokeCanonicalRotation(180));
        Assert.Equal(270, InvokeCanonicalRotation(270));
        Assert.Equal(0, InvokeCanonicalRotation(1));
        Assert.Equal(90, InvokeCanonicalRotation(88.5f));
        Assert.Equal(270, InvokeCanonicalRotation(271));
    }

    [Fact]
    public void LayoutProjector_CanonicalRotationSnapsNear360ToZero()
    {
        Assert.Equal(0, InvokeCanonicalRotation(358));
        Assert.Equal(0, InvokeCanonicalRotation(359));
        Assert.Equal(0, InvokeCanonicalRotation(359.5f));
        Assert.Equal(0, InvokeCanonicalRotation(360));
        Assert.Equal(0, InvokeCanonicalRotation(-1));
    }

    [Fact]
    public void LayoutProjector_CanonicalRotationPassesThroughNonCardinalAngles()
    {
        Assert.Equal(45, InvokeCanonicalRotation(45));
        Assert.Equal(357, InvokeCanonicalRotation(357));
    }

    [Fact]
    public void LayoutProjector_KeepsTwoColumnRowsSeparatedByStableAnchors()
    {
        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(240, 160),
            TextItems =
            [
                PdfItem("Left A", 10, 10, 36, 10),
                PdfItem("Right A", 140, 10, 42, 10),
                PdfItem("Left B", 10, 28, 36, 10),
                PdfItem("Right B", 140, 28, 42, 10),
                PdfItem("Left C", 10, 46, 36, 10),
                PdfItem("Right C", 140, 46, 42, 10)
            ]
        });

        Assert.Contains("Left A", result.Text, StringComparison.Ordinal);
        Assert.Contains("Right A", result.Text, StringComparison.Ordinal);
        Assert.Contains("Left B", result.Text, StringComparison.Ordinal);
        Assert.Contains("Right B", result.Text, StringComparison.Ordinal);
        Assert.True(GetMetric<int>(result, "leftAnchorCount") >= 2);
        Assert.True(GetMetric<int>(result, "gridBlockCount") >= 1);
        Assert.Contains(result.ProjectedItems, static item => item.Text == "Right B" && item.Metadata["projectionLeftAnchor"] is not null);
        Assert.Matches("Left A\\s{4,}Right A", result.Text);
    }

    [Fact]
    public void LayoutProjector_RendersFlowingParagraphAsFlowBlock()
    {
        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(220, 160),
            TextItems =
            [
                PdfItem("This", 10, 10, 20, 10),
                PdfItem("is", 35, 10, 10, 10),
                PdfItem("a", 50, 10, 5, 10),
                PdfItem("wide", 60, 10, 22, 10),
                PdfItem("flowing", 88, 10, 40, 10),
                PdfItem("line", 134, 10, 20, 10),
                PdfItem("Another", 10, 28, 38, 10),
                PdfItem("wide", 54, 28, 22, 10),
                PdfItem("paragraph", 82, 28, 52, 10),
                PdfItem("line", 140, 28, 20, 10),
                PdfItem("Final", 10, 46, 25, 10),
                PdfItem("wide", 41, 46, 22, 10),
                PdfItem("flowing", 69, 46, 40, 10),
                PdfItem("line", 115, 46, 20, 10)
            ]
        });

        Assert.True(GetMetric<int>(result, "flowBlockCount") >= 1);
        Assert.Contains("This is a wide flowing line", result.Text, StringComparison.Ordinal);
        Assert.Contains("Another wide paragraph line", result.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch("This\\s{4,}is", result.Text);
    }

    [Fact]
    public void LayoutProjector_DoesNotCrashWhenFlowingLinesAreSeparatedByInsertedBlankLine()
    {
        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(240, 220),
            TextItems =
            [
                PdfItem("Alpha", 10, 10, 30, 10),
                PdfItem("flow", 46, 10, 24, 10),
                PdfItem("line", 76, 10, 22, 10),
                PdfItem("spans", 104, 10, 32, 10),
                PdfItem("wide", 142, 10, 28, 10),
                PdfItem("Beta", 10, 120, 28, 10),
                PdfItem("flow", 44, 120, 24, 10),
                PdfItem("line", 74, 120, 22, 10),
                PdfItem("also", 102, 120, 26, 10),
                PdfItem("wide", 134, 120, 28, 10)
            ]
        });

        Assert.Contains("Alpha flow line spans wide", result.Text, StringComparison.Ordinal);
        Assert.Contains("Beta flow line also wide", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutProjector_PreservesRotatedSideLabelsWithoutPollutingBodyFlow()
    {
        var result = PdfLayoutProjector.Project(new PdfLayoutProjectionInput
        {
            PageNumber = 1,
            PageSize = new PageSize(220, 180),
            TextItems =
            [
                PdfItem("SIDE", 8, 18, 10, 35, 90),
                PdfItem("Body", 50, 18, 24, 10),
                PdfItem("row", 80, 18, 18, 10),
                PdfItem("one", 104, 18, 18, 10),
                PdfItem("Body", 50, 36, 24, 10),
                PdfItem("row", 80, 36, 18, 10),
                PdfItem("two", 104, 36, 18, 10),
                PdfItem("Body", 50, 54, 24, 10),
                PdfItem("row", 80, 54, 18, 10),
                PdfItem("three", 104, 54, 28, 10)
            ]
        });

        Assert.True(GetMetric<int>(result, "rotatedItemCount") >= 1);
        Assert.Contains(result.ProjectedItems, static item => item.Text == "SIDE" && (bool)item.Metadata["projectionRotated"]!);
        Assert.Matches("Body\\s+row\\s+one", result.Text);
        Assert.Matches("Body\\s+row\\s+two", result.Text);
        Assert.DoesNotMatch("SIDE[^\\r\\n]*Body", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_JoinsMultiplePagesWithBlankLine()
    {
        var input = ContentInput.FromBytes(CreatePdfFixture("page one text", "page two text"), "joined.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(input, new PdfExtractionOptions { OcrEnabled = false });

        Assert.Equal(2, result.Pages.Count);
        Assert.Contains("page one text", result.Text, StringComparison.Ordinal);
        Assert.Contains("page two text", result.Text, StringComparison.Ordinal);
        Assert.Contains("\n\n", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_InvalidPdfBytesSurfaceParserError()
    {
        var input = ContentInput.FromBytes("not a pdf"u8.ToArray(), "broken.pdf", MimeTypes.Pdf);
        var engine = new PdfExtractionEngine();

        var exception = await Assert.ThrowsAsync<PdfBackendException>(() =>
            engine.ExtractAsync(input, new PdfExtractionOptions()).AsTask());

        Assert.Equal(PdfBackendFailureKind.InvalidFormat, exception.Kind);
        Assert.Equal(3, exception.BackendErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_EncryptedFixtureRequiresPassword()
    {
        var path = PdfCorpusPath("security", "encrypted-password-is-password.pdf");
        Assert.True(File.Exists(path), $"Missing PDF corpus fixture: {path}");
        var engine = new PdfExtractionEngine();

        var exception = await Assert.ThrowsAsync<PdfBackendException>(() =>
            engine.ExtractAsync(ContentInput.FromPath(path, MimeTypes.Pdf), new PdfExtractionOptions { OcrEnabled = false }).AsTask());

        Assert.Equal(PdfBackendFailureKind.PasswordRequired, exception.Kind);
        Assert.Equal(4, exception.BackendErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_EncryptedFixtureOpensWithConfiguredPassword()
    {
        var path = PdfCorpusPath("security", "encrypted-password-is-password.pdf");
        Assert.True(File.Exists(path), $"Missing PDF corpus fixture: {path}");
        var engine = new PdfExtractionEngine();

        var result = await engine.ExtractAsync(
            ContentInput.FromPath(path, MimeTypes.Pdf),
            new PdfExtractionOptions
            {
                OcrEnabled = false,
                IncludeTextItems = true,
                Password = "password"
            });

        Assert.NotEmpty(result.Pages);
        Assert.Contains(result.Diagnostics.Timings, static timing => timing.Name == "pdf.total");
        Assert.True(result.Text.Length > 0);
        Assert.True(result.Pages.Sum(static page => page.TextItems.Count) > 0);
    }

    [Theory]
    [InlineData("circular-reference-issue-1122.pdf")]
    [InlineData("stack-depth-error.pdf")]
    public async Task ExtractAsync_MalformedSecurityFixturesFailWithClassifiedInvalidFormat(string fileName)
    {
        var path = PdfCorpusPath("security", fileName);
        Assert.True(File.Exists(path), $"Missing PDF corpus fixture: {path}");
        var engine = new PdfExtractionEngine();

        var exception = await Assert.ThrowsAsync<PdfBackendException>(() =>
            engine.ExtractAsync(ContentInput.FromPath(path, MimeTypes.Pdf), new PdfExtractionOptions { OcrEnabled = false }).AsTask());

        Assert.Equal(PdfBackendFailureKind.InvalidFormat, exception.Kind);
        Assert.Equal(3, exception.BackendErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_RecordsWarningAndContinuesWhenPageFailsToLoad()
    {
        var failedPage = new FailedPdfPageHandle(
            2,
            new PdfBackendException(PdfBackendFailureKind.PageLoad, "page two failed", backendErrorCode: 6));
        var engine = new PdfExtractionEngine(
            backend: new FakePdfBackend
            {
                Pages =
                [
                    new FakePdfPage { Number = 1 },
                    failedPage
                ]
            },
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new DefaultPdfLayoutProjector());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "partial.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = false });

        Assert.Equal(2, result.Pages.Count);
        Assert.Contains("fake", result.Text, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics.Warnings, static warning => warning.Contains("page two failed", StringComparison.Ordinal));
        var page = result.Pages[1];
        Assert.Equal(2, page.Number);
        Assert.Empty(page.Text);
        Assert.Equal(true, page.Metadata["pageLoadFailed"]);
        Assert.Equal(PdfBackendFailureKind.PageLoad.ToString(), page.Metadata["failureKind"]);
    }

    [Fact]
    public async Task ExtractAsync_UsesInjectedPipelineStages()
    {
        var engine = new PdfExtractionEngine(
            backend: new FakePdfBackend(),
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new FakeLayoutProjector());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = false });

        var page = Assert.Single(result.Pages);
        Assert.Equal("projected fake text", page.Text);
        Assert.Equal(12, page.Number);
        Assert.Equal(90, page.Metadata["rotation"]);
        Assert.Single(page.TextItems);
        Assert.Equal(1, page.Metadata["imageRegionCount"]);
        Assert.Equal("projected fake text", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_UsesInjectedOcrStagesAndRecordsExecution()
    {
        var backend = new FakePdfBackend
        {
            Page =
            {
                RenderedPage = FakeRenderedPage()
            }
        };
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new DefaultPdfLayoutProjector(),
            ocrExecutor: new FakeOcrExecutor(),
            ocrTextMerger: new DefaultPdfOcrTextMerger());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = true });

        var page = Assert.Single(result.Pages);
        Assert.True(result.Diagnostics.OcrPlanned);
        Assert.True(result.Diagnostics.OcrRendered);
        Assert.True(result.Diagnostics.OcrAttempted);
        Assert.True(result.Diagnostics.OcrSucceeded);
        Assert.True(result.Diagnostics.OcrUsed);
        Assert.Equal(1, result.Diagnostics.OcrCandidatePageCount);
        Assert.Equal(1, result.Diagnostics.OcrRenderedPageCount);
        Assert.Equal(1, result.Diagnostics.OcrAttemptedPageCount);
        Assert.Equal(1, result.Diagnostics.OcrSucceededPageCount);
        Assert.Equal(1, result.Diagnostics.OcrUsedPageCount);
        Assert.Contains(page.TextItems, item => item.Layer == PdfTextLayerKind.Ocr && item.Text == "ocr recovered text");
        Assert.Contains("ocr recovered text", result.Text, StringComparison.Ordinal);
        Assert.Equal(1, page.Metadata["ocrResultRegionCount"]);
        Assert.Equal(1, backend.Page.RenderRequests.Count);
        Assert.Equal(PdfRenderPurpose.Ocr, backend.Page.RenderRequests[0].Purpose);
        Assert.Equal(PdfRenderImageFormat.Bmp, backend.Page.RenderRequests[0].Format);
    }

    [Fact]
    public async Task ExtractAsync_FailsWhenAllStrictOcrAttemptsFail()
    {
        var backend = new FakePdfBackend
        {
            Page =
            {
                RenderedPage = FakeRenderedPage()
            }
        };
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new DefaultPdfLayoutProjector(),
            ocrExecutor: new FailingOcrExecutor(),
            ocrTextMerger: new DefaultPdfOcrTextMerger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExtractAsync(
                ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
                new PdfExtractionOptions { OcrEnabled = true }).AsTask());

        Assert.Contains("OCR failed for all 1 strict PDF page", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing traineddata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_DoesNotFailWhenBestEffortOcrAttemptFails()
    {
        var backend = new FakePdfBackend
        {
            Page =
            {
                NativeTextItems = DenseNativeTextItems(),
                ImageRegions = RelevantImageRegions(),
                RenderedPage = FakeRenderedPage()
            }
        };
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new DefaultPdfLayoutProjector(),
            ocrExecutor: new FailingOcrExecutor(),
            ocrTextMerger: new DefaultPdfOcrTextMerger());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = true });

        var page = Assert.Single(result.Pages);
        Assert.Equal(PdfOcrFailurePolicy.BestEffortEnrichment, page.OcrDecision.FailurePolicy);
        Assert.Contains(PdfOcrDecisionReason.EmbeddedImages, page.OcrDecision.Reasons);
        Assert.Contains("dense native text survives failed best effort OCR", result.Text, StringComparison.Ordinal);
        Assert.True(result.Diagnostics.OcrPlanned);
        Assert.True(result.Diagnostics.OcrRendered);
        Assert.True(result.Diagnostics.OcrAttempted);
        Assert.False(result.Diagnostics.OcrSucceeded);
        Assert.False(result.Diagnostics.OcrUsed);
        Assert.True(result.Diagnostics.OcrFailed);
        Assert.Contains(result.Diagnostics.Warnings, warning => warning.Contains("OCR failed: missing traineddata", StringComparison.Ordinal));
        Assert.Equal(1, Assert.IsType<int>(result.Diagnostics.Metrics["ocrAttemptedPageCount"]));
        Assert.Equal(1, Assert.IsType<int>(result.Diagnostics.Metrics["ocrFailedPageCount"]));
        Assert.Equal(0, Assert.IsType<int>(result.Diagnostics.Metrics["ocrStrictFailurePageCount"]));
    }

    [Fact]
    public async Task ExtractAsync_DoesNotFailWhenBestEffortOcrRenderFails()
    {
        var backend = new FakePdfBackend
        {
            Page =
            {
                NativeTextItems = DenseNativeTextItems(),
                ImageRegions = RelevantImageRegions()
            }
        };
        var engine = new PdfExtractionEngine(
            backend: backend,
            qualityAnalyzer: new DefaultPdfPageQualityAnalyzer(),
            layoutProjector: new DefaultPdfLayoutProjector());

        var result = await engine.ExtractAsync(
            ContentInput.FromBytes("unused"u8.ToArray(), "fake.pdf", MimeTypes.Pdf),
            new PdfExtractionOptions { OcrEnabled = true });

        var page = Assert.Single(result.Pages);
        Assert.Equal(PdfOcrFailurePolicy.BestEffortEnrichment, page.OcrDecision.FailurePolicy);
        Assert.Contains("dense native text survives failed best effort OCR", result.Text, StringComparison.Ordinal);
        Assert.True(result.Diagnostics.OcrPlanned);
        Assert.False(result.Diagnostics.OcrRendered);
        Assert.False(result.Diagnostics.OcrAttempted);
        Assert.False(result.Diagnostics.OcrUsed);
        Assert.True(result.Diagnostics.OcrFailed);
        Assert.Equal(1, result.Diagnostics.OcrRenderFailedPageCount);
        Assert.Equal(0, result.Diagnostics.OcrStrictFailurePageCount);
        Assert.Contains(result.Diagnostics.Warnings, warning => warning.Contains("rendering failed", StringComparison.Ordinal));
    }

    [Fact]
    public void OcrTextMerger_ConvertsRenderPixelBoxesToPdfPoints()
    {
        var merger = new DefaultPdfOcrTextMerger();
        var context = new PdfPipelineContext
        {
            Options = new PdfExtractionOptions(),
            Diagnostics = new ExtractionDiagnostics()
        };

        var result = merger.Merge(new PdfOcrMergeInput
        {
            PageNumber = 1,
            PageSize = new PageSize(200, 100),
            RenderedPage = new PdfRenderedPage
            {
                PageNumber = 1,
                Dpi = 144,
                EncodedFormat = PdfRenderImageFormat.Png,
                Image = new ImageFrame("png"u8.ToArray(), Width: 400, Height: 200, MimeTypes.ImagePng)
            },
            OcrResult = new PdfOcrPageResult
            {
                PageNumber = 1,
                CoordinateSpace = OcrCoordinateSpace.RenderPixelsTopLeft,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Text = "scaled OCR",
                        BoundingBox = new BoundingBox(20, 80, 240, 40),
                        Confidence = 0.9f
                    }
                ]
            }
        }, context);

        var item = Assert.Single(result.TextItems);
        Assert.Equal(PdfTextLayerKind.Ocr, item.Layer);
        Assert.Equal(new BoundingBox(10, 40, 120, 20), item.BoundingBox);
        Assert.Equal(1, result.Metrics["mergedOcrItemCount"]);
    }

    [Fact]
    public void OcrTextMerger_UsesRenderedPageGeometryForPixelBoxes()
    {
        var merger = new DefaultPdfOcrTextMerger();
        var context = TestPdfPipelineContext();

        var result = merger.Merge(new PdfOcrMergeInput
        {
            PageNumber = 1,
            PageSize = new PageSize(200, 100),
            RenderedPage = new PdfRenderedPage
            {
                PageNumber = 1,
                Dpi = 144,
                EncodedFormat = PdfRenderImageFormat.Png,
                Image = new ImageFrame("png"u8.ToArray(), Width: 400, Height: 200, MimeTypes.ImagePng),
                Geometry = new PdfRenderGeometry
                {
                    ViewportSize = new PageSize(300, 200),
                    Dpi = 144,
                    PixelWidth = 400,
                    PixelHeight = 200,
                    PixelToViewport = new PdfAffineTransform(0.5f, 0, 0, 0.5f, 5, 7)
                }
            },
            OcrResult = new PdfOcrPageResult
            {
                PageNumber = 1,
                CoordinateSpace = OcrCoordinateSpace.RenderPixelsTopLeft,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Text = "geometry OCR",
                        BoundingBox = new BoundingBox(20, 80, 240, 40),
                        Confidence = 0.9f
                    }
                ]
            }
        }, context);

        var item = Assert.Single(result.TextItems);
        Assert.Equal(new BoundingBox(15, 47, 120, 20), item.BoundingBox);
    }

    [Fact]
    public void OcrTextMerger_DropsCorruptNativeItemsBeforeMergingOcr()
    {
        var merger = new DefaultPdfOcrTextMerger();
        var context = TestPdfPipelineContext();

        var result = merger.Merge(new PdfOcrMergeInput
        {
            PageNumber = 1,
            PageSize = new PageSize(200, 100),
            NativeItems =
            [
                new PdfTextItem
                {
                    Text = "\uE001\uE002\uE003\uE004",
                    BoundingBox = new BoundingBox(20, 20, 80, 20),
                    Layer = PdfTextLayerKind.Native,
                    Font = new PdfFontInfo { LooksCorrupt = true },
                    HasUnicodeMapError = true
                }
            ],
            Quality = new PdfPageQuality
            {
                LooksGarbled = true,
                NativeTextLength = 4,
                CorruptNativeTextLength = 4,
                UnicodeMapErrorTextLength = 4
            },
            OcrResult = new PdfOcrPageResult
            {
                PageNumber = 1,
                CoordinateSpace = OcrCoordinateSpace.PdfPointsTopLeft,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Text = "recovered native text",
                        BoundingBox = new BoundingBox(20, 20, 80, 20),
                        Confidence = 0.95f
                    }
                ]
            }
        }, context);

        var item = Assert.Single(result.TextItems);
        Assert.Equal("recovered native text", item.Text);
        Assert.Equal(PdfTextLayerKind.Ocr, item.Layer);
        Assert.Equal(1, result.Metrics["droppedNativeItemCount"]);
        Assert.Equal(1, result.Metrics["mergedOcrItemCount"]);
    }

    [Fact]
    public void OcrTextMerger_SkipsOcrThatOverlapsCleanNativeText()
    {
        var merger = new DefaultPdfOcrTextMerger();
        var context = TestPdfPipelineContext();

        var result = merger.Merge(new PdfOcrMergeInput
        {
            PageNumber = 1,
            PageSize = new PageSize(200, 100),
            NativeItems =
            [
                new PdfTextItem
                {
                    Text = "clean native text",
                    BoundingBox = new BoundingBox(10, 10, 90, 20),
                    Layer = PdfTextLayerKind.Native
                }
            ],
            Quality = new PdfPageQuality(),
            OcrResult = new PdfOcrPageResult
            {
                PageNumber = 1,
                CoordinateSpace = OcrCoordinateSpace.PdfPointsTopLeft,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Text = "duplicate OCR",
                        BoundingBox = new BoundingBox(12, 12, 40, 12),
                        Confidence = 0.9f
                    },
                    new OcrTextRegion
                    {
                        Text = "new OCR",
                        BoundingBox = new BoundingBox(120, 60, 40, 12),
                        Confidence = 0.9f
                    }
                ]
            }
        }, context);

        Assert.Equal(2, result.TextItems.Count);
        Assert.Contains(result.TextItems, static item => item.Text == "clean native text" && item.Layer == PdfTextLayerKind.Native);
        Assert.Contains(result.TextItems, static item => item.Text == "new OCR" && item.Layer == PdfTextLayerKind.Ocr);
        Assert.Equal(1, result.Metrics["skippedOverlappingOcrItemCount"]);
        Assert.Equal(1, result.Metrics["mergedOcrItemCount"]);
    }

    [Fact]
    public void OcrTextMerger_CleansNumericTableBorderArtifacts()
    {
        var merger = new DefaultPdfOcrTextMerger();
        var context = TestPdfPipelineContext();

        var result = merger.Merge(new PdfOcrMergeInput
        {
            PageNumber = 1,
            PageSize = new PageSize(200, 100),
            Quality = new PdfPageQuality(),
            OcrResult = new PdfOcrPageResult
            {
                PageNumber = 1,
                CoordinateSpace = OcrCoordinateSpace.PdfPointsTopLeft,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Text = "44520]",
                        BoundingBox = new BoundingBox(10, 10, 30, 10),
                        Confidence = 0.9f
                    },
                    new OcrTextRegion
                    {
                        Text = "|hello|",
                        BoundingBox = new BoundingBox(60, 10, 40, 10),
                        Confidence = 0.9f
                    }
                ]
            }
        }, context);

        Assert.Contains(result.TextItems, static item => item.Text == "44520");
        Assert.Contains(result.TextItems, static item => item.Text == "|hello|");
        Assert.Equal(1, result.Metrics["cleanedOcrItemCount"]);
        Assert.Equal(2, result.Metrics["mergedOcrItemCount"]);
    }

    [Fact]
    public async Task HttpPdfOcrExecutor_PostsRenderedImageAndMapsRegions()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var client = new HttpClient(new FakeHttpMessageHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"results":[{"text":"http OCR","bbox":[10,20,70,50],"confidence":0.87}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }));
        var executor = new HttpPdfOcrExecutor(client);

        var result = await executor.RecognizeAsync(
            TestRenderedPage(PdfRenderImageFormat.Png),
            TestPdfPipelineContext(new PdfExtractionOptions
            {
                OcrEndpoint = new Uri("https://ocr.example.test/ocr"),
                OcrLanguage = "fra"
            }));

        Assert.True(result.Succeeded);
        Assert.Equal(OcrCoordinateSpace.RenderPixelsTopLeft, result.CoordinateSpace);
        var region = Assert.Single(result.Regions);
        Assert.Equal("http OCR", region.Text);
        Assert.Equal(new BoundingBox(10, 20, 60, 30), region.BoundingBox);
        Assert.Equal(0.87f, region.Confidence);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://ocr.example.test/ocr", capturedRequest.RequestUri!.ToString());
        Assert.Contains("fra", capturedBody, StringComparison.Ordinal);
        Assert.Contains("page-3.png", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpPdfOcrExecutor_ReturnsErrorForHttpFailure()
    {
        using var client = new HttpClient(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("bad gateway")
            })));
        var executor = new HttpPdfOcrExecutor(client);

        var result = await executor.RecognizeAsync(
            TestRenderedPage(PdfRenderImageFormat.Png),
            TestPdfPipelineContext(new PdfExtractionOptions
            {
                OcrEndpoint = new Uri("https://ocr.example.test/ocr")
            }));

        Assert.False(result.Succeeded);
        Assert.IsType<HttpRequestException>(result.Error);
    }

    [Fact]
    public async Task HttpPdfOcrExecutor_ReturnsErrorForMalformedJson()
    {
        using var client = new HttpClient(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not json", Encoding.UTF8, "application/json")
            })));
        var executor = new HttpPdfOcrExecutor(client);

        var result = await executor.RecognizeAsync(
            TestRenderedPage(PdfRenderImageFormat.Png),
            TestPdfPipelineContext(new PdfExtractionOptions
            {
                OcrEndpoint = new Uri("https://ocr.example.test/ocr")
            }));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task PdfDecoder_ReturnsRichPdfResultAndGeneralPageItems()
    {
        var decoder = new PdfDecoder();
        var input = ContentInput.FromBytes(CreatePdfFixture("decoder rich result"), "decoder.pdf", MimeTypes.Pdf);

        var result = await decoder.DecodeAsync(input, new ExtractionOptions { OcrEnabled = false });

        var page = Assert.Single(result.Pages);
        Assert.NotEmpty(page.TextItems);
        var rich = Assert.IsType<PdfExtractionResult>(result.RichResult);
        Assert.Single(rich.Pages);
        Assert.Contains("decoder rich result", result.Content.Sections[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PdfDecoder_ProjectsQualityAndOcrDecisionIntoPageMetadata()
    {
        var decoder = new PdfDecoder();
        var input = ContentInput.FromBytes(CreatePdfFixture("metadata"), "metadata.pdf", MimeTypes.Pdf);

        var result = await decoder.DecodeAsync(input, new ExtractionOptions { OcrEnabled = false });

        var page = Assert.Single(result.Pages);
        Assert.True(page.Metadata.ContainsKey("quality"));
        Assert.True(page.Metadata.ContainsKey("ocrDecision"));
        Assert.IsType<PdfPageQuality>(page.Metadata["quality"]);
        Assert.IsType<PdfOcrDecision>(page.Metadata["ocrDecision"]);
    }

    [Fact]
    public void PdfExtractionOptions_MapsFromGeneralExtractionOptions()
    {
        var options = new ExtractionOptions
        {
            Profile = ExtractionProfile.Deep,
            MaxPages = 7,
            TargetPages = "1,3",
            Password = "secret",
            IncludeTextItems = false,
            IncludeScreenshots = true,
            IncludeEmbeddedImages = true,
            OcrEnabled = false,
            OcrLanguage = "fra",
            Dpi = 220
        };

        var pdfOptions = PdfExtractionOptions.FromExtractionOptions(options);

        Assert.Equal(ExtractionProfile.Deep, pdfOptions.Profile);
        Assert.Equal(7, pdfOptions.MaxPages);
        Assert.Equal("1,3", pdfOptions.TargetPages);
        Assert.Equal("secret", pdfOptions.Password);
        Assert.False(pdfOptions.IncludeTextItems);
        Assert.True(pdfOptions.IncludeScreenshots);
        Assert.Equal(PdfRenderImageFormat.Png, pdfOptions.ScreenshotFormat);
        Assert.True(pdfOptions.IncludeEmbeddedImages);
        Assert.False(pdfOptions.OcrEnabled);
        Assert.Equal("fra", pdfOptions.OcrLanguage);
        Assert.Equal(220, pdfOptions.Dpi);
    }

    [Fact]
    public void PageSelector_ParsesRangesAndRejectsReverseRanges()
    {
        var pages = PdfPageSelector.Parse(" 1, 3-5, 5 ");

        Assert.NotNull(pages);
        Assert.Contains(1, pages);
        Assert.Contains(3, pages);
        Assert.Contains(4, pages);
        Assert.Contains(5, pages);
        Assert.Equal(4, pages.Count);
        Assert.Throws<ArgumentException>(() => PdfPageSelector.Parse("5-3"));
    }

    [Fact]
    public void PageSelector_HandlesNullSinglePageWhitespaceAndInvalidTokens()
    {
        Assert.Null(PdfPageSelector.Parse(null));
        Assert.Null(PdfPageSelector.Parse("   "));

        var single = PdfPageSelector.Parse("2-2");
        Assert.NotNull(single);
        Assert.Single(single);
        Assert.Contains(2, single);

        Assert.Throws<FormatException>(() => PdfPageSelector.Parse("abc"));
    }

    private static PdfTextItem PdfItem(string text, float x, float y, float width, float height, float rotation = 0) =>
        new()
        {
            Text = text,
            BoundingBox = new BoundingBox(x, y, width, height),
            Rotation = rotation
        };

    private static PdfFontInfo TestFont(float size) => new() { Size = size };

    private static T GetMetric<T>(PdfLayoutProjectionResult result, string key)
    {
        Assert.True(result.Metrics.TryGetValue(key, out var value), $"Missing projection metric '{key}'.");
        return Assert.IsType<T>(value);
    }

    private static PdfPipelineContext TestPdfPipelineContext(PdfExtractionOptions? options = null) => new()
    {
        Options = options ?? new PdfExtractionOptions(),
        Diagnostics = new ExtractionDiagnostics()
    };

    private static int InvokeCanonicalRotation(float rotation)
    {
        var method = typeof(PdfLayoutProjector).GetMethod(
            "CanonicalRotation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<int>(method.Invoke(null, [rotation]));
    }

    private static void AssertClose(float expected, float actual, float tolerance = 0.01f) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected} +/- {tolerance}, actual {actual}.");

    private static byte[] CreatePdfFixture(params string[] pageTexts)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>"
        };

        var pageObjectIds = new List<int>();
        var nextObjectId = 3;
        for (var i = 0; i < pageTexts.Length; i++)
        {
            var pageObjectId = nextObjectId++;
            var contentObjectId = nextObjectId++;
            pageObjectIds.Add(pageObjectId);

            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 FONT_OBJECT_ID 0 R >> >> /Contents {contentObjectId} 0 R >>");

            var content = $"BT /F1 24 Tf 72 720 Td ({EscapePdfText(pageTexts[i])}) Tj ET";
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }

        objects.Insert(1, $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pageTexts.Length} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        var fontObjectId = objects.Count;
        for (var i = 0; i < objects.Count; i++)
        {
            objects[i] = objects[i].Replace("FONT_OBJECT_ID", fontObjectId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n");
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreateImagePdfFixture()
    {
        var imageData = "abcdefghijkl"u8.ToArray();
        var content = "q 40 0 0 30 50 20 cm /Im1 Do Q";
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R >>"),
            Ascii($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"),
            Concat(
                Ascii($"<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {imageData.Length} >>\nstream\n"),
                imageData,
                Ascii("\nendstream"))
        };

        return BuildPdf(objects);
    }

    private static byte[] CreateStyledTextPdfFixture()
    {
        var content = "BT /F1 18 Tf 1 0 0 rg 40 120 Td (styled sentinel) Tj ET";
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Ascii($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
        };

        return BuildPdf(objects);
    }

    private static byte[] CreateBadToUnicodePdfFixture()
    {
        var text = string.Concat(Enumerable.Repeat("ABCD", 6));
        var content = $"BT /F1 18 Tf 40 120 Td ({text}) Tj ET";
        var cmap = """
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
            /CMapName /BrokenUnicode def
            /CMapType 2 def
            1 begincodespacerange
            <00> <FF>
            endcodespacerange
            4 beginbfchar
            <41> <E001>
            <42> <E002>
            <43> <E003>
            <44> <E004>
            endbfchar
            endcmap
            CMapName currentdict /CMap defineresource pop
            end
            end
            """;
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Ascii($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /ToUnicode 6 0 R >>"),
            Ascii($"<< /Length {Encoding.ASCII.GetByteCount(cmap)} >>\nstream\n{cmap}\nendstream")
        };

        return BuildPdf(objects);
    }

    private static byte[] CreateMarkedContentTextPdfFixture()
    {
        var content = "/Span << /MCID 7 >> BDC BT /F1 16 Tf 0 0 1 rg 1 0 0 RG 1 Tr 40 150 Td (mcid stroke sentinel) Tj ET EMC";
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Resources << /Font << /F1 5 0 R >> >> /StructParents 0 /Contents 4 0 R >>"),
            Ascii($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
        };

        return BuildPdf(objects);
    }

    private static byte[] BuildPdf(IReadOnlyList<byte[]> objects)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{i + 1} 0 obj\n");
            stream.Write(objects[i]);
            WriteAscii(stream, "\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(stream, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return stream.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(static part => part.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result.AsSpan(offset));
            offset += part.Length;
        }

        return result;
    }

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));

    private static string EscapePdfText(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static IEnumerable<LocalPdfFixtureCorpusCase> LocalPdfFixtureCorpusCases()
    {
        return
        [
            new LocalPdfFixtureCorpusCase(
                Name: "native.hello_world",
                Path: PdfCorpusPath("native-capabilities", "hello_world.pdf"),
                RequiredText: "Hello, world",
                MinTextLength: 5,
                MaxTextLength: 200,
                MinTextItemCount: 1,
                MinFontInfoItemCount: 1,
                MinRenderModeItemCount: 1,
                MinColorItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.rotated_text",
                Path: PdfCorpusPath("native-capabilities", "rotated_text.pdf"),
                RequiredText: null,
                MinTextLength: 5,
                MaxTextLength: 1000,
                MinTextItemCount: 1,
                MinRotatedItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.rotated_text_90",
                Path: PdfCorpusPath("native-capabilities", "rotated_text_90.pdf"),
                RequiredText: null,
                MinTextLength: 5,
                MaxTextLength: 1000,
                MinTextItemCount: 1,
                MinRotatedItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.vertical_text",
                Path: PdfCorpusPath("native-capabilities", "vertical_text.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 1000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.text_render_mode",
                Path: PdfCorpusPath("native-capabilities", "text_render_mode.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 2000,
                MinTextItemCount: 1,
                MinRenderModeItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.text_font",
                Path: PdfCorpusPath("native-capabilities", "text_font.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 2000,
                MinTextItemCount: 1,
                MinFontInfoItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.font_weight",
                Path: PdfCorpusPath("native-capabilities", "font_weight.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 2000,
                MinTextItemCount: 1,
                MinFontInfoItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.marked_content_id",
                Path: PdfCorpusPath("native-capabilities", "marked_content_id.pdf"),
                RequiredText: null,
                MinTextLength: 0,
                MaxTextLength: 2000,
                MinTextItemCount: 0,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "native.text_in_page_marked",
                Path: PdfCorpusPath("native-capabilities", "text_in_page_marked.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 2000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.embedded_images",
                Path: PdfCorpusPath("native-capabilities", "embedded_images.pdf"),
                RequiredText: null,
                MinTextLength: 0,
                MaxTextLength: 1000,
                MinTextItemCount: 0,
                MinAssetCount: 1,
                MinImageRegionCount: 1,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "native.rotated_image",
                Path: PdfCorpusPath("native-capabilities", "rotated_image.pdf"),
                RequiredText: null,
                MinTextLength: 0,
                MaxTextLength: 1000,
                MinTextItemCount: 0,
                MinImageRegionCount: 1,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "native.shared_form_xobject_matrix",
                Path: PdfCorpusPath("native-capabilities", "shared_form_xobject_matrix.pdf"),
                RequiredText: null,
                MinTextLength: 0,
                MaxTextLength: 3000,
                MinTextItemCount: 0,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "native.bigtable_mini",
                Path: PdfCorpusPath("native-capabilities", "bigtable_mini.pdf"),
                RequiredText: null,
                MinTextLength: 50,
                MaxTextLength: 20000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "native.cropped_text",
                Path: PdfCorpusPath("native-capabilities", "cropped_text.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 1000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.utf8",
                Path: PdfCorpusPath("native-capabilities", "utf-8.pdf"),
                RequiredText: null,
                MinTextLength: 0,
                MaxTextLength: 3000,
                MinTextItemCount: 0,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "native.form_object_with_text",
                Path: PdfCorpusPath("native-capabilities", "form_object_with_text.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 3000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "native.hello_world_screenshot",
                Path: PdfCorpusPath("native-capabilities", "hello_world.pdf"),
                RequiredText: "Hello, world",
                MinTextLength: 5,
                MaxTextLength: 200,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1,
                MinAssetCount: 1,
                IncludeScreenshot: true),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.cropped_and_rotated",
                Path: PdfCorpusPath("layout-stress", "cropped-and-rotated.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 5000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.single_page_90_rotation",
                Path: PdfCorpusPath("layout-stress", "single-page-90-rotation.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 5000,
                MinTextItemCount: 1,
                ExpectedRotation: 90,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.single_page_180_rotation",
                Path: PdfCorpusPath("layout-stress", "single-page-180-rotation.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 5000,
                MinTextItemCount: 1,
                ExpectedRotation: 180,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.single_page_270_rotation",
                Path: PdfCorpusPath("layout-stress", "single-page-270-rotation.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 5000,
                MinTextItemCount: 1,
                ExpectedRotation: 270,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.two_columns_hyphenated",
                Path: PdfCorpusPath("layout-stress", "random-2-columns-lists-hyph-justified.pdf"),
                RequiredText: null,
                MinTextLength: 100,
                MaxTextLength: 20000,
                MinTextItemCount: 5,
                MinProjectionLineCount: 5),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.cmap_parsing_exception",
                Path: PdfCorpusPath("layout-stress", "cmap-parsing-exception.pdf"),
                RequiredText: null,
                MinTextLength: 0,
                MaxTextLength: 10000,
                MinTextItemCount: 0,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.type0_cjk_font",
                Path: PdfCorpusPath("layout-stress", "type0-cjk-font.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 10000,
                MinTextItemCount: 1,
                MinFontInfoItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.grapheme_clusters_emoji",
                Path: PdfCorpusPath("layout-stress", "grapheme-clusters-emoji.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 10000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1),
            new LocalPdfFixtureCorpusCase(
                Name: "layout.single_page_images",
                Path: PdfCorpusPath("layout-stress", "single-page-images.pdf"),
                RequiredText: null,
                MinTextLength: 0,
                MaxTextLength: 5000,
                MinTextItemCount: 0,
                MinImageRegionCount: 1,
                MinProjectionLineCount: 0),
            new LocalPdfFixtureCorpusCase(
                Name: "sample.general_document",
                Path: PdfCorpusPath("document-samples", "sample.pdf"),
                RequiredText: null,
                MinTextLength: 1,
                MaxTextLength: 5000,
                MinTextItemCount: 1,
                MinProjectionLineCount: 1)
        ];
    }

    private static string PdfCorpusPath(string fixtureSet, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Content", "Fixtures", "PdfCorpus", fixtureSet, fileName);

    private static string? GetBaselinePassword(PdfCorpusBaselineEntry entry)
    {
        if (!string.Equals(entry.AccessMode, "password", StringComparison.OrdinalIgnoreCase))
            return null;

        return KnownCorpusPassword(entry.Path)
            ?? throw new InvalidOperationException($"Baseline entry requires password access but no known password is configured: {entry.Path}");
    }

    private static string? KnownCorpusPassword(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var exactPasswords = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/encrypted-password-is-password.pdf"] = "password",
            ["pdfium/testing/resources/bug_1124998.pdf"] = "test",
            ["pdfium/testing/resources/bug_644.pdf"] = "a",
            ["pdfium/testing/resources/encrypted.pdf"] = "1234",
            ["pdfium/testing/resources/encrypted_hello_world_r2.pdf"] = "h\u00f4tel",
            ["pdfium/testing/resources/encrypted_hello_world_r3.pdf"] = "h\u00f4tel",
            ["pdfium/testing/resources/encrypted_hello_world_r5.pdf"] = "h\u00f4tel",
            ["pdfium/testing/resources/encrypted_hello_world_r6.pdf"] = "h\u00f4tel"
        };

        if (exactPasswords.TryGetValue(normalized, out var password))
            return password;

        return string.Equals(Path.GetFileName(normalized), "encrypted-password-is-password.pdf", StringComparison.Ordinal)
            ? "password"
            : null;
    }

    private static int GetOptionalCorpusInt32(string name, int defaultValue)
    {
        var value = global::System.Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : defaultValue;
    }

    private static string ResolveOptionalCorpusRoot(string root)
    {
        if (Path.IsPathRooted(root) || Directory.Exists(root))
        {
            return root;
        }

        foreach (var basePath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(basePath);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, root);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return root;
    }

    private static string ResolveOptionalCorpusFile(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path))
        {
            return path;
        }

        foreach (var basePath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(basePath);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, path);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return path;
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static int GetMetricAsInt(IReadOnlyDictionary<string, object?> metrics, string key, string context)
    {
        Assert.True(metrics.TryGetValue(key, out var value), $"{context}: missing metric '{key}'.");
        return value switch
        {
            int intValue => intValue,
            long longValue => checked((int)longValue),
            float floatValue => (int)floatValue,
            double doubleValue => (int)doubleValue,
            _ => throw new Xunit.Sdk.XunitException($"{context}: metric '{key}' was not numeric: {value}")
        };
    }

    private static int GetProjectionLineCountOrZero(PdfPage page)
    {
        var projection = Assert.IsType<Dictionary<string, object?>>(page.Metadata["projection"]);
        return projection.TryGetValue("lineCount", out var _)
            ? GetMetricAsInt(projection, "lineCount", $"page {page.Number}")
            : 0;
    }

    private static void AssertRange(string path, string metric, PdfCorpusBaselineRange range, int actual) =>
        Assert.True(
            actual >= range.Min && actual <= range.Max,
            $"{path}: {metric} expected in [{range.Min}, {range.Max}], actual {actual}.");

    private static int CanonicalTestRotation(float rotation)
    {
        var value = (int)MathF.Round(rotation) % 360;
        return value < 0 ? value + 360 : value;
    }

    private sealed record LocalPdfFixtureCorpusCase(
        string Name,
        string Path,
        string? RequiredText,
        int MinTextLength,
        int MaxTextLength,
        int MinTextItemCount,
        int MinProjectionLineCount,
        int MinAssetCount = 0,
        int MinImageRegionCount = 0,
        int MinRotatedItemCount = 0,
        int MinFontInfoItemCount = 0,
        int MinRenderModeItemCount = 0,
        int MinColorItemCount = 0,
        int MinUnicodeMapErrorItemCount = 0,
        int MinMarkedContentItemCount = 0,
        int? ExpectedRotation = null,
        bool? ExpectedNeedsOcr = null,
        bool IncludeScreenshot = false,
        int MaxWarningCount = 0,
        int ExpectedOcrCandidatePageCount = 0);

    private sealed class PdfCorpusBaselineManifest
    {
        public int FormatVersion { get; init; }
        public int MaxPages { get; init; }
        public IReadOnlyList<PdfCorpusBaselineEntry> Entries { get; init; } = [];
    }

    private sealed class PdfCorpusBaselineEntry
    {
        public string Path { get; init; } = string.Empty;
        public string Status { get; init; } = "ok";
        public PdfCorpusBaselineRange PageCount { get; init; }
        public PdfCorpusBaselineRange TextLength { get; init; }
        public PdfCorpusBaselineRange TextItemCount { get; init; }
        public PdfCorpusBaselineRange AssetCount { get; init; }
        public PdfCorpusBaselineRange WarningCount { get; init; }
        public PdfCorpusBaselineRange OcrCandidatePageCount { get; init; }
        public PdfCorpusBaselineRange ProjectionLineCount { get; init; }
        public string? AccessMode { get; init; }
        public string? FailureType { get; init; }
        public string? FailureCategory { get; init; }
        public string? ReferenceExpectation { get; init; }
    }

    private readonly record struct PdfCorpusBaselineRange(int Min, int Max);

    private static PdfRenderedPage FakeRenderedPage() =>
        new()
        {
            PageNumber = 12,
            Dpi = 150,
            EncodedFormat = PdfRenderImageFormat.Bmp,
            Image = new ImageFrame("fake-render"u8.ToArray(), Width: 200, Height: 100, MimeTypes.ImageBmp),
            Metadata =
            {
                ["backend"] = "fake"
            }
        };

    private static PdfRenderedPage TestRenderedPage(PdfRenderImageFormat format) =>
        new()
        {
            PageNumber = 3,
            Dpi = 144,
            EncodedFormat = format,
            Image = new ImageFrame("rendered-image"u8.ToArray(), Width: 20, Height: 10, RenderedImageMimeType(format)),
            Metadata =
            {
                ["backend"] = "test"
            }
        };

    private static string RenderedImageMimeType(PdfRenderImageFormat format) => format switch
    {
        PdfRenderImageFormat.Bmp => MimeTypes.ImageBmp,
        PdfRenderImageFormat.Png => MimeTypes.ImagePng,
        PdfRenderImageFormat.Jpeg => MimeTypes.ImageJpeg,
        _ => "application/octet-stream"
    };

    private static IReadOnlyList<PdfTextItem> DenseNativeTextItems() =>
    [
        new PdfTextItem
        {
            Text = "dense native text survives failed best effort OCR",
            BoundingBox = new BoundingBox(0, 0, 150, 80),
            Layer = PdfTextLayerKind.Native
        }
    ];

    private static IReadOnlyList<PdfImageRegion> RelevantImageRegions() =>
    [
        new PdfImageRegion
        {
            BoundingBox = new BoundingBox(0, 0, 150, 80),
            PageCoverage = 0.6f,
            IsOcrRelevant = true
        }
    ];

    private sealed class FakePdfBackend : IPdfBackend
    {
        public FakePdfPage Page { get; init; } = new();
        public IReadOnlyList<IPdfPageHandle>? Pages { get; init; }

        public ValueTask<IPdfDocumentHandle> OpenAsync(
            ContentInput input,
            PdfExtractionOptions options,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IPdfDocumentHandle>(new FakePdfDocument(Pages ?? [Page]));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _send(request);
    }

    private sealed class FakePdfDocument : IPdfDocumentHandle
    {
        private readonly IReadOnlyList<IPdfPageHandle> _pages;

        public FakePdfDocument(IReadOnlyList<IPdfPageHandle> pages)
        {
            _pages = pages;
        }

        public PdfBackendCapabilities Capabilities { get; } = new()
        {
            CanExtractNativeText = true,
            CanExtractGlyphBounds = true,
            CanExtractImageRegions = true,
            CanExtractEmbeddedImages = true,
            CanRenderPages = true
        };

        public IEnumerable<IPdfPageHandle> GetPages()
        {
            foreach (var page in _pages)
            {
                yield return page;
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakePdfPage : IPdfPageHandle
    {
        public int Number { get; init; } = 12;
        public PageSize Size { get; init; } = new(200, 100);
        public int Rotation { get; init; } = 90;
        public int AssetExtractionCalls { get; private set; }
        public IReadOnlyList<PdfTextItem> NativeTextItems { get; set; } =
        [
            new PdfTextItem
            {
                Text = "fake",
                BoundingBox = new BoundingBox(0, 0, 100, 20),
                Layer = PdfTextLayerKind.Native
            }
        ];
        public IReadOnlyList<PdfImageRegion> ImageRegions { get; set; } =
        [
            new PdfImageRegion
            {
                BoundingBox = new BoundingBox(0, 0, 10, 10),
                PageCoverage = 0.01f,
                IsOcrRelevant = false
            }
        ];
        public IReadOnlyList<ExtractedAsset> Assets { get; set; } =
        [
            new ExtractedAsset
            {
                Kind = ExtractedAssetKind.EmbeddedImage,
                Name = "fake-image",
                PageNumber = 12,
                BoundingBox = new BoundingBox(0, 0, 10, 10),
                Data = "image"u8.ToArray()
            }
        ];
        public PdfRenderedPage? RenderedPage { get; set; }
        public Exception RenderError { get; set; } = new InvalidOperationException("rendering failed");
        public List<PdfPageRenderRequest> RenderRequests { get; } = new();

        public PdfPageSnapshot ExtractSnapshot(PdfPipelineContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var assets = Array.Empty<ExtractedAsset>() as IReadOnlyList<ExtractedAsset>;
            if (context.Options.IncludeEmbeddedImages)
            {
                AssetExtractionCalls++;
                assets = Assets;
            }

            return new PdfPageSnapshot
            {
                Number = Number,
                Size = Size,
                Rotation = Rotation,
                NativeTextItems = NativeTextItems,
                ImageRegions = ImageRegions,
                Assets = assets,
                Metadata =
                {
                    ["backend"] = "fake"
                }
            };
        }

        public ValueTask<PdfRenderPageResult> RenderAsync(PdfPageRenderRequest request, PdfPipelineContext context)
        {
            RenderRequests.Add(request);
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(RenderedPage is null
                ? new PdfRenderPageResult
                {
                    PageNumber = Number,
                    Error = RenderError
                }
                : new PdfRenderPageResult
                {
                    PageNumber = Number,
                    RenderedPage = new PdfRenderedPage
                    {
                        PageNumber = RenderedPage.PageNumber,
                        Dpi = request.Dpi ?? RenderedPage.Dpi,
                        EncodedFormat = request.Format,
                        Image = new ImageFrame(
                            RenderedPage.Image.Data,
                            RenderedPage.Image.Width,
                            RenderedPage.Image.Height,
                            RenderedImageMimeType(request.Format)),
                        Geometry = RenderedPage.Geometry,
                        Metadata = new Dictionary<string, object?>(RenderedPage.Metadata)
                    }
                });
        }

        private static string RenderedImageMimeType(PdfRenderImageFormat format) => format switch
        {
            PdfRenderImageFormat.Bmp => MimeTypes.ImageBmp,
            PdfRenderImageFormat.Png => MimeTypes.ImagePng,
            PdfRenderImageFormat.Jpeg => MimeTypes.ImageJpeg,
            _ => "application/octet-stream"
        };
    }

    private sealed class FakeLayoutProjector : IPdfLayoutProjector
    {
        public PdfLayoutProjectionResult Project(PdfLayoutProjectionInput input, PdfPipelineContext context) => new()
        {
            Text = "projected fake text",
            ProjectedItems = input.TextItems
        };
    }

    private sealed class FakeOcrExecutor : IPdfOcrExecutor
    {
        public ValueTask<PdfOcrPageResult> RecognizeAsync(PdfRenderedPage page, PdfPipelineContext context) =>
            ValueTask.FromResult(new PdfOcrPageResult
            {
                PageNumber = page.PageNumber,
                Regions =
                [
                    new OcrTextRegion
                    {
                        Text = "ocr recovered text",
                        BoundingBox = new BoundingBox(0, 40, 120, 20),
                        Confidence = 0.92f,
                        Language = context.Options.OcrLanguage
                    }
                ]
            });
    }

    private sealed class FailingOcrExecutor : IPdfOcrExecutor
    {
        public ValueTask<PdfOcrPageResult> RecognizeAsync(PdfRenderedPage page, PdfPipelineContext context) =>
            ValueTask.FromResult(new PdfOcrPageResult
            {
                PageNumber = page.PageNumber,
                Error = new InvalidOperationException("missing traineddata")
            });
    }
}
