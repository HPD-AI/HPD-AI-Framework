# HPD-TextExtract

.NET-native text and document extraction library with rich PDF structure, diagnostics, OCR planning, and layout-aware outputs.

## Install

```bash
dotnet add package HPD-TextExtract
```

## Use When

Use this package when an app or library needs to turn files, byte payloads, or URLs into text plus structured extraction metadata.

Supported inputs include:

- PDF
- Plain text, Markdown, JSON, and XML
- HTML and web URLs
- Microsoft Word, Excel, and PowerPoint Open XML documents
- Images through an injected OCR engine

For HPD agent middleware, use `HPD-Agent.TextExtraction`, which builds on this package.

## Quick Start

```csharp
using HPD.TextExtract;

using var extractor = new TextExtractionUtility();
var result = await extractor.ExtractTextAsync("document.pdf");

if (!result.IsSuccess)
{
    throw new InvalidOperationException(result.ErrorMessage);
}

Console.WriteLine(result.ExtractedText);
```

## Binary Payloads

```csharp
using HPD.TextExtract;
using HPD.TextExtract.Models;

var bytes = await File.ReadAllBytesAsync("document.pdf");

using var extractor = new TextExtractionUtility();
var result = await extractor.ExtractTextAsync(
    bytes,
    mimeType: MimeTypes.Pdf,
    fileName: "document.pdf");
```

## Dependency Injection

```csharp
using HPD.TextExtract;

builder.Services.AddTextExtraction();
```

Register custom decoders or OCR engines when the built-in behavior is not enough:

```csharp
using HPD.TextExtract;
using HPD.TextExtract.Decoders;

builder.Services.AddTextExtractionWithOcr<MyOcrEngine>();
```

## PDF Notes

PDF extraction is powered by PDFium through `PDFiumCore`.

The PDF pipeline extracts native text, glyph geometry, font metadata, colors, embedded image regions, optional screenshots, and diagnostics. It can also plan OCR for scanned or low-quality pages when an OCR executor is configured.

PDFium is a native dependency. The NuGet dependency brings platform-specific native assets, including macOS arm64 and x64, Windows x64/x86, and Linux x64 through the upstream PDFium packages. Native libraries are deployed as sidecar runtime assets; they are not embedded inside `HPD.TextExtract.dll`.

## Output Shape

`TextExtractionResult` gives the simple text view:

- `IsSuccess`
- `ExtractedText`
- `FileName`
- `MimeType`
- `ProcessingTime`
- `ErrorMessage`

For richer callers, `TextExtractionResult.Extraction` exposes:

- `Content.Sections`
- `Pages`
- `TextItems`
- `Assets`
- `Diagnostics`
- `Metadata`

## Target Frameworks

This package targets the repo-standard modern frameworks:

- `net8.0`
- `net9.0`
- `net10.0`

It is configured for trimming, single-file analysis, and Native AOT analysis.
