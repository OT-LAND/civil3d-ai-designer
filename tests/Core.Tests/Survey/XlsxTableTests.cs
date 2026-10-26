using System;
using System.IO;
using System.Linq;
using System.Text;
using CivilAI.Core.Survey;
using Xunit;

namespace CivilAI.Core.Tests.Survey
{
    public class XlsxTableTests
    {
        private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "samples", "survey", name);

        private static XlsxReadResult Read(string name, string? sheet = null)
        {
            using var stream = File.OpenRead(SamplePath(name));
            return XlsxTable.Read(stream, sheet);
        }

        [Fact]
        public void Several_sheets_ask_which_one_holds_the_trees()
        {
            var r = Read("trees-courtyard.xlsx");
            Assert.Null(r.Table);
            Assert.Equal(new[] { "Trees", "Notes" }, r.SheetsWithContent);
            Assert.Contains("\"Trees\", \"Notes\"", r.Question);
        }

        [Fact]
        public void Unknown_sheet_name_is_a_question()
        {
            var r = Read("trees-courtyard.xlsx", "Bäume");
            Assert.Null(r.Table);
            Assert.Contains("no sheet \"Bäume\"", r.Question);
        }

        [Fact]
        public void Chosen_sheet_imports_the_same_trees_as_the_csv()
        {
            var r = Read("trees-courtyard.xlsx", "trees"); // sheet name is case-insensitive
            Assert.NotNull(r.Table);
            Assert.Null(r.Question);
            Assert.False(r.Table!.AllowDecimalComma);

            var fromXlsx = Import(r.Table);
            var fromCsv = Import(CsvTable.Parse(File.ReadAllText(SamplePath("trees-courtyard.csv"))));

            Assert.Empty(fromXlsx.Problems);
            Assert.Equal(3, fromXlsx.Trees.Count);
            Assert.Equal(
                fromCsv.Trees.Select(t => (t.Id, t.Species, t.Location.X, t.Location.Y, t.TrunkDiameter, t.CanopyDiameter, t.Height, t.GroundLevel)),
                fromXlsx.Trees.Select(t => (t.Id, t.Species, t.Location.X, t.Location.Y, t.TrunkDiameter, t.CanopyDiameter, t.Height, t.GroundLevel)));
            Assert.Equal(2, r.Table.Rows[0].Line); // Excel row numbers in messages
        }

        private static TreeImportResult Import(CsvTable table)
        {
            var mapping = TreeFieldMapping.Suggest(table.Headers);
            return TreeTableImport.Import(table, mapping, TreeTableUnits.Suggest(mapping, coordinateSystem: "local"));
        }

        [Fact]
        public void Hebrew_sheet_hidden_sheet_ignored_blank_rows_skipped_units_still_asked()
        {
            var r = Read("trees-hebrew.xlsx");
            Assert.Equal(new[] { "עצים" }, r.SheetsWithContent); // hidden sheet "old" not offered
            var table = r.Table!;
            Assert.Equal(3, table.Rows[0].Line); // blank row 1, header on row 2

            var mapping = TreeFieldMapping.Suggest(table.Headers);
            Assert.Equal("מס' עץ", mapping.Columns[TreeField.Id]);
            Assert.Equal("קוטר גזע (ס\"מ)", mapping.Columns[TreeField.TrunkDiameter]);

            // X/Y headings carry no unit: asked, not assumed (D4b), even though the values look like ITM.
            var units = TreeTableUnits.Suggest(mapping, coordinateSystem: "ITM");
            var asked = TreeTableImport.Import(table, mapping, units);
            Assert.True(asked.NeedsAnswers);
            Assert.Empty(asked.Trees);

            var answered = TreeTableImport.Import(table, mapping, new TreeTableUnits
            {
                Coordinates = LengthUnit.Metre, TrunkDiameter = units.TrunkDiameter, CanopyDiameter = units.CanopyDiameter, CoordinateSystem = "ITM",
            });
            Assert.False(answered.NeedsAnswers);
            Assert.Equal(2, answered.Trees.Count);
            var oak = answered.Trees[0];
            Assert.Equal("12", oak.Id);
            Assert.Equal(180250.5, oak.Location.X, 6);
            Assert.Equal(0.55, oak.TrunkDiameter!.Value, 6); // "55" typed as text, cm → m (trunk = diameter, D12)
            Assert.Equal(10, oak.CanopyDiameter!.Value, 6);
            Assert.Null(answered.Trees[1].Species);
            Assert.Null(answered.Trees[1].TrunkDiameter);
        }

        [Fact]
        public void A_file_that_is_not_xlsx_is_a_clear_error()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Tree No.;X;Y\n1;2;3\n"));
            var e = Assert.Throws<FormatException>(() => XlsxTable.Read(stream));
            Assert.Contains("not an Excel .xlsx", e.Message);
        }

        [Fact]
        public void Cell_text_is_invariant_and_round_trips_numbers()
        {
            Assert.Equal("0.1", XlsxTable.CellText(0.1));
            Assert.Equal("-20.15", XlsxTable.CellText(-20.15));
            Assert.Equal("", XlsxTable.CellText(null));
            Assert.Equal("2026-10-24", XlsxTable.CellText(new DateTime(2026, 10, 24)));
        }
    }
}
