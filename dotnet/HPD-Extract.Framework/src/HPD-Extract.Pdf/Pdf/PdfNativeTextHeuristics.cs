using System.Globalization;
using HPD.Extract.Models;

namespace HPD.Extract.Pdf
{
    internal static class PdfNativeTextHeuristics
    {
        public static string ExpandPdfGlyph(uint unicode) => unicode switch
        {
            0x02 => "-",
            0x1A => "ff",
            0x1B => "ft",
            0x1C => "fi",
            0x1D => "Th",
            0x1E => "ffi",
            0x1F => "fl",
            _ => char.ConvertFromUtf32((int)unicode)
        };

        public static bool IsValidUnicodeScalar(uint unicode) =>
            unicode != 0 &&
            unicode is not 0xFFFE and not 0xFFFF &&
            unicode <= 0x10FFFF &&
            unicode is < 0xD800 or > 0xDFFF;

        public static string ToArgb(uint a, uint r, uint g, uint b) =>
            string.Create(CultureInfo.InvariantCulture, $"#{a:X2}{r:X2}{g:X2}{b:X2}");

        public static float AdjustRotation(float angleRadians, int pageRotation)
        {
            if (angleRadians < 0)
            {
                return 0;
            }

            var degrees = angleRadians * 180f / MathF.PI;
            degrees += pageRotation;
            degrees %= 360f;
            return degrees < 0 ? degrees + 360f : degrees;
        }

        public static bool IsInvisibleRenderMode(string? renderMode) =>
            renderMode?.Contains("INVISIBLE", StringComparison.OrdinalIgnoreCase) == true;

        public static bool IsBuggyFontName(string? fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                return false;
            }

            return fontName.StartsWith("TT", StringComparison.Ordinal)
                || fontName.Contains("+TT", StringComparison.Ordinal)
                || fontName.Length >= 7 && fontName[6] == '_';
        }

        public static bool IsBuggyCodepoint(uint codepoint) =>
            codepoint <= 0x1F || codepoint is > 0xE000 and <= 0xF8FF;

        public static void Deduplicate(List<PdfTextItem> items)
        {
            if (items.Count < 2)
            {
                return;
            }

            var keep = new bool[items.Count];
            Array.Fill(keep, true);

            for (var i = 0; i < items.Count; i++)
            {
                if (!keep[i])
                {
                    continue;
                }

                for (var j = i + 1; j < items.Count; j++)
                {
                    if (!keep[j])
                    {
                        continue;
                    }

                    var overlap = OverlapRatio(items[i].BoundingBox, items[j].BoundingBox);
                    if (overlap <= 0)
                    {
                        continue;
                    }

                    if (items[i].Text == items[j].Text)
                    {
                        keep[i] = false;
                        break;
                    }

                    if (overlap > 0.5f && !HasLargeAreaRatio(items[i].BoundingBox, items[j].BoundingBox))
                    {
                        keep[i] = false;
                        break;
                    }
                }
            }

            var idx = 0;
            items.RemoveAll(_ => !keep[idx++]);
        }

        public static float OverlapRatio(BoundingBox a, BoundingBox b)
        {
            var left = Math.Max(a.X, b.X);
            var right = Math.Min(a.Right, b.Right);
            var top = Math.Max(a.Y, b.Y);
            var bottom = Math.Min(a.Bottom, b.Bottom);
            if (left >= right || top >= bottom)
            {
                return 0;
            }

            var intersection = (right - left) * (bottom - top);
            var smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
            return smaller > 0 ? intersection / smaller : 0;
        }

        public static bool HasLargeAreaRatio(BoundingBox a, BoundingBox b)
        {
            var areaA = a.Width * a.Height;
            var areaB = b.Width * b.Height;
            var smaller = Math.Min(areaA, areaB);
            if (smaller <= 0)
            {
                return false;
            }

            var larger = Math.Max(areaA, areaB);
            return larger / smaller > 5f;
        }
    }
}
