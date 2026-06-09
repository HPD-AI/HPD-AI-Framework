using HPD.TextExtract.Models;
using PDFiumCore;

namespace HPD.TextExtract.Pdf
{
    internal static class PdfiumGeometry
    {
        public static PdfiumViewportTransform CreateViewportTransform(PdfiumPageHandle page)
        {
            var viewBox = GetViewBox(page);
            var (originX, originY) = PageToViewport(page, viewBox, 0, 0);
            var (unitX, unitXy) = PageToViewport(page, viewBox, 1, 0);
            var (unitYx, unitY) = PageToViewport(page, viewBox, 0, 1);

            return new PdfiumViewportTransform(
                unitX - originX,
                unitYx - originX,
                unitXy - originY,
                unitY - originY,
                originX,
                originY);
        }

        public static PdfRenderGeometry CreateRenderGeometry(
            PdfiumPageHandle page,
            PdfiumViewportTransform pageToViewport,
            float dpi,
            int pixelWidth,
            int pixelHeight)
        {
            var viewportWidth = pixelWidth > 0 && dpi > 0
                ? pixelWidth * 72f / dpi
                : page.Size.Width;
            var viewportHeight = pixelHeight > 0 && dpi > 0
                ? pixelHeight * 72f / dpi
                : page.Size.Height;
            var viewportToPixel = new PdfAffineTransform(
                pixelWidth > 0 && viewportWidth > 0 ? pixelWidth / viewportWidth : 1,
                0,
                0,
                pixelHeight > 0 && viewportHeight > 0 ? pixelHeight / viewportHeight : 1,
                0,
                0);

            return new PdfRenderGeometry
            {
                ViewportSize = new PageSize(viewportWidth, viewportHeight),
                Rotation = page.Rotation,
                Dpi = dpi,
                PixelWidth = pixelWidth,
                PixelHeight = pixelHeight,
                PageToViewport = pageToViewport.ToAffineTransform(),
                ViewportToPage = pageToViewport.ToAffineTransform().Invert(),
                ViewportToPixel = viewportToPixel,
                PixelToViewport = viewportToPixel.Invert()
            };
        }

        public static BoundingBox ToBoundingBox(PdfiumViewportTransform transform, double left, double bottom, double right, double top)
        {
            var (lowerLeftX, lowerLeftY) = transform.Transform((float)left, (float)bottom);
            var (upperRightX, upperRightY) = transform.Transform((float)right, (float)top);
            var x = MathF.Min(lowerLeftX, upperRightX);
            var y = MathF.Min(lowerLeftY, upperRightY);
            return new BoundingBox(
                x,
                y,
                MathF.Abs(upperRightX - lowerLeftX),
                MathF.Abs(upperRightY - lowerLeftY));
        }

        private static FS_RECTF_ GetViewBox(PdfiumPageHandle page)
        {
            var rect = new FS_RECTF_();
            if (fpdfview.FPDF_GetPageBoundingBox(page.NativePage, rect) != 0
                && rect.Right > rect.Left
                && rect.Top > rect.Bottom)
            {
                return rect;
            }

            rect.Left = 0;
            rect.Bottom = 0;
            rect.Right = page.Size.Width;
            rect.Top = page.Size.Height;
            return rect;
        }

        private static (float X, float Y) PageToViewport(
            PdfiumPageHandle page,
            FS_RECTF_ viewBox,
            float pageX,
            float pageY)
        {
            var width = viewBox.Right - viewBox.Left;
            var height = viewBox.Top - viewBox.Bottom;
            if (page.Rotation is 90 or 270)
            {
                (width, height) = (height, width);
            }

            var deviceWidth = Math.Max(1, (int)MathF.Round(width * 1000f));
            var deviceHeight = Math.Max(1, (int)MathF.Round(height * 1000f));
            var deviceX = 0;
            var deviceY = 0;
            _ = fpdfview.FPDF_PageToDevice(
                page.NativePage,
                0,
                0,
                deviceWidth,
                deviceHeight,
                rotate: 0,
                pageX,
                pageY,
                ref deviceX,
                ref deviceY);

            return (deviceX / 1000f, deviceY / 1000f);
        }
    }

    internal readonly record struct PdfiumViewportTransform(
        float A,
        float B,
        float C,
        float D,
        float E,
        float F)
    {
        public (float X, float Y) Transform(float pageX, float pageY) =>
            (A * pageX + B * pageY + E, C * pageX + D * pageY + F);

        public PdfAffineTransform ToAffineTransform() => new(A, B, C, D, E, F);
    }
}
