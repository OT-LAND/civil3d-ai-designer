using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Survey
{
    // Existing-tree table import (roadmap 2.1a, D4b option c: trees as a separate table).
    //
    // Flow: CsvTable.Parse (or XlsxTable.Read for .xlsx, 2.1c) → TreeFieldMapping.Suggest (designer confirms/corrects which column is
    // what) → TreeTableUnits.Suggest (units read from headers like "Trunk (cm)") → Import.
    // Units and the coordinate system are never assumed: if one is unknown the import returns a
    // question for the designer and no trees (D4b).

    public enum TreeField
    {
        Id,
        Species,
        X,
        Y,
        TrunkDiameter,
        CanopyDiameter,
        Height,
        GroundLevel,
    }

    public enum LengthUnit
    {
        Metre,
        Centimetre,
        Millimetre,
    }

    public static class LengthUnits
    {
        public static double ToMetres(this LengthUnit unit, double value) => unit switch
        {
            LengthUnit.Metre => value,
            LengthUnit.Centimetre => value / 100.0,
            LengthUnit.Millimetre => value / 1000.0,
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };

        /// <summary>Reads "m", "cm", "mm" (also "מ", "ס\"מ", "מ\"מ").</summary>
        public static bool TryParse(string? text, out LengthUnit unit)
        {
            unit = default;
            switch ((text ?? "").Trim().Trim('.').Replace("\"", "").Replace("'", "").ToLowerInvariant())
            {
                case "m": case "metre": case "meter": case "metres": case "meters": case "מ": case "מטר":
                    unit = LengthUnit.Metre; return true;
                case "cm": case "centimetre": case "centimeter": case "סמ": case "סנטימטר":
                    unit = LengthUnit.Centimetre; return true;
                case "mm": case "millimetre": case "millimeter": case "ממ": case "מילימטר":
                    unit = LengthUnit.Millimetre; return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Which table column holds which tree field (header text as in the file).</summary>
    public sealed class TreeFieldMapping
    {
        private static readonly Dictionary<TreeField, string[]> Aliases = new()
        {
            [TreeField.Id] = new[] { "id", "no", "number", "treeno", "treenumber", "treeid", "num", "מס", "מספר", "מספרעץ", "מסעץ" },
            [TreeField.Species] = new[] { "species", "name", "botanicalname", "latinname", "treespecies", "type", "מין", "שםהעץ", "שםמדעי", "סוג" },
            [TreeField.X] = new[] { "x", "easting", "east", "e" },
            [TreeField.Y] = new[] { "y", "northing", "north", "n" },
            [TreeField.TrunkDiameter] = new[] { "trunk", "trunkdiameter", "dbh", "stem", "stemdiameter", "trunkdia", "קוטרגזע", "גזע" },
            [TreeField.CanopyDiameter] = new[] { "canopy", "canopydiameter", "crown", "crowndiameter", "spread", "קוטרנוף", "נוף" },
            [TreeField.Height] = new[] { "height", "treeheight", "h", "גובה", "גובהעץ" },
            [TreeField.GroundLevel] = new[] { "z", "level", "groundlevel", "elevation", "elev", "gl", "רום", "מפלס", "גובהקרקע" },
        };

        public Dictionary<TreeField, string> Columns { get; init; } = new();

        /// <summary>
        /// Proposes a mapping from header names (English and Hebrew survey headings, unit
        /// suffixes ignored). A proposal only: the designer confirms it before import.
        /// </summary>
        public static TreeFieldMapping Suggest(IEnumerable<string> headers)
        {
            var mapping = new TreeFieldMapping();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = headers.ToList();
            foreach (var field in Aliases.Keys)
            {
                foreach (var header in list)
                {
                    if (used.Contains(header)) continue;
                    if (!Aliases[field].Contains(Normalize(StripUnit(header, out _)))) continue;
                    mapping.Columns[field] = header;
                    used.Add(header);
                    break;
                }
            }
            return mapping;
        }

        /// <summary>Header text without a trailing unit like "(cm)" or "[m]"; the unit, if any, is returned.</summary>
        internal static string StripUnit(string header, out LengthUnit? unit)
        {
            unit = null;
            var h = header.Trim();
            if (h.Length > 2 && (h.EndsWith(")") || h.EndsWith("]")))
            {
                var open = h.LastIndexOfAny(new[] { '(', '[' });
                if (open > 0)
                {
                    if (LengthUnits.TryParse(h.Substring(open + 1, h.Length - open - 2), out var u)) unit = u;
                    return h.Substring(0, open).Trim();
                }
            }
            return h;
        }

        internal static string Normalize(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }
    }

    /// <summary>Units of the table's values, and the coordinate system its X/Y are in.</summary>
    public sealed class TreeTableUnits
    {
        public LengthUnit? Coordinates { get; init; }
        public LengthUnit? TrunkDiameter { get; init; }
        public LengthUnit? CanopyDiameter { get; init; }
        public LengthUnit? Height { get; init; }
        public LengthUnit? GroundLevel { get; init; }

        /// <summary>
        /// The coordinate system the X/Y are in, as the designer names it (e.g. "ITM", "local");
        /// it must match the drawing's. Free text in v0; checked against the drawing in the plugin.
        /// </summary>
        public string? CoordinateSystem { get; init; }

        /// <summary>Units stated in the column headers ("Trunk (cm)"). Anything not stated stays unknown.</summary>
        public static TreeTableUnits Suggest(TreeFieldMapping mapping, string? coordinateSystem = null)
        {
            LengthUnit? From(TreeField f)
            {
                if (!mapping.Columns.TryGetValue(f, out var header)) return null;
                TreeFieldMapping.StripUnit(header, out var unit);
                return unit;
            }
            var x = From(TreeField.X);
            var y = From(TreeField.Y);
            return new TreeTableUnits
            {
                Coordinates = x == y ? x : null,
                TrunkDiameter = From(TreeField.TrunkDiameter),
                CanopyDiameter = From(TreeField.CanopyDiameter),
                Height = From(TreeField.Height),
                GroundLevel = From(TreeField.GroundLevel),
                CoordinateSystem = coordinateSystem,
            };
        }
    }

    public sealed class TreeImportProblem
    {
        public TreeImportProblem(int line, string message, bool rowSkipped)
        {
            Line = line;
            Message = message;
            RowSkipped = rowSkipped;
        }

        /// <summary>1-based line in the file.</summary>
        public int Line { get; }

        public string Message { get; }

        /// <summary>True if the whole tree was left out; false if only one value was dropped or flagged.</summary>
        public bool RowSkipped { get; }

        public override string ToString() => $"Line {Line}: {Message}";
    }

    public sealed class TreeImportResult
    {
        /// <summary>Questions the designer must answer before anything is imported. Non-empty ⇒ no trees.</summary>
        public List<string> Questions { get; } = new();

        public List<ExistingTree> Trees { get; } = new();

        public List<TreeImportProblem> Problems { get; } = new();

        public bool NeedsAnswers => Questions.Count > 0;
    }

    public static class TreeTableImport
    {
        // Sizes beyond these (after unit conversion) almost always mean a wrong unit.
        internal const double MaxPlausibleTrunk = 5.0;
        internal const double MaxPlausibleCanopy = 60.0;
        internal const double MaxPlausibleHeight = 120.0;

        private static readonly Dictionary<TreeField, string> FieldNames = new()
        {
            [TreeField.Id] = "tree number",
            [TreeField.Species] = "species",
            [TreeField.X] = "X (easting)",
            [TreeField.Y] = "Y (northing)",
            [TreeField.TrunkDiameter] = "trunk diameter",
            [TreeField.CanopyDiameter] = "canopy diameter",
            [TreeField.Height] = "height",
            [TreeField.GroundLevel] = "ground level",
        };

        public static TreeImportResult Import(CsvTable table, TreeFieldMapping mapping, TreeTableUnits units)
        {
            if (table is null) throw new ArgumentNullException(nameof(table));
            if (mapping is null) throw new ArgumentNullException(nameof(mapping));
            if (units is null) throw new ArgumentNullException(nameof(units));

            var result = new TreeImportResult();
            var col = new Dictionary<TreeField, int>();

            foreach (var pair in mapping.Columns)
            {
                var index = table.IndexOf(pair.Value);
                if (index < 0) result.Questions.Add($"The {FieldNames[pair.Key]} column \"{pair.Value}\" is not in the table. Which column holds the {FieldNames[pair.Key]}?");
                else col[pair.Key] = index;
            }
            foreach (var required in new[] { TreeField.X, TreeField.Y })
                if (!mapping.Columns.ContainsKey(required))
                    result.Questions.Add($"Which column holds the tree {FieldNames[required]}?");

            AskUnit(result, mapping, TreeField.X, units.Coordinates, "the X/Y coordinates");
            AskUnit(result, mapping, TreeField.TrunkDiameter, units.TrunkDiameter, "trunk diameter");
            AskUnit(result, mapping, TreeField.CanopyDiameter, units.CanopyDiameter, "canopy diameter");
            AskUnit(result, mapping, TreeField.Height, units.Height, "tree height");
            AskUnit(result, mapping, TreeField.GroundLevel, units.GroundLevel, "ground level");
            if (string.IsNullOrWhiteSpace(units.CoordinateSystem))
                result.Questions.Add("Which coordinate system are the tree X/Y in (for example ITM, or a local site grid)? It must match the drawing.");

            if (result.NeedsAnswers) return result;

            var decimalComma = table.AllowDecimalComma;
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                string Cell(TreeField f) => col.TryGetValue(f, out var i) ? row[i] : "";

                var xOk = TryNumber(Cell(TreeField.X), decimalComma, out var x);
                var yOk = TryNumber(Cell(TreeField.Y), decimalComma, out var y);
                if (!xOk || !yOk)
                {
                    result.Problems.Add(new TreeImportProblem(row.Line,
                        $"no usable position (X \"{Cell(TreeField.X)}\", Y \"{Cell(TreeField.Y)}\"); tree left out.", rowSkipped: true));
                    continue;
                }

                var id = Cell(TreeField.Id);
                if (string.IsNullOrWhiteSpace(id)) id = $"row {row.Line}";
                else if (!seenIds.Add(id))
                    result.Problems.Add(new TreeImportProblem(row.Line, $"tree number \"{id}\" appears more than once; both trees kept, please check.", rowSkipped: false));

                var species = Cell(TreeField.Species);
                result.Trees.Add(new ExistingTree
                {
                    Id = id,
                    Species = string.IsNullOrWhiteSpace(species) ? null : species,
                    Location = new Point2(units.Coordinates!.Value.ToMetres(x), units.Coordinates!.Value.ToMetres(y)),
                    TrunkDiameter = Size(result, row, TreeField.TrunkDiameter, Cell(TreeField.TrunkDiameter), units.TrunkDiameter, MaxPlausibleTrunk, decimalComma),
                    CanopyDiameter = Size(result, row, TreeField.CanopyDiameter, Cell(TreeField.CanopyDiameter), units.CanopyDiameter, MaxPlausibleCanopy, decimalComma),
                    Height = Size(result, row, TreeField.Height, Cell(TreeField.Height), units.Height, MaxPlausibleHeight, decimalComma),
                    GroundLevel = Level(result, row, Cell(TreeField.GroundLevel), units.GroundLevel, decimalComma),
                });
            }
            return result;
        }

        private static void AskUnit(TreeImportResult result, TreeFieldMapping mapping, TreeField field, LengthUnit? unit, string what)
        {
            if (mapping.Columns.ContainsKey(field) && unit is null)
                result.Questions.Add($"What unit is the {what} in: metres, centimetres or millimetres?");
        }

        private static double? Size(TreeImportResult result, CsvRow row, TreeField field, string text, LengthUnit? unit, double maxPlausible, bool decimalComma)
        {
            if (string.IsNullOrWhiteSpace(text) || unit is null) return null;
            if (!TryNumber(text, decimalComma, out var raw))
            {
                result.Problems.Add(new TreeImportProblem(row.Line, $"{FieldNames[field]} \"{text}\" is not a number; left empty.", rowSkipped: false));
                return null;
            }
            var metres = unit.Value.ToMetres(raw);
            if (!(metres > 0))
            {
                result.Problems.Add(new TreeImportProblem(row.Line, $"{FieldNames[field]} {text} is not a positive size; left empty.", rowSkipped: false));
                return null;
            }
            if (metres > maxPlausible)
                result.Problems.Add(new TreeImportProblem(row.Line,
                    $"{FieldNames[field]} {metres.ToString("0.##", CultureInfo.InvariantCulture)} m looks too large; is the unit right?", rowSkipped: false));
            return metres;
        }

        private static double? Level(TreeImportResult result, CsvRow row, string text, LengthUnit? unit, bool decimalComma)
        {
            if (string.IsNullOrWhiteSpace(text) || unit is null) return null;
            if (TryNumber(text, decimalComma, out var raw)) return unit.Value.ToMetres(raw);
            result.Problems.Add(new TreeImportProblem(row.Line, $"ground level \"{text}\" is not a number; left empty.", rowSkipped: false));
            return null;
        }

        /// <summary>Invariant number; with a non-comma separator a lone decimal comma ("12,5") is accepted too.</summary>
        public static bool TryNumber(string text, bool allowDecimalComma, out double value)
        {
            value = 0;
            var s = (text ?? "").Trim();
            if (s.Length == 0) return false;
            if (allowDecimalComma && s.Count(c => c == ',') == 1 && !s.Contains('.')) s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
