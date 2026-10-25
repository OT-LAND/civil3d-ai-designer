using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ExcelDataReader;

namespace CivilAI.Core.Survey
{
    /// <summary>
    /// Reads an Excel (.xlsx) tree table into a <see cref="CsvTable"/> (roadmap 2.1c, D12), so the
    /// same field mapping, unit questions and import apply as for CSV. Cell values are used as
    /// stored (numbers as numbers, formulas as their last calculated value); row numbers in
    /// messages are the Excel row numbers.
    /// </summary>
    public static class XlsxTable
    {
        // ExcelDataReader's configuration asks for code page 1252 (a legacy-format default) even for
        // .xlsx; .NET 8 ships that table but only after the provider is registered. Additive, process-wide.
        static XlsxTable() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        /// <summary>
        /// Reads one sheet. With <paramref name="sheetName"/> null: the only visible sheet that has
        /// content; if there are several, the result asks which one holds the trees (never guessed).
        /// </summary>
        public static XlsxReadResult Read(Stream stream, string? sheetName = null)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            var sheets = ReadSheets(stream);
            var withContent = sheets.Where(s => s.Visible && s.Records.Any(r => r.Cells.Any(c => c.Length > 0))).ToList();
            var names = withContent.Select(s => s.Name).ToList();

            Sheet? chosen;
            if (sheetName is not null)
            {
                chosen = sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (chosen is null)
                    return new XlsxReadResult(null, names, $"There is no sheet \"{sheetName}\" in the workbook. Which sheet holds the trees: {List(names)}?");
            }
            else if (withContent.Count == 0)
                throw new FormatException("The workbook has no sheet with content.");
            else if (withContent.Count > 1)
                return new XlsxReadResult(null, names, $"The workbook has several sheets. Which one holds the trees: {List(names)}?");
            else chosen = withContent[0];

            return new XlsxReadResult(CsvTable.FromRecords(chosen.Records, ',', allowDecimalComma: false), names, null);
        }

        private static string List(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"\"{n}\""));

        private sealed record Sheet(string Name, bool Visible, List<(int Line, List<string> Cells)> Records);

        private static List<Sheet> ReadSheets(Stream stream)
        {
            IExcelDataReader reader;
            try
            {
                reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
            }
            catch (Exception e) when (e is ExcelDataReader.Exceptions.HeaderException || e is InvalidDataException)
            {
                throw new FormatException("This file is not an Excel .xlsx workbook. Save it as .xlsx (or CSV) and try again.", e);
            }

            var sheets = new List<Sheet>();
            using (reader)
            {
                do
                {
                    var records = new List<(int, List<string>)>();
                    var row = 0;
                    while (reader.Read())
                    {
                        row++;
                        var cells = new List<string>(reader.FieldCount);
                        for (var i = 0; i < reader.FieldCount; i++) cells.Add(CellText(reader.GetValue(i)));
                        records.Add((row, cells));
                    }
                    sheets.Add(new Sheet(reader.Name ?? $"Sheet{sheets.Count + 1}",
                        string.IsNullOrEmpty(reader.VisibleState) || reader.VisibleState == "visible", records));
                }
                while (reader.NextResult());
            }
            return sheets;
        }

        /// <summary>Invariant text for a cell value, so <see cref="TreeTableImport.TryNumber"/> reads it back exactly.</summary>
        public static string CellText(object? value) => value switch
        {
            null => "",
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            DateTime t => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
    }

    /// <summary>Either a table, or a question for the designer (which sheet) with no table.</summary>
    public sealed class XlsxReadResult
    {
        public XlsxReadResult(CsvTable? table, IReadOnlyList<string> sheetsWithContent, string? question)
        {
            Table = table;
            SheetsWithContent = sheetsWithContent;
            Question = question;
        }

        public CsvTable? Table { get; }

        /// <summary>Visible sheets that have content, in workbook order (for a sheet picker).</summary>
        public IReadOnlyList<string> SheetsWithContent { get; }

        public string? Question { get; }
    }
}
