#:project ../../HPD-Extract.Framework/src/HPD-Extract/HPD-Extract.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Extract.Decoders;
using HPD.Extract.Models;

if (args.Length < 2 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Usage: dotnet run --file extract-pdf-sections.cs -- <input.pdf> <output-dir> [--raw]");
    return args.Length < 2 ? 1 : 0;
}

var inputPdf = Path.GetFullPath(args[0]);
var outputDir = Path.GetFullPath(args[1]);
var writeRaw = args.Contains("--raw", StringComparer.OrdinalIgnoreCase);

if (!File.Exists(inputPdf))
{
    Console.Error.WriteLine($"Input PDF was not found: {inputPdf}");
    return 2;
}

Directory.CreateDirectory(outputDir);

var decoder = new PdfDecoder();
var extraction = await decoder.DecodeAsync(
    ContentInput.FromPath(inputPdf, MimeTypes.Pdf),
    new ExtractionOptions
    {
        OcrEnabled = false,
        IncludeTextItems = false,
        IncludeScreenshots = false,
        IncludeEmbeddedImages = false
    });

var pageTexts = extraction.Pages
    .OrderBy(page => page.Number)
    .Select(page => new PageText(page.Number, NormalizePageText(page.Text)))
    .ToArray();

var fullText = string.Join("\n\n", pageTexts.Select(page => page.Text));
if (writeRaw)
{
    await File.WriteAllTextAsync(Path.Combine(outputDir, "_raw-extraction.txt"), fullText, Encoding.UTF8);
}

var sections = SectionDetector.Detect(pageTexts);
if (sections.Count == 0)
{
    sections.Add(new Section("document", "Document", pageTexts.FirstOrDefault()?.Number ?? 1, fullText));
}

var written = new List<(string Title, int StartPage, string FileName, int CharacterCount)>();
var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var sequence = 1;
foreach (var section in sections)
{
    var safeName = UniqueFileName($"{sequence:00}-{Slugify(section.Title)}.md", usedNames);
    var path = Path.Combine(outputDir, safeName);
    var markdown = new StringBuilder();
    markdown.AppendLine(CleanHeading(section.Title));
    markdown.AppendLine();
    markdown.AppendLine($"Source: `{Path.GetFileName(inputPdf)}`");
    markdown.AppendLine($"Start page: {section.StartPage.ToString(CultureInfo.InvariantCulture)}");
    markdown.AppendLine();
    markdown.AppendLine("---");
    markdown.AppendLine();
    markdown.AppendLine(section.Markdown.Trim());
    markdown.AppendLine();

    await File.WriteAllTextAsync(path, markdown.ToString(), Encoding.UTF8);
    written.Add((section.Title, section.StartPage, safeName, section.Markdown.Length));
    sequence++;
}

await WriteIndexAsync(outputDir, inputPdf, written);

Console.WriteLine($"Extracted {pageTexts.Length} pages into {written.Count} Markdown section files.");
Console.WriteLine($"Output: {outputDir}");
foreach (var item in written)
{
    Console.WriteLine($"- {item.FileName} (page {item.StartPage}, {item.CharacterCount} chars)");
}

return 0;

static string NormalizePageText(string text)
{
    text = text.Replace("\r\n", "\n").Replace('\r', '\n');
    text = Regex.Replace(text, @"[ \t]+\n", "\n");
    text = Regex.Replace(text, @"\n{3,}", "\n\n");
    return text.Trim();
}

static string Slugify(string value)
{
    var lower = value.Trim().ToLowerInvariant();
    var chars = new StringBuilder(lower.Length);
    foreach (var ch in lower)
    {
        if (char.IsAsciiLetterOrDigit(ch))
        {
            chars.Append(ch);
        }
        else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '/' or ':' or '.')
        {
            chars.Append('-');
        }
    }

    var slug = Regex.Replace(chars.ToString(), "-{2,}", "-").Trim('-');
    return string.IsNullOrWhiteSpace(slug) ? "section" : slug;
}

static string UniqueFileName(string fileName, HashSet<string> usedNames)
{
    if (usedNames.Add(fileName))
    {
        return fileName;
    }

    var stem = Path.GetFileNameWithoutExtension(fileName);
    var extension = Path.GetExtension(fileName);
    for (var i = 2; ; i++)
    {
        var candidate = $"{stem}-{i}{extension}";
        if (usedNames.Add(candidate))
        {
            return candidate;
        }
    }
}

static string CleanHeading(string title)
{
    var clean = Regex.Replace(title.Trim(), @"\s+", " ");
    return clean.StartsWith("# ", StringComparison.Ordinal) ? clean : $"# {clean}";
}

static async Task WriteIndexAsync(
    string outputDir,
    string inputPdf,
    IReadOnlyList<(string Title, int StartPage, string FileName, int CharacterCount)> written)
{
    var index = new StringBuilder();
    index.AppendLine("# Extracted Sections");
    index.AppendLine();
    index.AppendLine($"Source: `{inputPdf}`");
    index.AppendLine();
    foreach (var item in written)
    {
        index.AppendLine($"- [{item.Title}]({item.FileName}) - page {item.StartPage}, {item.CharacterCount} chars");
    }

    await File.WriteAllTextAsync(Path.Combine(outputDir, "index.md"), index.ToString(), Encoding.UTF8);
}

sealed record PageText(int Number, string Text);

sealed record Section(string Id, string Title, int StartPage, string Markdown);

static partial class SectionDetector
{
    private static readonly Regex ChapterLine = ChapterRegex();
    private static readonly Regex PartLine = PartRegex();
    private static readonly Regex TopNumberedLine = TopNumberedRegex();
    private static readonly Regex ArticleLine = ArticleRegex();
    private static readonly Regex TocNoiseLine = TocNoiseRegex();

    public static List<Section> Detect(IReadOnlyList<PageText> pages)
    {
        var articleHeadings = FindArticleHeadingCandidates(pages);
        if (articleHeadings.Count >= 2)
        {
            return BuildSections(pages, articleHeadings);
        }

        var candidates = FindHeadingCandidates(pages);
        var selected = SelectMainHeadings(candidates);

        if (selected.Count == 0)
        {
            selected = SelectFallbackHeadings(candidates);
        }

        return BuildSections(pages, selected);
    }

    private static List<HeadingCandidate> FindArticleHeadingCandidates(IReadOnlyList<PageText> pages)
    {
        var result = new List<HeadingCandidate>();
        foreach (var page in pages)
        {
            var lines = page.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!ArticleLine.IsMatch(Collapse(lines[i])))
                {
                    continue;
                }

                var titleLines = new List<string>();
                for (var j = i - 1; j >= 0 && titleLines.Count < 4; j--)
                {
                    var line = Collapse(lines[j]);
                    if (line.Length == 0)
                    {
                        if (titleLines.Count > 0)
                        {
                            break;
                        }

                        continue;
                    }

                    if (TocNoiseLine.IsMatch(line) || line.StartsWith("<!--", StringComparison.Ordinal))
                    {
                        break;
                    }

                    titleLines.Add(line);
                }

                titleLines.Reverse();
                var title = Collapse(string.Join(' ', titleLines));
                if (title.Length is < 4 or > 180)
                {
                    continue;
                }

                result.Add(new HeadingCandidate(title, page.Number, Math.Max(0, i - titleLines.Count), 1));
            }
        }

        return result
            .GroupBy(candidate => NormalizeTitle(candidate.Title), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.PageNumber)
            .ThenBy(candidate => candidate.LineIndex)
            .ToList();
    }

    private static List<HeadingCandidate> FindHeadingCandidates(IReadOnlyList<PageText> pages)
    {
        var result = new List<HeadingCandidate>();
        foreach (var page in pages)
        {
            var lines = page.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = Collapse(lines[i]);
                if (!LooksLikeHeading(line))
                {
                    continue;
                }

                var title = line;
                var level = 2;
                if (ChapterLine.IsMatch(line) && i + 1 < lines.Length)
                {
                    var next = Collapse(lines[i + 1]);
                    if (next.Length is > 3 and < 120 && !TocNoiseLine.IsMatch(next))
                    {
                        title = $"{line}: {next}";
                        level = 1;
                    }
                }
                else if (PartLine.IsMatch(line))
                {
                    level = 1;
                }
                else if (TopNumberedLine.IsMatch(line))
                {
                    level = 1;
                }

                result.Add(new HeadingCandidate(title, page.Number, i, level));
            }
        }

        return result;
    }

    private static List<HeadingCandidate> SelectMainHeadings(List<HeadingCandidate> candidates)
    {
        var mains = candidates
            .Where(candidate => candidate.Level == 1)
            .Where(candidate => candidate.PageNumber > 2)
            .GroupBy(candidate => NormalizeTitle(candidate.Title), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.PageNumber)
            .ThenBy(candidate => candidate.LineIndex)
            .ToList();

        return mains.Count >= 2 ? mains : new List<HeadingCandidate>();
    }

    private static List<HeadingCandidate> SelectFallbackHeadings(List<HeadingCandidate> candidates)
    {
        return candidates
            .Where(candidate => candidate.PageNumber > 2)
            .Where(candidate => candidate.Title.Length is >= 4 and <= 90)
            .Where(candidate => !TocNoiseLine.IsMatch(candidate.Title))
            .GroupBy(candidate => NormalizeTitle(candidate.Title), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.PageNumber)
            .ThenBy(candidate => candidate.LineIndex)
            .Take(40)
            .ToList();
    }

    private static List<Section> BuildSections(IReadOnlyList<PageText> pages, IReadOnlyList<HeadingCandidate> headings)
    {
        if (headings.Count == 0)
        {
            return new List<Section>();
        }

        var sections = new List<Section>();
        for (var i = 0; i < headings.Count; i++)
        {
            var current = headings[i];
            var next = i + 1 < headings.Count ? headings[i + 1] : null;
            var builder = new StringBuilder();

            foreach (var page in pages)
            {
                if (page.Number < current.PageNumber)
                {
                    continue;
                }

                if (next is not null && page.Number > next.PageNumber)
                {
                    break;
                }

                var pageText = page.Text;
                if (page.Number == current.PageNumber)
                {
                    pageText = SlicePageText(pageText, current.LineIndex, null);
                }

                if (next is not null && page.Number == next.PageNumber)
                {
                    pageText = SlicePageText(pageText, null, next.LineIndex);
                }

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    continue;
                }

                builder.AppendLine($"<!-- page {page.Number.ToString(CultureInfo.InvariantCulture)} -->");
                builder.AppendLine();
                builder.AppendLine(FormatMarkdown(pageText));
                builder.AppendLine();
            }

            var markdown = builder.ToString().Trim();
            if (markdown.Length > 0)
            {
                sections.Add(new Section(NormalizeTitle(current.Title), current.Title, current.PageNumber, markdown));
            }
        }

        return sections;
    }

    private static string SlicePageText(string text, int? startLine, int? endLine)
    {
        var lines = text.Split('\n');
        var start = Math.Clamp(startLine ?? 0, 0, lines.Length);
        var end = Math.Clamp(endLine ?? lines.Length, start, lines.Length);
        return string.Join('\n', lines[start..end]);
    }

    private static string FormatMarkdown(string text)
    {
        var lines = text.Split('\n');
        var builder = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = Collapse(raw);
            if (line.Length == 0)
            {
                builder.AppendLine();
                continue;
            }

            if (LooksLikeHeading(line))
            {
                builder.AppendLine($"## {line}");
            }
            else
            {
                builder.AppendLine(line);
            }
        }

        return Regex.Replace(builder.ToString(), @"\n{3,}", "\n\n").Trim();
    }

    private static bool LooksLikeHeading(string line)
    {
        if (line.Length is < 3 or > 140)
        {
            return false;
        }

        if (TocNoiseLine.IsMatch(line))
        {
            return false;
        }

        if (ChapterLine.IsMatch(line) || PartLine.IsMatch(line) || TopNumberedLine.IsMatch(line))
        {
            return true;
        }

        var letters = line.Count(char.IsLetter);
        if (letters < 3)
        {
            return false;
        }

        var lower = line.Count(char.IsLower);
        var upper = line.Count(char.IsUpper);
        return lower == 0 && upper >= 3;
    }

    private static string Collapse(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    private static string NormalizeTitle(string title)
    {
        title = Collapse(title).ToLowerInvariant();
        title = Regex.Replace(title, @"[^a-z0-9]+", "-").Trim('-');
        return title;
    }

    [GeneratedRegex(@"^(chapter|ch\.?)\s+\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterRegex();

    [GeneratedRegex(@"^part\s+\d+\b|^appendix\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartRegex();

    [GeneratedRegex(@"^\d+\s+[A-Z][A-Za-z0-9 .,'’()/-]{3,}$", RegexOptions.CultureInvariant)]
    private static partial Regex TopNumberedRegex();

    [GeneratedRegex(@"^Article\s+•\s+\d{2}/\d{2}/\d{4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArticleRegex();

    [GeneratedRegex(@"\.{3,}\s*\d+$|^\d+$|^Page\s+\d+\b|^©|^Copyright|^Contents$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TocNoiseRegex();

    private sealed record HeadingCandidate(string Title, int PageNumber, int LineIndex, int Level);
}
