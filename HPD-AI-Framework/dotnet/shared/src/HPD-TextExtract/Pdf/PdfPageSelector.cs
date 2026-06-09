namespace HPD.TextExtract.Pdf
{
    internal static class PdfPageSelector
    {
        public static HashSet<int>? Parse(string? targetPages)
        {
            if (string.IsNullOrWhiteSpace(targetPages))
            {
                return null;
            }

            var pages = new HashSet<int>();
            var parts = targetPages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var dash = part.IndexOf('-', StringComparison.Ordinal);
                if (dash >= 0)
                {
                    var start = int.Parse(part[..dash].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    var end = int.Parse(part[(dash + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    if (start > end)
                    {
                        throw new ArgumentException($"Invalid page range: {part}", nameof(targetPages));
                    }

                    for (var page = start; page <= end; page++)
                    {
                        pages.Add(page);
                    }
                }
                else
                {
                    pages.Add(int.Parse(part, System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            return pages;
        }
    }
}
