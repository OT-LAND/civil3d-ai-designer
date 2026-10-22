using System;
using System.IO;
using System.Linq;
using CivilAI.Core.Survey;
using Xunit;

namespace CivilAI.Core.Tests.Survey
{
    public class CsvTableTests
    {
        [Fact]
        public void Detects_semicolon_and_reads_quoted_cells()
        {
            var t = CsvTable.Parse("﻿a;b;c\r\n1;\"x; \"\"y\"\"\";3\r\n\r\n4;5\r\n");
            Assert.Equal(';', t.Separator);
            Assert.Equal(new[] { "a", "b", "c" }, t.Headers);
            Assert.Equal(2, t.Rows.Count);
            Assert.Equal("x; \"y\"", t.Rows[0][1]);
            Assert.Equal("", t.Rows[1][2]); // short row
            Assert.Equal(4, t.Rows[1].Line); // blank line skipped, line numbers kept
        }

        [Fact]
        public void Detects_comma_and_tab()
        {
            Assert.Equal(',', CsvTable.Parse("id,x,y\n1,2,3").Separator);
            Assert.Equal('\t', CsvTable.Parse("id\tx\ty\n1\t2,5\t3").Separator);
        }

        [Fact]
        public void Line_break_inside_quotes_stays_in_the_cell()
        {
            var t = CsvTable.Parse("id,note\n1,\"two\nlines\"\n2,ok");
            Assert.Equal("two\nlines", t.Rows[0][1]);
            Assert.Equal(4, t.Rows[1].Line);
        }

        [Fact]
        public void Unclosed_quote_and_empty_table_are_errors()
        {
            Assert.Throws<FormatException>(() => CsvTable.Parse("a,b\n1,\"open"));
            Assert.Throws<FormatException>(() => CsvTable.Parse("\n \n"));
        }
    }

    public class TreeTableImportTests
    {
        private static string Sample() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "survey", "trees-courtyard.csv"));

        [Fact]
        public void Sample_imports_with_suggested_mapping_and_header_units()
        {
            var table = CsvTable.Parse(Sample());
            var mapping = TreeFieldMapping.Suggest(table.Headers);
            Assert.Equal("Tree No.", mapping.Columns[TreeField.Id]);
            Assert.Equal("X (m)", mapping.Columns[TreeField.X]);
            Assert.Equal("Z (m)", mapping.Columns[TreeField.GroundLevel]);

            var units = TreeTableUnits.Suggest(mapping, coordinateSystem: "local");
            Assert.Equal(LengthUnit.Centimetre, units.TrunkDiameter);

            var result = TreeTableImport.Import(table, mapping, units);
            Assert.False(result.NeedsAnswers, string.Join(" | ", result.Questions));
            Assert.Empty(result.Problems);
            Assert.Equal(3, result.Trees.Count);

            var ficus = result.Trees[0];
            Assert.Equal("1", ficus.Id);
            Assert.Equal("Ficus microcarpa", ficus.Species);
            Assert.Equal(-5.40, ficus.Location.X, 6); // decimal comma
            Assert.Equal(0.45, ficus.TrunkDiameter!.Value, 6); // cm → m
            Assert.Equal(8.0, ficus.CanopyDiameter!.Value, 6);
            Assert.Equal(10.20, ficus.GroundLevel!.Value, 6);
            Assert.Equal("Ceratonia siliqua; multi-stem", result.Trees[2].Species);
        }

        [Fact]
        public void Hebrew_headers_are_recognised()
        {
            var table = CsvTable.Parse("מס' עץ,מין,X,Y,קוטר גזע (ס\"מ),קוטר נוף (מ)\n7,זית,1,2,40,6");
            var mapping = TreeFieldMapping.Suggest(table.Headers);
            Assert.Equal(TreeField.Id, mapping.Columns.Single(c => c.Value == "מס' עץ").Key);
            Assert.Equal("מין", mapping.Columns[TreeField.Species]);
            Assert.Equal("קוטר גזע (ס\"מ)", mapping.Columns[TreeField.TrunkDiameter]);
            var units = TreeTableUnits.Suggest(mapping);
            Assert.Equal(LengthUnit.Centimetre, units.TrunkDiameter);
            Assert.Equal(LengthUnit.Metre, units.CanopyDiameter);
        }

        [Fact]
        public void Unknown_units_and_coordinate_system_are_asked_not_assumed()
        {
            var table = CsvTable.Parse("id,x,y,trunk\n1,10,20,30");
            var mapping = TreeFieldMapping.Suggest(table.Headers);
            var result = TreeTableImport.Import(table, mapping, TreeTableUnits.Suggest(mapping));

            Assert.True(result.NeedsAnswers);
            Assert.Empty(result.Trees);
            Assert.Contains(result.Questions, q => q.Contains("X/Y coordinates"));
            Assert.Contains(result.Questions, q => q.Contains("trunk diameter"));
            Assert.Contains(result.Questions, q => q.Contains("coordinate system"));
            Assert.DoesNotContain(result.Questions, q => q.Contains("canopy")); // not in the table, not asked
        }

        [Fact]
        public void Missing_position_columns_are_asked()
        {
            var table = CsvTable.Parse("id,species\n1,Olea");
            var mapping = TreeFieldMapping.Suggest(table.Headers);
            var result = TreeTableImport.Import(table, mapping, new TreeTableUnits { CoordinateSystem = "ITM" });
            Assert.Contains(result.Questions, q => q.Contains("X (easting)"));
            Assert.Contains(result.Questions, q => q.Contains("Y (northing)"));
        }

        [Fact]
        public void Mapped_column_not_in_table_is_asked()
        {
            var table = CsvTable.Parse("E,N\n1,2");
            var mapping = new TreeFieldMapping();
            mapping.Columns[TreeField.X] = "Easting";
            mapping.Columns[TreeField.Y] = "N";
            var result = TreeTableImport.Import(table, mapping, new TreeTableUnits { Coordinates = LengthUnit.Metre, CoordinateSystem = "ITM" });
            Assert.Single(result.Questions);
            Assert.Contains("\"Easting\"", result.Questions[0]);
        }

        [Fact]
        public void Bad_rows_are_reported_and_good_rows_kept()
        {
            var csv = "id,species,x,y,trunk,canopy\n" +
                      "1,Olea,10,20,30,5\n" +
                      "2,Pinus,,20,30,5\n" +       // no X → skipped
                      "3,Ficus,11,21,abc,-2\n" +   // bad trunk, negative canopy → values dropped
                      "1,Olea,12,22,900,5\n";      // duplicate id; 9 m trunk looks like wrong unit
            var table = CsvTable.Parse(csv);
            var mapping = TreeFieldMapping.Suggest(table.Headers);
            var units = new TreeTableUnits
            {
                Coordinates = LengthUnit.Metre,
                TrunkDiameter = LengthUnit.Centimetre,
                CanopyDiameter = LengthUnit.Metre,
                CoordinateSystem = "ITM",
            };
            var result = TreeTableImport.Import(table, mapping, units);

            Assert.Equal(new[] { "1", "3", "1" }, result.Trees.Select(t => t.Id));
            Assert.Null(result.Trees[1].TrunkDiameter);
            Assert.Null(result.Trees[1].CanopyDiameter);
            Assert.Equal(9.0, result.Trees[2].TrunkDiameter!.Value, 6);

            Assert.Contains(result.Problems, p => p.Line == 3 && p.RowSkipped);
            Assert.Contains(result.Problems, p => p.Line == 4 && p.Message.Contains("not a number"));
            Assert.Contains(result.Problems, p => p.Line == 4 && p.Message.Contains("not a positive size"));
            Assert.Contains(result.Problems, p => p.Line == 5 && p.Message.Contains("more than once"));
            Assert.Contains(result.Problems, p => p.Line == 5 && p.Message.Contains("unit right"));
        }

        [Fact]
        public void Coordinates_in_millimetres_are_converted_and_missing_id_uses_the_row()
        {
            var table = CsvTable.Parse("x;y\n12500;-3000");
            var mapping = TreeFieldMapping.Suggest(table.Headers);
            var result = TreeTableImport.Import(table, mapping, new TreeTableUnits { Coordinates = LengthUnit.Millimetre, CoordinateSystem = "local" });
            var tree = Assert.Single(result.Trees);
            Assert.Equal(12.5, tree.Location.X, 6);
            Assert.Equal(-3.0, tree.Location.Y, 6);
            Assert.Equal("row 2", tree.Id);
        }

        [Theory]
        [InlineData("12,5", true, 12.5)]
        [InlineData("12.5", true, 12.5)]
        [InlineData("1e3", false, 1000)]
        public void Numbers_accept_decimal_comma_only_when_allowed(string text, bool allowComma, double expected)
        {
            Assert.True(TreeTableImport.TryNumber(text, allowComma, out var v));
            Assert.Equal(expected, v, 6);
        }

        [Theory]
        [InlineData("12,5")]
        [InlineData("1,234.5")]
        [InlineData("NaN")]
        public void Numbers_reject_ambiguous_text_without_decimal_comma(string text)
        {
            Assert.False(TreeTableImport.TryNumber(text, allowDecimalComma: false, out _));
        }
    }
}
