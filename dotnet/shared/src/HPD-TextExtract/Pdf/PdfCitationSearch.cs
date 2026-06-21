namespace HPD.TextExtract.Pdf
{
    public sealed class PdfCitationSearch
    {
        public IReadOnlyList<PdfTextMatch> Search(
            PdfExtractionResult result,
            string phrase,
            bool caseSensitive = false)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentException.ThrowIfNullOrWhiteSpace(phrase);

            var matches = new List<PdfTextMatch>();
            for (var i = 0; i < result.Pages.Count; i++)
            {
                SearchPage(result.Pages[i], phrase, caseSensitive, matches);
            }

            return matches;
        }

        private static void SearchPage(
            PdfPage page,
            string phrase,
            bool caseSensitive,
            List<PdfTextMatch> matches)
        {
            var items = page.TextItems;
            if (items.Count == 0)
            {
                return;
            }

            var query = Normalize(phrase, caseSensitive);
            var separators = BuildSeparators(items);
            var start = 0;

            while (start < items.Count)
            {
                var combined = string.Empty;
                var found = false;

                for (var end = start; end < items.Count; end++)
                {
                    if (end > start)
                    {
                        combined += separators[end];
                    }

                    combined += items[end].Text;

                    if (Normalize(combined, caseSensitive).Contains(query, StringComparison.Ordinal))
                    {
                        var narrowed = combined;
                        var first = start;
                        while (first < end)
                        {
                            var skip = items[first].Text.Length + separators[first + 1].Length;
                            var without = narrowed[skip..];
                            if (Normalize(without, caseSensitive).Contains(query, StringComparison.Ordinal))
                            {
                                narrowed = without;
                                first++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        var matched = items.Skip(first).Take(end - first + 1).ToArray();
                        matches.Add(new PdfTextMatch
                        {
                            PageNumber = page.Number,
                            Text = phrase,
                            BoundingBox = Union(matched),
                            Items = matched
                        });

                        start = end + 1;
                        found = true;
                        break;
                    }

                    if (combined.Length > query.Length * 2)
                    {
                        break;
                    }
                }

                if (!found)
                {
                    start++;
                }
            }
        }

        private static string Normalize(string value, bool caseSensitive) =>
            caseSensitive ? value : value.ToLowerInvariant();

        private static string[] BuildSeparators(IReadOnlyList<PdfTextItem> items)
        {
            var separators = new string[items.Count];
            separators[0] = string.Empty;

            for (var i = 1; i < items.Count; i++)
            {
                var previous = items[i - 1];
                var current = items[i];
                var fontSize = previous.Font?.Size ?? current.Font?.Size ?? 12f;
                var sameLine = Math.Abs(current.BoundingBox.Y - previous.BoundingBox.Y) < fontSize * 0.5f;
                var gap = current.BoundingBox.X - previous.BoundingBox.Right;
                separators[i] = sameLine && gap <= fontSize * 0.3f ? string.Empty : " ";
            }

            return separators;
        }

        private static HPD.TextExtract.Models.BoundingBox Union(IReadOnlyList<PdfTextItem> items)
        {
            var left = items.Min(static item => item.BoundingBox.X);
            var top = items.Min(static item => item.BoundingBox.Y);
            var right = items.Max(static item => item.BoundingBox.Right);
            var bottom = items.Max(static item => item.BoundingBox.Bottom);
            return new HPD.TextExtract.Models.BoundingBox(left, top, right - left, bottom - top);
        }
    }
}
