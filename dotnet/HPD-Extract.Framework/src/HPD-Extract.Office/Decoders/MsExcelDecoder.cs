using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using HPD.Extract.Extensions;
using HPD.Extract.Interfaces;
using HPD.Extract.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Extract.Decoders
{
    public sealed class MsExcelDecoder : IContentDecoder
    {
        private readonly ILogger<MsExcelDecoder> _log;

        public MsExcelDecoder(ILoggerFactory? loggerFactory = null)
        {
            _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MsExcelDecoder>();
        }

        public bool SupportsMimeType(string mimeType)
        {
            return mimeType != null &&
                   (mimeType.StartsWith(MimeTypes.MsExcelX, StringComparison.OrdinalIgnoreCase) ||
                    mimeType.StartsWith(MimeTypes.MsExcel, StringComparison.OrdinalIgnoreCase));
        }

        public async ValueTask<ExtractionResult> DecodeAsync(
            ContentInput input,
            ExtractionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _log.LogDebug("Extracting text from MS Excel file");

            var result = new FileContent(MimeTypes.PlainText);
            var pages = new List<ExtractedPage>();
            await using var data = await input.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            using var spreadsheetDocument = SpreadsheetDocument.Open(data, false);

            WorkbookPart? workbookPart = spreadsheetDocument.WorkbookPart;
            if (workbookPart is null)
            {
                throw new InvalidOperationException("The workbook part is missing.");
            }

            SharedStringTablePart? sharedStringTablePart = workbookPart.SharedStringTablePart;
            IEnumerable<Sheet>? sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>();

            if (sheets != null)
            {
                int sectionNumber = 1;
                foreach (Sheet sheet in sheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sb = new StringBuilder();
                    WorksheetPart? worksheetPart = workbookPart.GetPartById(sheet.Id!) as WorksheetPart;
                    if (worksheetPart != null)
                    {
                        SheetData? sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
                        if (sheetData != null)
                        {
                            var allRows = sheetData.Elements<Row>().ToList();
                            var lastRowWithContent = -1;

                            // Process rows and find last row with content
                            for (int rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
                            {
                                var rowContent = new StringBuilder();
                                Row row = allRows[rowIndex];
                                IEnumerable<Cell>? cells = row.Elements<Cell>();
                                bool rowHasContent = false;

                                if (cells != null)
                                {
                                    foreach (Cell cell in cells)
                                    {
                                        string cellValue = GetCellValue(cell, sharedStringTablePart);
                                        if (!string.IsNullOrWhiteSpace(cellValue))
                                        {
                                            rowHasContent = true;
                                            rowContent.Append(cellValue);
                                        }
                                        rowContent.Append('\t');
                                    }
                                }

                                if (rowHasContent)
                                {
                                    lastRowWithContent = rowIndex;
                                    sb.AppendLineNix(rowContent.ToString().TrimEnd('\t'));
                                }
                                else if (rowIndex < allRows.Count - 1) // Not the last row
                                {
                                    sb.AppendLineNix(); // Empty row
                                }
                            }

                            // Only add the section if there's actual content
                            string sheetContent = sb.ToString().NormalizeNewlines(false);
                            if (!string.IsNullOrWhiteSpace(sheetContent))
                            {
                                var pageNumber = sectionNumber;
                                result.Sections.Add(new Chunk(
                                    sheetContent,
                                    sectionNumber++,
                                    Chunk.Meta(sentencesAreComplete: true, pageNumber: pageNumber, source: TextExtractionSource.Native)
                                ));
                                pages.Add(new ExtractedPage
                                {
                                    Number = pageNumber,
                                    Text = sheetContent,
                                    Metadata = { ["sheetName"] = sheet.Name?.Value }
                                });
                            }
                        }
                    }
                }
            }

            return new ExtractionResult(result)
            {
                Pages = pages
            };
        }

        private static string GetCellValue(Cell cell, SharedStringTablePart? sharedStringTablePart)
        {
            if (cell.CellValue is null)
            {
                return string.Empty;
            }

            string value = cell.CellValue.InnerText;

            // If the cell contains a shared string, look it up in the shared string table
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                if (sharedStringTablePart != null && int.TryParse(value, out int sharedStringIndex))
                {
                    SharedStringItem? sharedStringItem = sharedStringTablePart.SharedStringTable
                        .Elements<SharedStringItem>()
                        .ElementAtOrDefault(sharedStringIndex);

                    if (sharedStringItem != null)
                    {
                        value = sharedStringItem.InnerText;
                    }
                }
            }

            return value;
        }
    }
}
