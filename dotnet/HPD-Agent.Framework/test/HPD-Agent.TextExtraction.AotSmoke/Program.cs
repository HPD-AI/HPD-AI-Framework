// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Extract.Models;
using HPD.Extract.Pdf;

var fixturePath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "Fixtures", "hello_world.pdf");

if (!File.Exists(fixturePath))
{
    Console.Error.WriteLine($"Missing PDF fixture: {fixturePath}");
    return 1;
}

var engine = new PdfExtractionEngine();
var result = await engine.ExtractAsync(
    ContentInput.FromPath(fixturePath, MimeTypes.Pdf),
    new PdfExtractionOptions
    {
        OcrEnabled = false,
        IncludeTextItems = true,
        IncludeScreenshots = true,
        ScreenshotFormat = PdfRenderImageFormat.Png,
        Dpi = 96,
        MaxPages = 1
    });

if (result.Pages.Count != 1)
{
    Console.Error.WriteLine($"Expected one extracted page, found {result.Pages.Count}.");
    return 2;
}

var page = result.Pages[0];
if (!result.Text.Contains("Hello, world", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Expected extracted text to contain 'Hello, world'. Text length: {result.Text.Length}.");
    return 3;
}

if (page.TextItems.Count == 0)
{
    Console.Error.WriteLine("Expected native text items.");
    return 4;
}

if (page.Metadata.TryGetValue("backend", out var backend) && !string.Equals(Convert.ToString(backend), "PDFium", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Expected PDFium backend metadata, found '{backend}'.");
    return 5;
}

var screenshot = result.Assets.SingleOrDefault(static asset => asset.Kind == ExtractedAssetKind.PageScreenshot);
if (screenshot is null)
{
    Console.Error.WriteLine("Expected one page screenshot asset.");
    return 6;
}

if (!string.Equals(screenshot.MimeType, MimeTypes.ImagePng, StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Expected PNG screenshot, found '{screenshot.MimeType}'.");
    return 7;
}

if (screenshot.Data.Length < 8)
{
    Console.Error.WriteLine($"Expected non-empty PNG screenshot, length was {screenshot.Data.Length}.");
    return 8;
}

var bytes = screenshot.Data.Span;
if (bytes[0] != 0x89
    || bytes[1] != (byte)'P'
    || bytes[2] != (byte)'N'
    || bytes[3] != (byte)'G'
    || bytes[4] != 0x0D
    || bytes[5] != 0x0A
    || bytes[6] != 0x1A
    || bytes[7] != 0x0A)
{
    Console.Error.WriteLine("Expected PNG signature on screenshot asset.");
    return 9;
}

if (!screenshot.Metadata.TryGetValue("width", out var width)
    || !screenshot.Metadata.TryGetValue("height", out var height)
    || Convert.ToInt32(width) <= 0
    || Convert.ToInt32(height) <= 0)
{
    Console.Error.WriteLine("Expected positive screenshot dimensions.");
    return 10;
}

if (result.Diagnostics.Warnings.Count != 0)
{
    Console.Error.WriteLine($"Expected no extraction warnings, found {result.Diagnostics.Warnings.Count}: {string.Join(" | ", result.Diagnostics.Warnings)}");
    return 11;
}

Console.WriteLine($"AOT PDF smoke passed: page={page.Number}, textItems={page.TextItems.Count}, screenshot={width}x{height}, bytes={screenshot.Data.Length}.");
return 0;
