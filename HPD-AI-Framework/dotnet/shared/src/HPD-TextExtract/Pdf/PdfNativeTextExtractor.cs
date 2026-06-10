using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HPD.TextExtract.Models;
using PDFiumCore;

namespace HPD.TextExtract.Pdf
{
    internal static class PdfNativeTextExtractor
    {
        private const float MaxInlineGap = 15f;

        public static IReadOnlyList<PdfTextItem> Extract(PdfiumPageHandle page)
        {
            var textPage = fpdf_text.FPDFTextLoadPage(page.NativePage);
            if (PdfiumBackend.IsNull(textPage))
            {
                return Array.Empty<PdfTextItem>();
            }

            try
            {
                var count = fpdf_text.FPDFTextCountChars(textPage);
                if (count <= 0)
                {
                    return Array.Empty<PdfTextItem>();
                }

                var transform = PdfiumGeometry.CreateViewportTransform(page);
                var items = new List<PdfTextItem>(Math.Max(1, count / 4));
                var segment = new SegmentBuilder();

                for (var index = 0; index < count; index++)
                {
                    var glyph = ExtractGlyph(textPage, transform, page.Rotation, index);
                    if (glyph is null)
                    {
                        continue;
                    }

                    if (glyph.IsLineBreak)
                    {
                        segment.Flush(items);
                        continue;
                    }

                    if (glyph.IsSpace)
                    {
                        segment.MarkPendingSpace();
                        continue;
                    }

                    if (!segment.HasContent)
                    {
                        segment.Start(glyph);
                        continue;
                    }

                    if (segment.Layer != glyph.Layer)
                    {
                        segment.Flush(items);
                        segment.Start(glyph);
                        continue;
                    }

                    var yOverlap = glyph.LooseBox.Y < segment.Bottom + 2f
                        && glyph.LooseBox.Bottom > segment.Top - 2f;
                    var gap = glyph.StrictBox.X - segment.LastStrictRight;
                    var lineChanged = glyph.StrictBox.Y > segment.LastStrictBottom + 2f
                        || (gap < -5f && glyph.StrictBox.Y > segment.LastStrictBottom)
                        || (segment.Width > 20f && gap < -(segment.Width * 0.5f));
                    var dotLeaderBreak = IsDotLeaderBreak(segment, glyph, gap);

                    if (!yOverlap || lineChanged || gap >= MaxInlineGap || dotLeaderBreak)
                    {
                        segment.Flush(items);
                        segment.Start(glyph);
                        continue;
                    }

                    if (segment.PendingSpace)
                    {
                        if (gap > segment.AverageCharacterWidth * 2.2f)
                        {
                            segment.Flush(items);
                            segment.Start(glyph);
                        }
                        else
                        {
                            segment.CommitPendingSpace();
                            segment.Append(glyph);
                        }
                    }
                    else
                    {
                        segment.Append(glyph);
                    }
                }

                segment.Flush(items);
                ApplyInvisibleTextPolicy(items);
                PdfNativeTextHeuristics.Deduplicate(items);
                return items;
            }
            finally
            {
                fpdf_text.FPDFTextClosePage(textPage);
            }
        }

        private static PdfGlyph? ExtractGlyph(
            FpdfTextpageT textPage,
            PdfiumViewportTransform transform,
            int pageRotation,
            int index)
        {
            var unicode = fpdf_text.FPDFTextGetUnicode(textPage, index);
            if (!PdfNativeTextHeuristics.IsValidUnicodeScalar(unicode))
            {
                return null;
            }

            var text = PdfNativeTextHeuristics.ExpandPdfGlyph(unicode);
            if (text.Length == 0)
            {
                return null;
            }

            var first = text[0];
            if (first is '\r' or '\n')
            {
                return PdfGlyph.LineBreak(index);
            }

            if (first == ' ')
            {
                return PdfGlyph.Space(index);
            }

            if (!TryGetCharacterBoxes(textPage, index, transform, out var strictBox, out var looseBox))
            {
                return null;
            }

            if (looseBox.Height < 0.5f)
            {
                return null;
            }

            FpdfPageobjectT? textObject = null;
            var renderMode = GetRenderMode(textObject);
            var layer = PdfNativeTextHeuristics.IsInvisibleRenderMode(renderMode)
                ? PdfTextLayerKind.InvisibleOcrLayer
                : PdfTextLayerKind.Native;
            var font = GetFont(textPage, textObject, index, looseBox, unicode);
            var rotation = PdfNativeTextHeuristics.AdjustRotation(fpdf_text.FPDFTextGetCharAngle(textPage, index), pageRotation);

            var metadata = new Dictionary<string, object?>
            {
                ["backend"] = "PDFium",
                ["pdfiumCoreBinding"] = "4688"
            };

            if (TryGetTextMatrix(textPage, index, out var matrix))
            {
                metadata["matrix"] = matrix;
            }

            return new PdfGlyph
            {
                Text = text,
                Unicode = unicode,
                Index = index,
                StrictBox = strictBox,
                LooseBox = looseBox,
                Layer = layer,
                Font = font,
                HasUnicodeMapError = false,
                MarkedContentId = GetMarkedContentId(textObject),
                RenderMode = renderMode,
                FillColorArgb = GetColor(textPage, index, fill: true),
                StrokeColorArgb = GetColor(textPage, index, fill: false),
                Rotation = rotation,
                Metadata = metadata
            };
        }

        private static bool TryGetCharacterBoxes(
            FpdfTextpageT textPage,
            int index,
            PdfiumViewportTransform transform,
            out BoundingBox strictBox,
            out BoundingBox looseBox)
        {
            var hasStrict = TryGetStrictCharacterBox(textPage, index, transform, out strictBox);
            var hasLoose = TryGetLooseCharacterBox(textPage, index, transform, out looseBox);
            if (hasStrict && hasLoose)
            {
                return true;
            }

            if (hasStrict)
            {
                looseBox = strictBox;
                return true;
            }

            if (hasLoose)
            {
                strictBox = looseBox;
                return true;
            }

            return false;
        }

        private static bool TryGetStrictCharacterBox(
            FpdfTextpageT textPage,
            int index,
            PdfiumViewportTransform transform,
            out BoundingBox box)
        {
            double left = 0;
            double right = 0;
            double bottom = 0;
            double top = 0;
            if (fpdf_text.FPDFTextGetCharBox(textPage, index, ref left, ref right, ref bottom, ref top) != 0
                && right > left
                && top > bottom)
            {
                box = PdfiumGeometry.ToBoundingBox(transform, left, bottom, right, top);
                return true;
            }

            box = default;
            return false;
        }

        private static bool TryGetLooseCharacterBox(
            FpdfTextpageT textPage,
            int index,
            PdfiumViewportTransform transform,
            out BoundingBox box)
        {
            var loose = new FS_RECTF_();
            if (fpdf_text.FPDFTextGetLooseCharBox(textPage, index, loose) != 0
                && loose.Right > loose.Left
                && loose.Top > loose.Bottom)
            {
                box = PdfiumGeometry.ToBoundingBox(transform, loose.Left, loose.Bottom, loose.Right, loose.Top);
                return true;
            }

            box = default;
            return false;
        }

        private static bool TryGetTextMatrix(FpdfTextpageT textPage, int index, out string value)
        {
            var matrix = new FS_MATRIX_();
            if (fpdf_text.FPDFTextGetMatrix(textPage, index, matrix) == 0)
            {
                value = string.Empty;
                return false;
            }

            value = string.Create(
                CultureInfo.InvariantCulture,
                $"{matrix.A},{matrix.B},{matrix.C},{matrix.D},{matrix.E},{matrix.F}");
            return true;
        }

        private static PdfFontInfo? GetFont(
            FpdfTextpageT textPage,
            FpdfPageobjectT? textObject,
            int index,
            BoundingBox box,
            uint unicode)
        {
            var name = GetFontName(textPage, index, out var textFontFlags);
            var size = (float?)fpdf_text.FPDFTextGetFontSize(textPage, index);
            var weight = fpdf_text.FPDFTextGetFontWeight(textPage, index);
            var baseName = default(string);
            var familyName = default(string);
            var flags = textFontFlags;
            var isEmbedded = default(bool?);
            var ascent = default(float?);
            var descent = default(float?);
            var glyphWidth = default(float?);

            if (!PdfiumBackend.IsNull(textObject))
            {
                var fontHandle = fpdf_edit.FPDFTextObjGetFont(textObject);
                if (!PdfiumBackend.IsNull(fontHandle))
                {
                    baseName = GetFontString(fontHandle, FontStringKind.BaseName);
                    var fontFlags = fpdf_edit.FPDFFontGetFlags(fontHandle);
                    if (fontFlags != 0)
                    {
                        flags = fontFlags;
                    }

                    var fontWeight = fpdf_edit.FPDFFontGetWeight(fontHandle);
                    if (fontWeight > 0)
                    {
                        weight = fontWeight;
                    }

                    if (size is > 0)
                    {
                        float value = 0;
                        if (fpdf_edit.FPDFFontGetAscent(fontHandle, size.Value, ref value) != 0)
                        {
                            ascent = value;
                        }

                        value = 0;
                        if (fpdf_edit.FPDFFontGetDescent(fontHandle, size.Value, ref value) != 0)
                        {
                            descent = value;
                        }

                        value = 0;
                        if (fpdf_edit.FPDFFontGetGlyphWidth(fontHandle, unicode, size.Value, ref value) != 0 && value > 0)
                        {
                            glyphWidth = value;
                        }
                    }
                }
            }

            var resolvedName = baseName ?? name;
            if (resolvedName is null && size is null && flags is null && weight < 0)
            {
                return null;
            }

            var looksCorrupt = PdfNativeTextHeuristics.IsBuggyFontName(resolvedName)
                || PdfNativeTextHeuristics.IsBuggyFontName(name)
                || PdfNativeTextHeuristics.IsBuggyCodepoint(unicode);

            return new PdfFontInfo
            {
                Name = resolvedName,
                BaseName = baseName,
                FamilyName = familyName,
                Size = size,
                Height = box.Height,
                Ascent = ascent,
                Descent = descent,
                Weight = weight >= 0 ? weight : null,
                Flags = flags,
                IsEmbedded = isEmbedded,
                LooksCorrupt = looksCorrupt,
                Metadata =
                {
                    ["pdfiumFontName"] = name,
                    ["glyphWidth"] = glyphWidth
                }
            };
        }

        private static string? GetFontName(FpdfTextpageT textPage, int index, out int? fontFlags)
        {
            var flags = 0;
            var byteCount = fpdf_text.FPDFTextGetFontInfo(textPage, index, IntPtr.Zero, 0, ref flags);
            fontFlags = flags;
            if (byteCount <= 1)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)byteCount);
            try
            {
                _ = fpdf_text.FPDFTextGetFontInfo(textPage, index, buffer, byteCount, ref flags);
                fontFlags = flags;
                var data = new byte[(int)byteCount];
                Marshal.Copy(buffer, data, 0, data.Length);
                var terminator = Array.IndexOf(data, (byte)0);
                var length = terminator >= 0 ? terminator : data.Length;
                return length > 0 ? Encoding.UTF8.GetString(data, 0, length) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static unsafe string? GetFontString(FpdfFontT font, FontStringKind kind)
        {
            if (kind != FontStringKind.BaseName)
            {
                return null;
            }

            var byteCount = fpdf_edit.FPDFFontGetFontName(font, null, 0);
            if (byteCount <= 1)
            {
                return null;
            }

            var buffer = new sbyte[byteCount];
            fixed (sbyte* ptr = buffer)
            {
                var written = fpdf_edit.FPDFFontGetFontName(font, ptr, byteCount);
                if (written <= 1)
                {
                    return null;
                }

                var bytes = new byte[written];
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = unchecked((byte)buffer[i]);
                }

                var terminator = Array.IndexOf(bytes, (byte)0);
                var length = terminator >= 0 ? terminator : bytes.Length;
                return length > 0 ? Encoding.UTF8.GetString(bytes, 0, length) : null;
            }
        }

        private static string? GetRenderMode(FpdfPageobjectT? textObject)
        {
            if (PdfiumBackend.IsNull(textObject))
            {
                return null;
            }

            return fpdf_edit.FPDFTextObjGetTextRenderMode(textObject).ToString();
        }

        private static int? GetMarkedContentId(FpdfPageobjectT? pageObject)
        {
            return null;
        }

        private static string? GetColor(FpdfTextpageT textPage, int index, bool fill)
        {
            uint r = 0;
            uint g = 0;
            uint b = 0;
            uint a = 0;
            var success = fill
                ? fpdf_text.FPDFTextGetFillColor(textPage, index, ref r, ref g, ref b, ref a)
                : fpdf_text.FPDFTextGetStrokeColor(textPage, index, ref r, ref g, ref b, ref a);
            return success != 0 ? PdfNativeTextHeuristics.ToArgb(a, r, g, b) : null;
        }

        private static bool IsDotLeaderBreak(SegmentBuilder segment, PdfGlyph glyph, float gap)
        {
            var text = glyph.Text;
            if (text.Length == 0)
            {
                return false;
            }

            var ch = text[0];
            if (segment.PendingSpace)
            {
                return ch == '.' && segment.HasNonDotContent
                    || ch != '.' && !segment.HasNonDotContent && segment.CharacterCount >= 3;
            }

            return ch == '.' && segment.HasNonDotContent && gap > segment.AverageCharacterWidth * 0.4f;
        }

        private static void ApplyInvisibleTextPolicy(List<PdfTextItem> items)
        {
            var visibleLength = 0;
            var invisibleLength = 0;

            foreach (var item in items)
            {
                if (item.Layer == PdfTextLayerKind.InvisibleOcrLayer)
                {
                    invisibleLength += item.Text.Length;
                }
                else
                {
                    visibleLength += item.Text.Length;
                }
            }

            if (visibleLength == 0 || invisibleLength == 0)
            {
                return;
            }

            var invisibleRatio = (float)invisibleLength / (visibleLength + invisibleLength);
            if (invisibleRatio < 0.30f)
            {
                items.RemoveAll(static item => item.Layer == PdfTextLayerKind.InvisibleOcrLayer);
            }
        }


        private enum FontStringKind
        {
            BaseName,
            FamilyName
        }

        private sealed class SegmentBuilder
        {
            private readonly StringBuilder _text = new();
            private BoundingBox _box;
            private PdfFontInfo? _font;
            private bool _fontLooksCorrupt;
            private bool _hasUnicodeMapError;
            private int? _markedContentId;
            private string? _renderMode;
            private string? _fillColor;
            private string? _strokeColor;
            private float _rotation;
            private float _textWidth;
            private int _startIndex;
            private int _endIndex;
            private readonly Dictionary<string, object?> _metadata = new();

            public bool HasContent { get; private set; }
            public bool PendingSpace { get; private set; }
            public int CharacterCount { get; private set; }
            public PdfTextLayerKind Layer { get; private set; }
            public float Top => _box.Y;
            public float Bottom => _box.Bottom;
            public float Width => _box.Width;
            public float LastStrictRight { get; private set; }
            public float LastStrictBottom { get; private set; }
            public bool HasNonDotContent { get; private set; }
            public float AverageCharacterWidth => CharacterCount == 0
                ? 5f
                : (_textWidth > 0 ? _textWidth : _box.Width) / CharacterCount;

            public void Start(PdfGlyph glyph)
            {
                _text.Clear();
                _text.Append(glyph.Text);
                _box = glyph.LooseBox;
                LastStrictRight = glyph.StrictBox.Right;
                LastStrictBottom = glyph.StrictBox.Bottom;
                CharacterCount = glyph.Text.Length;
                HasContent = true;
                PendingSpace = false;
                HasNonDotContent = HasNonDot(glyph.Text);
                Layer = glyph.Layer;
                _font = glyph.Font;
                _fontLooksCorrupt = glyph.Font?.LooksCorrupt ?? false;
                _hasUnicodeMapError = glyph.HasUnicodeMapError;
                _markedContentId = glyph.MarkedContentId;
                _renderMode = glyph.RenderMode;
                _fillColor = glyph.FillColorArgb;
                _strokeColor = glyph.StrokeColorArgb;
                _rotation = glyph.Rotation;
                _textWidth = glyph.StrictBox.Width;
                _startIndex = glyph.Index;
                _endIndex = glyph.Index;
                _metadata.Clear();
                _metadata["backend"] = "PDFium";
                _metadata["sourceCharStart"] = _startIndex;
                _metadata["sourceCharEnd"] = _endIndex;
                _metadata["glyphCount"] = 1;
                _metadata["sourceKind"] = "text-run";
                foreach (var pair in glyph.Metadata)
                {
                    _metadata[pair.Key] = pair.Value;
                }
            }

            public void MarkPendingSpace() => PendingSpace = HasContent;

            public void CommitPendingSpace()
            {
                if (PendingSpace && _text.Length > 0 && _text[^1] != ' ')
                {
                    _text.Append(' ');
                }

                PendingSpace = false;
            }

            public void Append(PdfGlyph glyph)
            {
                _text.Append(glyph.Text);
                _box = Union(_box, glyph.LooseBox);
                LastStrictRight = glyph.StrictBox.Right;
                LastStrictBottom = glyph.StrictBox.Bottom;
                CharacterCount += glyph.Text.Length;
                HasNonDotContent |= HasNonDot(glyph.Text);
                _fontLooksCorrupt |= glyph.Font?.LooksCorrupt ?? false;
                _hasUnicodeMapError |= glyph.HasUnicodeMapError;
                _textWidth += glyph.StrictBox.Width;
                _endIndex = glyph.Index;
                _metadata["sourceCharEnd"] = _endIndex;
                _metadata["glyphCount"] = (int)_metadata["glyphCount"]! + 1;
            }

            public void Flush(List<PdfTextItem> items)
            {
                if (!HasContent)
                {
                    return;
                }

                var text = _text.ToString().Trim();
                if (text.Length > 0)
                {
                    items.Add(new PdfTextItem
                    {
                        Text = text,
                        BoundingBox = _box,
                        Rotation = _rotation,
                        Layer = Layer,
                        Font = WithLooksCorrupt(_font, _fontLooksCorrupt || PdfGarbledTextDetector.IsLikelyGarbled(text)),
                        TextWidth = _textWidth > 0 ? _textWidth : null,
                        HasUnicodeMapError = _hasUnicodeMapError,
                        Confidence = Layer == PdfTextLayerKind.Native ? 1f : null,
                        MarkedContentId = _markedContentId,
                        RenderMode = _renderMode,
                        FillColorArgb = _fillColor,
                        StrokeColorArgb = _strokeColor,
                        Metadata = new Dictionary<string, object?>(_metadata)
                    });
                }

                HasContent = false;
                PendingSpace = false;
                CharacterCount = 0;
                HasNonDotContent = false;
                _text.Clear();
                _metadata.Clear();
            }

            private static BoundingBox Union(BoundingBox a, BoundingBox b)
            {
                var left = Math.Min(a.X, b.X);
                var top = Math.Min(a.Y, b.Y);
                var right = Math.Max(a.Right, b.Right);
                var bottom = Math.Max(a.Bottom, b.Bottom);
                return new BoundingBox(left, top, right - left, bottom - top);
            }

            private static bool HasNonDot(string text)
            {
                for (var i = 0; i < text.Length; i++)
                {
                    if (text[i] is not ('.' or ' ' or '\u00B7' or '\u2022'))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static PdfFontInfo? WithLooksCorrupt(PdfFontInfo? font, bool looksCorrupt)
            {
                if (font is null)
                {
                    return looksCorrupt ? new PdfFontInfo { LooksCorrupt = true } : null;
                }

                if (font.LooksCorrupt == looksCorrupt)
                {
                    return font;
                }

                return new PdfFontInfo
                {
                    Name = font.Name,
                    BaseName = font.BaseName,
                    FamilyName = font.FamilyName,
                    Size = font.Size,
                    Height = font.Height,
                    Ascent = font.Ascent,
                    Descent = font.Descent,
                    Weight = font.Weight,
                    Flags = font.Flags,
                    IsEmbedded = font.IsEmbedded,
                    LooksCorrupt = looksCorrupt,
                    Metadata = new Dictionary<string, object?>(font.Metadata)
                };
            }
        }

        private sealed class PdfGlyph
        {
            public required string Text { get; init; }
            public uint Unicode { get; init; }
            public int Index { get; init; }
            public BoundingBox StrictBox { get; init; }
            public BoundingBox LooseBox { get; init; }
            public PdfTextLayerKind Layer { get; init; }
            public PdfFontInfo? Font { get; init; }
            public bool HasUnicodeMapError { get; init; }
            public int? MarkedContentId { get; init; }
            public string? RenderMode { get; init; }
            public string? FillColorArgb { get; init; }
            public string? StrokeColorArgb { get; init; }
            public float Rotation { get; init; }
            public bool IsLineBreak { get; init; }
            public bool IsSpace { get; init; }
            public Dictionary<string, object?> Metadata { get; init; } = new();

            public static PdfGlyph LineBreak(int index) => new()
            {
                Text = "\n",
                Index = index,
                IsLineBreak = true
            };

            public static PdfGlyph Space(int index) => new()
            {
                Text = " ",
                Index = index,
                IsSpace = true
            };
        }
    }
}
