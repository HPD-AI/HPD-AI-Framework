using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using HPD.Agent.TextExtraction;
using HPD.Agent.TextExtraction.Models;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace HPD.Agent.Tests.Content;

public sealed class TextExtractionFixtureMatrixTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        "hpd-text-extraction-fixtures",
        Guid.NewGuid().ToString("N"));

    public TextExtractionFixtureMatrixTests()
    {
        Directory.CreateDirectory(_fixtureRoot);
    }

    private static readonly (string FileName, string MimeType, string ExpectedText)[] s_fileFixtures =
    [
        ("plain.txt", MimeTypes.PlainText, "Plain fixture sentinel"),
        ("notes.md", MimeTypes.MarkDown, "Markdown fixture sentinel"),
        ("payload.json", MimeTypes.Json, "Json fixture sentinel"),
        ("page.html", MimeTypes.Html, "Html fixture sentinel"),
        ("document.docx", MimeTypes.MsWordX, "Word fixture sentinel"),
        ("workbook.xlsx", MimeTypes.MsExcelX, "Excel fixture sentinel"),
        ("slides.pptx", MimeTypes.MsPowerPointX, "PowerPoint fixture sentinel"),
        ("sample.pdf", MimeTypes.Pdf, "PDF fixture sentinel")
    ];

    public static TheoryData<string, string, string> FileFixtures
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var fixture in s_fileFixtures)
            {
                data.Add(fixture.FileName, fixture.MimeType, fixture.ExpectedText);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FileFixtures))]
    public async Task ExtractTextAsync_FilePath_CoversRegisteredDocumentFixtureMatrix(
        string fileName,
        string expectedMimeType,
        string expectedText)
    {
        var path = Path.Combine(_fixtureRoot, fileName);
        await WriteFixtureAsync(path, expectedText);

        using var utility = new TextExtractionUtility();
        var result = await utility.ExtractTextAsync(path);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(fileName, result.FileName);
        Assert.Equal(path, result.FilePath);
        Assert.Equal(expectedMimeType, result.MimeType);
        Assert.Contains(expectedText, result.ExtractedText, StringComparison.Ordinal);
        Assert.NotNull(result.OriginalFileContent);
        Assert.NotEmpty(result.OriginalFileContent.Sections);
    }

    [Theory]
    [MemberData(nameof(FileFixtures))]
    public async Task ExtractTextAsync_BinaryData_CoversRegisteredDocumentFixtureMatrix(
        string fileName,
        string mimeType,
        string expectedText)
    {
        var path = Path.Combine(_fixtureRoot, fileName);
        await WriteFixtureAsync(path, expectedText);
        var data = await File.ReadAllBytesAsync(path);

        using var utility = new TextExtractionUtility();
        var result = await utility.ExtractTextAsync(data, mimeType, fileName);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(fileName, result.FileName);
        Assert.Equal("binary-data", result.FilePath);
        Assert.Equal(mimeType, result.MimeType);
        Assert.Contains(expectedText, result.ExtractedText, StringComparison.Ordinal);
    }

    [Fact]
    public void DecoderFactory_Defaults_RegistersEveryFixtureMimeType()
    {
        var factory = new DecoderFactory();

        foreach (var fixture in s_fileFixtures)
        {
            var decoder = factory.GetDecoder(fixture.MimeType);

            Assert.NotNull(decoder);
            Assert.True(decoder.SupportsMimeType(fixture.MimeType));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureRoot))
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }
    }

    private static Task WriteFixtureAsync(string path, string sentinel)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" => File.WriteAllTextAsync(path, sentinel, Encoding.UTF8),
            ".md" => File.WriteAllTextAsync(path, $"# Matrix\n\n{sentinel}\n", Encoding.UTF8),
            ".json" => File.WriteAllTextAsync(path, $$"""{ "message": "{{sentinel}}" }""", Encoding.UTF8),
            ".html" => File.WriteAllTextAsync(path, $"<html><body><h1>{sentinel}</h1></body></html>", Encoding.UTF8),
            ".docx" => Task.Run(() => WriteWordFixture(path, sentinel)),
            ".xlsx" => Task.Run(() => WriteExcelFixture(path, sentinel)),
            ".pptx" => Task.Run(() => WritePowerPointFixture(path, sentinel)),
            ".pdf" => File.WriteAllBytesAsync(path, CreatePdfFixture(sentinel)),
            _ => throw new NotSupportedException(path)
        };
    }

    private static void WriteWordFixture(string path, string sentinel)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new W.Document(
            new W.Body(
                new W.Paragraph(
                    new W.Run(
                        new W.Text(sentinel)))));
        mainPart.Document.Save();
    }

    private static void WriteExcelFixture(string path, string sentinel)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new S.Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new S.Worksheet(
            new S.SheetData(
                new S.Row(
                    new S.Cell
                    {
                        DataType = S.CellValues.String,
                        CellValue = new S.CellValue(sentinel)
                    })));

        workbookPart.Workbook.AppendChild(new S.Sheets(
            new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Matrix"
            }));
        workbookPart.Workbook.Save();
    }

    private static void WritePowerPointFixture(string path, string sentinel)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = document.AddPresentationPart();
        presentationPart.Presentation = new P.Presentation();

        var slidePart = presentationPart.AddNewPart<SlidePart>("rId1");
        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "Root" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    new P.Shape(
                        new P.NonVisualShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 2, Name = "TextBox" },
                            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.ShapeProperties(),
                        new P.TextBody(
                            new A.BodyProperties(),
                            new A.ListStyle(),
                            new A.Paragraph(new A.Run(new A.Text(sentinel))))))));
        slidePart.Slide.Save();

        presentationPart.Presentation.SlideIdList = new P.SlideIdList(
            new P.SlideId
            {
                Id = 256,
                RelationshipId = "rId1"
            });
        presentationPart.Presentation.Save();
    }

    private static byte[] CreatePdfFixture(string sentinel)
    {
        static string EscapePdfText(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {44 + EscapePdfText(sentinel).Length} >>\nstream\nBT /F1 24 Tf 72 720 Td ({EscapePdfText(sentinel)}) Tj ET\nendstream"
        };

        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n");
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
