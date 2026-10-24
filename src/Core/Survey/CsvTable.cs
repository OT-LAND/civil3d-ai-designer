using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CivilAI.Core.Survey
{
    /// <summary>
    /// A plain table read from CSV text: a header row and data rows. Surveyors export with
    /// commas, semicolons (European/Israeli Excel) or tabs; the separator is detected from the
    /// header line. Quoted cells ("a, b" and "" escapes, line breaks inside quotes) are supported.
    /// Excel sheets are read into the same table by <see cref="XlsxTable"/> (D12), so field mapping
    /// and import work the same for both.
    /// </summary>
    public sealed class CsvTable
    {
        private CsvTable(char separator, IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows, bool allowDecimalComma)
        {
            Separator = separator;
            Headers = headers;
            Rows = rows;
            AllowDecimalComma = allowDecimalComma;
        }

        /// <summary>Cell separator of the CSV file; ',' for a table read from Excel.</summary>
        public char Separator { get; }

        /// <summary>
        /// Whether a lone comma in a number cell is a decimal comma ("12,5"). True for CSV files that
        /// use ';' or tab; false for comma CSV and for Excel (numbers come from the cell values).
        /// </summary>
        public bool AllowDecimalComma { get; }

        public IReadOnlyList<string> Headers { get; }

        public IReadOnlyList<CsvRow> Rows { get; }

        /// <summary>Index of a header (exact text, trimmed, case-insensitive), or -1.</summary>
        public int IndexOf(string header)
        {
            for (var i = 0; i < Headers.Count; i++)
                if (string.Equals(Headers[i], header.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static CsvTable Parse(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);

            var separator = DetectSeparator(text);
            var records = ReadRecords(text, separator);
            return FromRecords(records, separator, allowDecimalComma: separator != ',');
        }

        /// <summary>Builds a table from raw records (line/row number + cells): first non-blank record is the header.</summary>
        internal static CsvTable FromRecords(List<(int Line, List<string> Cells)> records, char separator, bool allowDecimalComma)
        {
            var headerIndex = records.FindIndex(r => !IsBlank(r.Cells));
            if (headerIndex < 0) throw new FormatException("The table is empty: no header row found.");

            var headers = records[headerIndex].Cells.Select(c => c.Trim()).ToList();
            var rows = new List<CsvRow>();
            for (var i = headerIndex + 1; i < records.Count; i++)
            {
                if (IsBlank(records[i].Cells)) continue;
                rows.Add(new CsvRow(records[i].Line, records[i].Cells.Select(c => c.Trim()).ToList()));
            }
            return new CsvTable(separator, headers, rows, allowDecimalComma);
        }

        private static bool IsBlank(List<string> cells) => cells.All(string.IsNullOrWhiteSpace);

        private static char DetectSeparator(string text)
        {
            // Count candidates in the first non-empty line, outside quotes.
            var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
            var inQuotes = false;
            var seenContent = false;
            var prev = '\n';
            foreach (var ch in text)
            {
                // Like the reader: a quote opens a quoted cell only at the start of a cell
                // (a quote inside a header such as ס"מ is literal).
                var atCellStart = prev == '\n' || prev == '\r' || counts.ContainsKey(prev);
                prev = ch;
                if (ch == '"' && (inQuotes || atCellStart)) inQuotes = !inQuotes;
                else if (!inQuotes && (ch == '\n' || ch == '\r'))
                {
                    if (seenContent) break;
                    continue;
                }
                else if (!inQuotes && counts.ContainsKey(ch)) counts[ch]++;
                if (!char.IsWhiteSpace(ch)) seenContent = true;
            }
            // Ties prefer tab, then semicolon, then comma (a decimal comma never beats a real separator).
            return new[] { '\t', ';', ',' }.OrderByDescending(c => counts[c]).First();
        }

        private static List<(int Line, List<string> Cells)> ReadRecords(string text, char separator)
        {
            var records = new List<(int, List<string>)>();
            var cells = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;
            var line = 1;
            var recordLine = 1;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else
                    {
                        if (ch == '\n') line++;
                        cell.Append(ch);
                    }
                    continue;
                }

                if (ch == '"' && cell.ToString().Trim().Length == 0) { cell.Clear(); inQuotes = true; }
                else if (ch == separator) { cells.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    cells.Add(cell.ToString()); cell.Clear();
                    records.Add((recordLine, cells));
                    cells = new List<string>();
                    line++;
                    recordLine = line;
                }
                else cell.Append(ch);
            }
            if (inQuotes) throw new FormatException($"Line {recordLine}: a quoted cell is never closed.");
            if (cell.Length > 0 || cells.Count > 0)
            {
                cells.Add(cell.ToString());
                records.Add((recordLine, cells));
            }
            return records;
        }
    }

    /// <summary>One data row; <see cref="Line"/> is the 1-based line in the file, for messages.</summary>
    public sealed class CsvRow
    {
        public CsvRow(int line, IReadOnlyList<string> cells)
        {
            Line = line;
            Cells = cells;
        }

        public int Line { get; }

        public IReadOnlyList<string> Cells { get; }

        /// <summary>Cell at a column index, or "" when the row is shorter.</summary>
        public string this[int index] => index >= 0 && index < Cells.Count ? Cells[index] : "";
    }
}
