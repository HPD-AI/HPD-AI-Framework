using System.Runtime.InteropServices;
using System.Text;
using HPD.TextExtract.Models;
using PDFiumCore;

namespace HPD.TextExtract.Pdf
{
    internal static class PdfAssetExtractor
    {
        private const float MinimumOcrRelevantImageSize = 25f;
        private const float MaximumOcrRelevantPageCoverage = 0.90f;

        public static IReadOnlyList<PdfImageRegion> ExtractImageRegions(PdfiumPageHandle page)
        {
            var objects = fpdf_edit.FPDFPageCountObjects(page.NativePage);
            if (objects <= 0)
            {
                return Array.Empty<PdfImageRegion>();
            }

            var pageArea = page.Size.Width * page.Size.Height;
            var transform = PdfiumGeometry.CreateViewportTransform(page);
            var regions = new List<PdfImageRegion>();
            for (var index = 0; index < objects; index++)
            {
                var imageObject = TryGetImageObject(page.NativePage, index);
                if (imageObject is null || !TryGetObjectBounds(imageObject, transform, out var box))
                {
                    continue;
                }

                var metadata = GetImageMetadata(imageObject, page.NativePage);
                var imageArea = box.Width * box.Height;
                var coverage = pageArea > 0 ? imageArea / pageArea : 0;
                var isRelevant = box.Width >= MinimumOcrRelevantImageSize
                    && box.Height >= MinimumOcrRelevantImageSize
                    && coverage <= MaximumOcrRelevantPageCoverage;

                regions.Add(new PdfImageRegion
                {
                    BoundingBox = box,
                    WidthInSamples = metadata.Width > 0 ? (int)metadata.Width : null,
                    HeightInSamples = metadata.Height > 0 ? (int)metadata.Height : null,
                    BitsPerComponent = metadata.BitsPerPixel > 0 ? (int)metadata.BitsPerPixel : null,
                    IsInline = null,
                    PageCoverage = coverage,
                    IsOcrRelevant = isRelevant,
                    Metadata =
                    {
                        ["backend"] = "PDFium",
                        ["objectIndex"] = index,
                        ["colorspace"] = metadata.Colorspace,
                        ["horizontalDpi"] = metadata.HorizontalDpi,
                        ["verticalDpi"] = metadata.VerticalDpi,
                        ["markedContentId"] = metadata.MarkedContentId,
                        ["filters"] = GetImageFilters(imageObject)
                    }
                });
            }

            return regions;
        }

        public static IReadOnlyList<ExtractedAsset> ExtractImages(PdfiumPageHandle page)
        {
            var objects = fpdf_edit.FPDFPageCountObjects(page.NativePage);
            if (objects <= 0)
            {
                return Array.Empty<ExtractedAsset>();
            }

            var assets = new List<ExtractedAsset>();
            var transform = PdfiumGeometry.CreateViewportTransform(page);
            var imageIndex = 0;
            for (var objectIndex = 0; objectIndex < objects; objectIndex++)
            {
                var imageObject = TryGetImageObject(page.NativePage, objectIndex);
                if (imageObject is null || !TryGetObjectBounds(imageObject, transform, out var box))
                {
                    continue;
                }

                var metadata = GetImageMetadata(imageObject, page.NativePage);
                var data = ReadImageBytes(imageObject);
                assets.Add(new ExtractedAsset
                {
                    Kind = ExtractedAssetKind.EmbeddedImage,
                    Name = $"page-{page.Number}-image-{imageIndex}",
                    MimeType = null,
                    PageNumber = page.Number,
                    BoundingBox = box,
                    Data = data,
                    Metadata =
                    {
                        ["backend"] = "PDFium",
                        ["objectIndex"] = objectIndex,
                        ["widthInSamples"] = metadata.Width,
                        ["heightInSamples"] = metadata.Height,
                        ["bitsPerPixel"] = metadata.BitsPerPixel,
                        ["colorspace"] = metadata.Colorspace,
                        ["horizontalDpi"] = metadata.HorizontalDpi,
                        ["verticalDpi"] = metadata.VerticalDpi,
                        ["markedContentId"] = metadata.MarkedContentId,
                        ["filters"] = GetImageFilters(imageObject),
                        ["encoding"] = "raw-pdf-image-stream"
                    }
                });
                imageIndex++;
            }

            return assets;
        }

        private static FpdfPageobjectT? TryGetImageObject(FpdfPageT page, int index)
        {
            var pageObject = fpdf_edit.FPDFPageGetObject(page, index);
            if (PdfiumBackend.IsNull(pageObject))
            {
                return null;
            }

            var metadata = new FPDF_IMAGEOBJ_METADATA();
            return fpdf_edit.FPDFImageObjGetImageMetadata(pageObject, page, metadata) != 0
                && metadata.Width > 0
                && metadata.Height > 0
                ? pageObject
                : null;
        }

        private static bool TryGetObjectBounds(
            FpdfPageobjectT pageObject,
            PdfiumViewportTransform transform,
            out BoundingBox box)
        {
            float left = 0;
            float bottom = 0;
            float right = 0;
            float top = 0;
            if (fpdf_edit.FPDFPageObjGetBounds(pageObject, ref left, ref bottom, ref right, ref top) != 0
                && right > left
                && top > bottom)
            {
                box = PdfiumGeometry.ToBoundingBox(transform, left, bottom, right, top);
                return true;
            }

            box = default;
            return false;
        }

        private static FPDF_IMAGEOBJ_METADATA GetImageMetadata(FpdfPageobjectT imageObject, FpdfPageT page)
        {
            var metadata = new FPDF_IMAGEOBJ_METADATA();
            _ = fpdf_edit.FPDFImageObjGetImageMetadata(imageObject, page, metadata);
            return metadata;
        }

        private static IReadOnlyList<string> GetImageFilters(FpdfPageobjectT imageObject)
        {
            var count = fpdf_edit.FPDFImageObjGetImageFilterCount(imageObject);
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            var filters = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                var byteCount = fpdf_edit.FPDFImageObjGetImageFilter(imageObject, index, IntPtr.Zero, 0);
                if (byteCount <= 1)
                {
                    continue;
                }

                var buffer = Marshal.AllocHGlobal((int)byteCount);
                try
                {
                    _ = fpdf_edit.FPDFImageObjGetImageFilter(imageObject, index, buffer, byteCount);
                    var data = new byte[(int)byteCount];
                    Marshal.Copy(buffer, data, 0, data.Length);
                    var terminator = Array.IndexOf(data, (byte)0);
                    var length = terminator >= 0 ? terminator : data.Length;
                    if (length > 0)
                    {
                        filters.Add(Encoding.ASCII.GetString(data, 0, length));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return filters;
        }

        private static ReadOnlyMemory<byte> ReadImageBytes(FpdfPageobjectT imageObject)
        {
            var byteCount = fpdf_edit.FPDFImageObjGetImageDataRaw(imageObject, IntPtr.Zero, 0);
            if (byteCount == 0)
            {
                byteCount = fpdf_edit.FPDFImageObjGetImageDataDecoded(imageObject, IntPtr.Zero, 0);
                if (byteCount == 0)
                {
                    return ReadOnlyMemory<byte>.Empty;
                }

                return ReadImageBytes(imageObject, byteCount, decoded: true);
            }

            return ReadImageBytes(imageObject, byteCount, decoded: false);
        }

        private static ReadOnlyMemory<byte> ReadImageBytes(FpdfPageobjectT imageObject, ulong byteCount, bool decoded)
        {
            if (byteCount > int.MaxValue)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            var buffer = Marshal.AllocHGlobal((int)byteCount);
            try
            {
                var actual = decoded
                    ? fpdf_edit.FPDFImageObjGetImageDataDecoded(imageObject, buffer, byteCount)
                    : fpdf_edit.FPDFImageObjGetImageDataRaw(imageObject, buffer, byteCount);
                if (actual == 0)
                {
                    return ReadOnlyMemory<byte>.Empty;
                }

                var data = new byte[(int)actual];
                Marshal.Copy(buffer, data, 0, data.Length);
                return data;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
