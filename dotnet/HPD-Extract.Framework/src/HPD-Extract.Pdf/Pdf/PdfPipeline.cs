using HPD.Extract.Models;
using PDFiumCore;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HPD.Extract.Pdf
{
    public interface IPdfBackend
    {
        ValueTask<IPdfDocumentHandle> OpenAsync(
            ContentInput input,
            PdfExtractionOptions options,
            CancellationToken cancellationToken = default);
    }

    public interface IPdfDocumentHandle : IDisposable
    {
        PdfBackendCapabilities Capabilities { get; }
        IEnumerable<IPdfPageHandle> GetPages();
    }

    public interface IPdfPageHandle
    {
        int Number { get; }
        PageSize Size { get; }
        int Rotation { get; }
        PdfPageSnapshot ExtractSnapshot(PdfPipelineContext context);
        ValueTask<PdfRenderPageResult> RenderAsync(PdfPageRenderRequest request, PdfPipelineContext context);
    }

    public interface IPdfPageSelector
    {
        HashSet<int>? Parse(string? targetPages);
    }

    public interface IPdfPageQualityAnalyzer
    {
        PdfPageQuality Analyze(PdfPageQualityInput input, PdfPipelineContext context);
        PdfOcrDecision PlanOcr(PdfPageQuality quality, PdfPipelineContext context);
    }

    public interface IPdfLayoutProjector
    {
        PdfLayoutProjectionResult Project(PdfLayoutProjectionInput input, PdfPipelineContext context);
    }

    public interface IPdfOcrExecutor
    {
        ValueTask<PdfOcrPageResult> RecognizeAsync(PdfRenderedPage page, PdfPipelineContext context);
    }

    public interface IPdfOcrTextMerger
    {
        PdfOcrMergeResult Merge(PdfOcrMergeInput input, PdfPipelineContext context);
    }

    public sealed class PdfiumBackend : IPdfBackend
    {
        private static readonly Lazy<bool> s_library = new(static () =>
        {
            fpdfview.FPDF_InitLibrary();
            return true;
        });

        public async ValueTask<IPdfDocumentHandle> OpenAsync(
            ContentInput input,
            PdfExtractionOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(options);
            _ = s_library.Value;

            await using var data = await input.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            var pinnedData = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var document = fpdfview.FPDF_LoadMemDocument64(
                    pinnedData.AddrOfPinnedObject(),
                    (ulong)bytes.LongLength,
                    options.Password ?? string.Empty);

                if (IsNull(document))
                {
                    var error = checked((int)fpdfview.FPDF_GetLastError());
                    throw new PdfBackendException(
                        ClassifyPdfiumError(error),
                        $"PDFium failed to load PDF. Error code: {error}.",
                        error);
                }

                return new PdfiumDocumentHandle(document, pinnedData);
            }
            catch
            {
                pinnedData.Free();
                throw;
            }
        }

        internal static bool IsNull(FpdfDocumentT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
        internal static bool IsNull(FpdfPageT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
        internal static bool IsNull(FpdfTextpageT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
        internal static bool IsNull(FpdfPageobjectT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
        internal static bool IsNull(FpdfBitmapT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
        internal static bool IsNull(FpdfFontT? handle) => handle is null || handle.__Instance == IntPtr.Zero;

        internal static PdfBackendFailureKind ClassifyPdfiumError(int error) => error switch
        {
            2 => PdfBackendFailureKind.FileAccess,
            3 => PdfBackendFailureKind.InvalidFormat,
            4 => PdfBackendFailureKind.PasswordRequired,
            5 => PdfBackendFailureKind.Security,
            6 => PdfBackendFailureKind.PageLoad,
            _ => PdfBackendFailureKind.Unknown
        };
    }

    internal sealed class PdfiumDocumentHandle : IPdfDocumentHandle
    {
        private readonly FpdfDocumentT _document;
        private readonly GCHandle _pinnedData;
        private List<IPdfPageHandle>? _pages;
        private bool _disposed;

        public PdfiumDocumentHandle(FpdfDocumentT document, GCHandle pinnedData)
        {
            _document = document;
            _pinnedData = pinnedData;
        }

        public PdfBackendCapabilities Capabilities { get; } = new()
        {
            Name = "PDFium",
            Version = typeof(fpdfview).Assembly.GetName().Version?.ToString(),
            CanExtractNativeText = true,
            CanExtractGlyphBounds = true,
            CanExtractImageRegions = true,
            CanExtractEmbeddedImages = true,
            CanRenderPages = true,
            CanReportFontMetadata = true,
            CanReportMarkedContent = true,
            CanReportTextRenderMode = true,
            CanReportTextColors = true,
            SupportedRenderFormats = new HashSet<PdfRenderImageFormat>
            {
                PdfRenderImageFormat.Bmp,
                PdfRenderImageFormat.Png
            },
            Metadata =
            {
                ["backend"] = "PDFium",
                ["binding"] = "PDFiumCore",
                ["bindingVersion"] = typeof(fpdfview).Assembly.GetName().Version?.ToString(),
                ["nativeLibrary"] = "pdfium"
            }
        };

        public IEnumerable<IPdfPageHandle> GetPages()
        {
            _pages ??= LoadPages();
            foreach (var page in _pages)
            {
                yield return page;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_pages is not null)
            {
                foreach (var page in _pages)
                {
                    if (page is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            fpdfview.FPDF_CloseDocument(_document);
            _pinnedData.Free();
            _disposed = true;
        }

        private List<IPdfPageHandle> LoadPages()
        {
            var count = fpdfview.FPDF_GetPageCount(_document);
            var pages = new List<IPdfPageHandle>(count);
            for (var index = 0; index < count; index++)
            {
                var page = fpdfview.FPDF_LoadPage(_document, index);
                if (PdfiumBackend.IsNull(page))
                {
                    pages.Add(new FailedPdfPageHandle(
                        index + 1,
                        new PdfBackendException(
                            PdfBackendFailureKind.PageLoad,
                            $"PDFium failed to load PDF page {index + 1}.",
                            checked((int)fpdfview.FPDF_GetLastError()))));
                    continue;
                }

                var width = fpdfview.FPDF_GetPageWidthF(page);
                var height = fpdfview.FPDF_GetPageHeightF(page);
                var rotation = NormalizeRotation(fpdf_edit.FPDFPageGetRotation(page));
                pages.Add(new PdfiumPageHandle(_document, page, index + 1, new PageSize(width, height), rotation));
            }

            return pages;
        }

        private static int NormalizeRotation(int rotation) => rotation switch
        {
            1 => 90,
            2 => 180,
            3 => 270,
            _ => 0
        };
    }

    internal sealed class FailedPdfPageHandle : IPdfPageHandle
    {
        private readonly PdfBackendException _error;

        public FailedPdfPageHandle(int number, PdfBackendException error)
        {
            Number = number;
            _error = error;
        }

        public int Number { get; }
        public PageSize Size { get; } = new(0, 0);
        public int Rotation => 0;

        public PdfPageSnapshot ExtractSnapshot(PdfPipelineContext context)
        {
            context.Diagnostics.Warnings.Add(_error.Message);
            return new PdfPageSnapshot
            {
                Number = Number,
                Size = Size,
                Rotation = Rotation,
                Metadata =
                {
                    ["backend"] = "PDFium",
                    ["pageLoadFailed"] = true,
                    ["failureKind"] = _error.Kind.ToString(),
                    ["backendErrorCode"] = _error.BackendErrorCode
                }
            };
        }

        public ValueTask<PdfRenderPageResult> RenderAsync(PdfPageRenderRequest request, PdfPipelineContext context) =>
            ValueTask.FromResult(new PdfRenderPageResult
            {
                PageNumber = Number,
                Error = _error,
                Metadata =
                {
                    ["pageLoadFailed"] = true,
                    ["failureKind"] = _error.Kind.ToString(),
                    ["backendErrorCode"] = _error.BackendErrorCode
                }
            });
    }

    internal sealed class PdfiumPageHandle : IPdfPageHandle, IDisposable
    {
        private bool _disposed;

        public PdfiumPageHandle(FpdfDocumentT document, FpdfPageT page, int number, PageSize size, int rotation)
        {
            Document = document;
            NativePage = page;
            Number = number;
            Size = size;
            Rotation = rotation;
        }

        public FpdfDocumentT Document { get; }
        public FpdfPageT NativePage { get; }
        public int Number { get; }
        public PageSize Size { get; }
        public int Rotation { get; }

        public PdfPageSnapshot ExtractSnapshot(PdfPipelineContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var nativeItems = context.Options.NativeTextEnabled
                ? PdfNativeTextExtractor.Extract(this)
                : Array.Empty<PdfTextItem>();
            var imageRegions = PdfAssetExtractor.ExtractImageRegions(this);
            var assets = context.Options.IncludeEmbeddedImages
                ? PdfAssetExtractor.ExtractImages(this)
                : Array.Empty<ExtractedAsset>();

            return new PdfPageSnapshot
            {
                Number = Number,
                Size = Size,
                Rotation = Rotation,
                NativeTextItems = nativeItems,
                ImageRegions = imageRegions,
                Assets = assets,
                Metadata =
                {
                    ["backend"] = "PDFium"
                }
            };
        }

        public ValueTask<PdfRenderPageResult> RenderAsync(PdfPageRenderRequest request, PdfPipelineContext context)
        {
            ArgumentNullException.ThrowIfNull(request);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (request.Format is not (PdfRenderImageFormat.Bmp or PdfRenderImageFormat.Png))
            {
                return ValueTask.FromResult(new PdfRenderPageResult
                {
                    PageNumber = Number,
                    Error = new NotSupportedException($"PDFium page rendering currently supports {PdfRenderImageFormat.Bmp} and {PdfRenderImageFormat.Png} output, not {request.Format}.")
                });
            }

            var dpi = request.Dpi is > 0 ? request.Dpi.Value : context.Options.Dpi <= 0 ? 150f : context.Options.Dpi;
            var scale = dpi / 72f;
            var width = Math.Max(1, (int)MathF.Ceiling(Size.Width * scale));
            var height = Math.Max(1, (int)MathF.Ceiling(Size.Height * scale));
            var bitmap = fpdfview.FPDFBitmapCreateEx(
                width,
                height,
                4,
                IntPtr.Zero,
                stride: 0);
            if (PdfiumBackend.IsNull(bitmap))
            {
                return ValueTask.FromResult(new PdfRenderPageResult
                {
                    PageNumber = Number,
                    Error = new InvalidOperationException("PDFium failed to allocate a render bitmap.")
                });
            }

            try
            {
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                fpdfview.FPDF_RenderPageBitmap(bitmap, NativePage, 0, 0, width, height, 0, flags: 0);
                var image = CopyBitmap(bitmap, width, height, request.Format);
                var mimeType = RenderedImageMimeType(request.Format);
                var pageToViewport = PdfiumGeometry.CreateViewportTransform(this);
                var geometry = PdfiumGeometry.CreateRenderGeometry(this, pageToViewport, dpi, width, height);
                return ValueTask.FromResult(new PdfRenderPageResult
                {
                    PageNumber = Number,
                    RenderedPage = new PdfRenderedPage
                    {
                        PageNumber = Number,
                        Dpi = dpi,
                        Image = image,
                        EncodedFormat = request.Format,
                        Geometry = geometry,
                        Metadata =
                        {
                            ["backend"] = "PDFium",
                            ["renderPurpose"] = request.Purpose.ToString(),
                            ["pixelFormat"] = "BGRA32",
                            ["encodedFormat"] = request.Format.ToString(),
                            ["mimeType"] = mimeType,
                            ["pdfPointToPixelScale"] = scale,
                            ["viewportWidth"] = geometry.ViewportSize.Width,
                            ["viewportHeight"] = geometry.ViewportSize.Height,
                            ["pixelWidth"] = geometry.PixelWidth,
                            ["pixelHeight"] = geometry.PixelHeight,
                            ["rotation"] = geometry.Rotation
                        }
                    }
                });
            }
            catch (Exception error)
            {
                return ValueTask.FromResult(new PdfRenderPageResult
                {
                    PageNumber = Number,
                    Error = error
                });
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            fpdfview.FPDF_ClosePage(NativePage);
            _disposed = true;
        }

        private static ImageFrame CopyBitmap(FpdfBitmapT bitmap, int width, int height, PdfRenderImageFormat format)
        {
            var stride = fpdfview.FPDFBitmapGetStride(bitmap);
            var source = fpdfview.FPDFBitmapGetBuffer(bitmap);
            var data = new byte[stride * height];
            Marshal.Copy(source, data, 0, data.Length);
            var encoded = format switch
            {
                PdfRenderImageFormat.Bmp => EncodeTopDownBgraBmp(data, width, height, stride),
                PdfRenderImageFormat.Png => EncodeBgraPng(data, width, height, stride),
                _ => throw new NotSupportedException($"Unsupported PDF render image format: {format}.")
            };

            return new ImageFrame(encoded, width, height, RenderedImageMimeType(format));
        }

        private static string RenderedImageMimeType(PdfRenderImageFormat format) => format switch
        {
            PdfRenderImageFormat.Bmp => MimeTypes.ImageBmp,
            PdfRenderImageFormat.Png => MimeTypes.ImagePng,
            PdfRenderImageFormat.Jpeg => MimeTypes.ImageJpeg,
            _ => "application/octet-stream"
        };

        private static byte[] EncodeTopDownBgraBmp(byte[] bgra, int width, int height, int stride)
        {
            const int fileHeaderSize = 14;
            const int infoHeaderSize = 40;
            const int bytesPerPixel = 4;
            var rowBytes = checked(width * bytesPerPixel);
            var pixelBytes = checked(rowBytes * height);
            var pixelOffset = fileHeaderSize + infoHeaderSize;
            var fileSize = checked(pixelOffset + pixelBytes);
            var bmp = new byte[fileSize];

            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            WriteInt32(bmp, 2, fileSize);
            WriteInt32(bmp, 10, pixelOffset);
            WriteInt32(bmp, 14, infoHeaderSize);
            WriteInt32(bmp, 18, width);
            WriteInt32(bmp, 22, -height);
            WriteInt16(bmp, 26, 1);
            WriteInt16(bmp, 28, 32);
            WriteInt32(bmp, 34, pixelBytes);

            for (var y = 0; y < height; y++)
            {
                Buffer.BlockCopy(bgra, y * stride, bmp, pixelOffset + y * rowBytes, rowBytes);
            }

            return bmp;
        }

        private static byte[] EncodeBgraPng(byte[] bgra, int width, int height, int stride)
        {
            const int bytesPerPixel = 4;
            var rowBytes = checked(width * bytesPerPixel);
            using var raw = new MemoryStream(checked((rowBytes + 1) * height));
            for (var y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                var sourceOffset = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var pixelOffset = sourceOffset + x * bytesPerPixel;
                    raw.WriteByte(bgra[pixelOffset + 2]);
                    raw.WriteByte(bgra[pixelOffset + 1]);
                    raw.WriteByte(bgra[pixelOffset]);
                    raw.WriteByte(bgra[pixelOffset + 3]);
                }
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                var rawBytes = raw.GetBuffer().AsSpan(0, checked((int)raw.Length));
                zlib.Write(rawBytes);
            }

            using var png = new MemoryStream();
            png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
            BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
            header[8] = 8;
            header[9] = 6;
            header[10] = 0;
            header[11] = 0;
            header[12] = 0;
            WritePngChunk(png, "IHDR"u8, header);
            WritePngChunk(png, "IDAT"u8, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
            WritePngChunk(png, "IEND"u8, ReadOnlySpan<byte>.Empty);
            return png.ToArray();
        }

        private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            stream.Write(length);
            stream.Write(type);
            stream.Write(data);
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(type, data));
            stream.Write(crc);
        }

        private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            crc = UpdateCrc32(crc, type);
            crc = UpdateCrc32(crc, data);
            return ~crc;
        }

        private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
        {
            const uint polynomial = 0xEDB88320u;
            for (var i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? polynomial ^ (crc >> 1) : crc >> 1;
                }
            }

            return crc;
        }

        private static void WriteInt16(byte[] data, int offset, short value) =>
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, sizeof(short)), value);

        private static void WriteInt32(byte[] data, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
    }

    public sealed class DefaultPdfPageSelector : IPdfPageSelector
    {
        public HashSet<int>? Parse(string? targetPages) => PdfPageSelector.Parse(targetPages);
    }

    public sealed class DefaultPdfPageQualityAnalyzer : IPdfPageQualityAnalyzer
    {
        public PdfPageQuality Analyze(PdfPageQualityInput input, PdfPipelineContext context) =>
            PdfPageQualityAnalyzer.Analyze(input);

        public PdfOcrDecision PlanOcr(PdfPageQuality quality, PdfPipelineContext context) =>
            PdfPageQualityAnalyzer.PlanOcr(quality, context.Options);
    }

    public sealed class DefaultPdfLayoutProjector : IPdfLayoutProjector
    {
        public PdfLayoutProjectionResult Project(PdfLayoutProjectionInput input, PdfPipelineContext context) =>
            PdfLayoutProjector.Project(input);
    }

    public sealed class NoOpPdfOcrExecutor : IPdfOcrExecutor
    {
        public ValueTask<PdfOcrPageResult> RecognizeAsync(PdfRenderedPage page, PdfPipelineContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PdfOcrPageResult
            {
                PageNumber = page.PageNumber,
                Regions = Array.Empty<OcrTextRegion>()
            });
        }
    }

    public sealed class HttpPdfOcrExecutor : IPdfOcrExecutor, IDisposable
    {
        private readonly HttpClient _client;
        private readonly bool _disposeClient;

        public HttpPdfOcrExecutor(HttpClient? client = null)
        {
            _client = client ?? new HttpClient();
            _disposeClient = client is null;
        }

        public async ValueTask<PdfOcrPageResult> RecognizeAsync(PdfRenderedPage page, PdfPipelineContext context)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (context.Options.OcrEndpoint is not { } endpoint)
            {
                return new PdfOcrPageResult
                {
                    PageNumber = page.PageNumber,
                    Error = new InvalidOperationException("HTTP OCR endpoint is not configured.")
                };
            }

            try
            {
                using var form = new MultipartFormDataContent();
                var file = new ByteArrayContent(page.Image.Data.ToArray());
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(page.Image.MimeType);
                form.Add(file, "file", $"page-{page.PageNumber}.{RenderedImageExtension(page.EncodedFormat)}");
                form.Add(new StringContent(context.Options.OcrLanguage), "language");
                form.Add(new StringContent(page.Image.Width.ToString(CultureInfo.InvariantCulture)), "width");
                form.Add(new StringContent(page.Image.Height.ToString(CultureInfo.InvariantCulture)), "height");
                form.Add(new StringContent(page.Dpi.ToString(CultureInfo.InvariantCulture)), "dpi");

                using var response = await _client.PostAsync(endpoint, form, context.CancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(context.CancellationToken).ConfigureAwait(false);
                var regions = await ParseHttpOcrResponseAsync(stream, context.CancellationToken).ConfigureAwait(false);

                return new PdfOcrPageResult
                {
                    PageNumber = page.PageNumber,
                    CoordinateSpace = OcrCoordinateSpace.RenderPixelsTopLeft,
                    Regions = regions,
                    Metadata =
                    {
                        ["engine"] = "http",
                        ["endpoint"] = endpoint.ToString()
                    }
                };
            }
            catch (Exception error)
            {
                return new PdfOcrPageResult
                {
                    PageNumber = page.PageNumber,
                    Error = error,
                    Metadata =
                    {
                        ["engine"] = "http",
                        ["endpoint"] = context.Options.OcrEndpoint?.ToString()
                    }
                };
            }
        }

        public void Dispose()
        {
            if (_disposeClient)
            {
                _client.Dispose();
            }
        }

        private static string RenderedImageExtension(PdfRenderImageFormat format) => format switch
        {
            PdfRenderImageFormat.Bmp => "bmp",
            PdfRenderImageFormat.Png => "png",
            PdfRenderImageFormat.Jpeg => "jpg",
            _ => "bin"
        };

        private static async ValueTask<IReadOnlyList<OcrTextRegion>> ParseHttpOcrResponseAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("HTTP OCR response must contain a 'results' array.");
            }

            var regions = new List<OcrTextRegion>();
            foreach (var item in results.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                    ? textElement.GetString() ?? string.Empty
                    : string.Empty;
                var confidence = item.TryGetProperty("confidence", out var confidenceElement) &&
                    confidenceElement.TryGetSingle(out var parsedConfidence)
                        ? parsedConfidence
                        : 0f;
                var box = item.TryGetProperty("bbox", out var bboxElement)
                    ? ParseOcrBoundingBox(bboxElement)
                    : default;

                regions.Add(new OcrTextRegion
                {
                    Text = text,
                    BoundingBox = box,
                    Confidence = confidence,
                    CoordinateSpace = OcrCoordinateSpace.RenderPixelsTopLeft
                });
            }

            return regions;
        }

        private static BoundingBox ParseOcrBoundingBox(JsonElement bbox)
        {
            if (bbox.ValueKind != JsonValueKind.Array || bbox.GetArrayLength() < 4)
            {
                return default;
            }

            var values = new float[4];
            var index = 0;
            foreach (var value in bbox.EnumerateArray())
            {
                if (index >= values.Length)
                {
                    break;
                }

                values[index++] = value.TryGetSingle(out var parsed) ? parsed : 0f;
            }

            return new BoundingBox(
                values[0],
                values[1],
                Math.Max(0, values[2] - values[0]),
                Math.Max(0, values[3] - values[1]));
        }
    }

    public sealed class DefaultPdfOcrTextMerger : IPdfOcrTextMerger
    {
        public PdfOcrMergeResult Merge(PdfOcrMergeInput input, PdfPipelineContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!input.OcrResult.Succeeded || input.OcrResult.Regions.Count == 0)
            {
                return new PdfOcrMergeResult
                {
                    TextItems = input.NativeItems,
                    Metrics =
                    {
                        ["ocrRegionCount"] = input.OcrResult.Regions.Count,
                        ["mergedOcrItemCount"] = 0
                    }
                };
            }

            var nativeItems = input.Quality.LooksGarbled
                ? Array.Empty<PdfTextItem>()
                : input.NativeItems
                    .Where(static item => !IsCorruptNativeItem(item))
                    .ToArray();
            var droppedNativeItemCount = input.NativeItems.Count - nativeItems.Length;
            var merged = new List<PdfTextItem>(nativeItems);
            var skippedOverlappingOcrItemCount = 0;
            var skippedLowConfidenceOcrItemCount = 0;
            var skippedEmptyOcrItemCount = 0;
            var cleanedOcrItemCount = 0;
            for (var i = 0; i < input.OcrResult.Regions.Count; i++)
            {
                var region = input.OcrResult.Regions[i];
                if (region.Confidence <= 0.1f || string.IsNullOrWhiteSpace(region.Text))
                {
                    if (region.Confidence <= 0.1f)
                    {
                        skippedLowConfidenceOcrItemCount++;
                    }
                    else
                    {
                        skippedEmptyOcrItemCount++;
                    }

                    continue;
                }

                var box = ConvertToViewportPoints(region, input);
                if (OverlapsExistingText(nativeItems, box, tolerance: 2f))
                {
                    skippedOverlappingOcrItemCount++;
                    continue;
                }

                var cleaned = CleanOcrTableArtifacts(region.Text);
                if (cleaned.Length == 0)
                {
                    skippedEmptyOcrItemCount++;
                    continue;
                }

                if (!string.Equals(cleaned, region.Text.Trim(), StringComparison.Ordinal))
                {
                    cleanedOcrItemCount++;
                }

                merged.Add(new PdfTextItem
                {
                    Text = cleaned,
                    BoundingBox = box,
                    Layer = PdfTextLayerKind.Ocr,
                    Font = new PdfFontInfo
                    {
                        Name = "OCR",
                        Size = box.Height > 0 ? box.Height : null
                    },
                    Confidence = region.Confidence,
                    Metadata =
                    {
                        ["language"] = region.Language,
                        ["ocrPageNumber"] = input.OcrResult.PageNumber
                    }
                });
            }

            return new PdfOcrMergeResult
            {
                TextItems = merged,
                Metrics =
                {
                    ["ocrRegionCount"] = input.OcrResult.Regions.Count,
                    ["nativeItemCount"] = input.NativeItems.Count,
                    ["nativeSurvivorItemCount"] = nativeItems.Length,
                    ["droppedNativeItemCount"] = droppedNativeItemCount,
                    ["skippedOverlappingOcrItemCount"] = skippedOverlappingOcrItemCount,
                    ["skippedLowConfidenceOcrItemCount"] = skippedLowConfidenceOcrItemCount,
                    ["skippedEmptyOcrItemCount"] = skippedEmptyOcrItemCount,
                    ["cleanedOcrItemCount"] = cleanedOcrItemCount,
                    ["mergedOcrItemCount"] = merged.Count - nativeItems.Length
                }
            };
        }

        private static bool IsCorruptNativeItem(PdfTextItem item) =>
            item.Font?.LooksCorrupt == true ||
            item.HasUnicodeMapError == true ||
            PdfGarbledTextDetector.IsLikelyGarbled(item.Text);

        private static bool OverlapsExistingText(
            IReadOnlyList<PdfTextItem> items,
            BoundingBox box,
            float tolerance)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var existing = items[i].BoundingBox;
                var overlapX = box.X < existing.Right + tolerance && box.Right > existing.X - tolerance;
                var overlapY = box.Y < existing.Bottom + tolerance && box.Bottom > existing.Y - tolerance;
                if (overlapX && overlapY)
                {
                    return true;
                }
            }

            return false;
        }

        private static string CleanOcrTableArtifacts(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            var withoutArtifacts = trimmed
                .TrimStart('|', '[', ']', '(', ')', '{', '}')
                .TrimEnd('|', '[', ']', '(', ')', '{', '}')
                .Trim();
            if (withoutArtifacts.Length == 0)
            {
                return trimmed;
            }

            var isNumericLike = IsNumericLikeOcrText(withoutArtifacts)
                || string.Equals(withoutArtifacts, "N/A", StringComparison.Ordinal)
                || string.Equals(withoutArtifacts, "Z", StringComparison.Ordinal)
                || string.Equals(withoutArtifacts, "-", StringComparison.Ordinal);
            return isNumericLike ? withoutArtifacts : trimmed;
        }

        private static bool IsNumericLikeOcrText(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (!char.IsAsciiDigit(c) && c is not (',' or '.' or ' ' or '%' or '-' or '+' or '*' or '/'))
                {
                    return false;
                }
            }

            return text.Length > 0;
        }

        private static BoundingBox ConvertToViewportPoints(OcrTextRegion region, PdfOcrMergeInput input)
        {
            var box = region.BoundingBox;
            var coordinateSpace = region.CoordinateSpace == OcrCoordinateSpace.Unknown
                ? input.OcrResult.CoordinateSpace
                : region.CoordinateSpace;

            return coordinateSpace switch
            {
                OcrCoordinateSpace.RenderPixelsTopLeft => RenderedPixelsToViewportPoints(box, input),
                OcrCoordinateSpace.NormalizedTopLeft => input.RenderedPage?.Geometry is { } geometry
                    ? geometry.NormalizedTopLeftToViewportBox(box)
                    : new BoundingBox(
                        box.X * input.PageSize.Width,
                        box.Y * input.PageSize.Height,
                        box.Width * input.PageSize.Width,
                        box.Height * input.PageSize.Height),
                _ => box
            };
        }

        private static BoundingBox RenderedPixelsToViewportPoints(BoundingBox box, PdfOcrMergeInput input)
        {
            var geometry = input.RenderedPage?.Geometry;
            if (geometry is not null && geometry.PixelWidth > 0 && geometry.PixelHeight > 0)
            {
                return geometry.PixelTopLeftToViewportBox(box);
            }

            var image = input.RenderedPage?.Image;
            if (image is { Width: > 0, Height: > 0 } renderedImage &&
                input.PageSize.Width > 0 &&
                input.PageSize.Height > 0)
            {
                var scaleX = input.PageSize.Width / renderedImage.Width;
                var scaleY = input.PageSize.Height / renderedImage.Height;
                return new BoundingBox(
                    box.X * scaleX,
                    box.Y * scaleY,
                    box.Width * scaleX,
                    box.Height * scaleY);
            }

            var dpi = input.RenderedPage?.Dpi ?? 0;
            if (dpi > 0)
            {
                var scale = 72f / dpi;
                return new BoundingBox(box.X * scale, box.Y * scale, box.Width * scale, box.Height * scale);
            }

            return box;
        }
        }
    }
