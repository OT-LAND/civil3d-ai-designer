using System.Collections.Generic;

namespace CivilAI.Core.Intent
{
    // Design Intent v0 (roadmap 1.1).
    //
    // The structured description of a landscape design that Claude produces and that the
    // deterministic Civil 3D builders consume. It speaks the designer's language (spaces,
    // levels, edges, materials), not Civil 3D object types.
    //
    // Units and coordinates: metres, in the drawing's coordinate system (X east, Y north,
    // Z = absolute level). Which coordinate system that is (ITM or local) is open in D4b;
    // the intent does not assume one.
    //
    // The JSON form is described by Intent/Schema/design-intent.v0.schema.json. Keep the
    // two in step: DesignIntentSchemaTests checks the enum lists and required fields match.

    /// <summary>Root of a Design Intent document.</summary>
    public sealed class DesignIntent
    {
        public const string CurrentSchemaVersion = "0";

        public string SchemaVersion { get; init; } = CurrentSchemaVersion;

        public Site Site { get; init; } = new Site();

        public List<Space> Spaces { get; init; } = new List<Space>();

        public List<Edge> Edges { get; init; } = new List<Edge>();

        public List<Material> Materials { get; init; } = new List<Material>();
    }

    /// <summary>The site: lot border and buildings with their finished floor levels.</summary>
    public sealed class Site
    {
        public string? Name { get; init; }

        /// <summary>Lot border as a closed polygon (first point not repeated at the end).</summary>
        public List<Point2>? LotBoundary { get; init; }

        public List<Building> Buildings { get; init; } = new List<Building>();
    }

    public sealed class Building
    {
        public string Id { get; init; } = "";

        public string? Name { get; init; }

        /// <summary>Footprint as a closed polygon.</summary>
        public List<Point2> Footprint { get; init; } = new List<Point2>();

        /// <summary>Finished floor level (FFL), absolute, metres.</summary>
        public double Ffl { get; init; }

        public List<Entrance> Entrances { get; init; } = new List<Entrance>();
    }

    /// <summary>A building entrance the outdoor levels must tie into.</summary>
    public sealed class Entrance
    {
        public string Id { get; init; } = "";

        public string? Name { get; init; }

        public Point2 Location { get; init; }

        /// <summary>Threshold level; when omitted the building FFL applies.</summary>
        public double? ThresholdLevel { get; init; }

        /// <summary>True if this entrance must be reached by an accessible (step-free) route.</summary>
        public bool Accessible { get; init; }
    }

    public enum SpaceKind
    {
        Plaza,
        Path,
        Lawn,
        Bed,
    }

    /// <summary>
    /// A space as the designer thinks of it. Area spaces (plaza, lawn, bed) have an
    /// <see cref="Outline"/>; a path has a <see cref="Centerline"/> and <see cref="Width"/>.
    /// </summary>
    public sealed class Space
    {
        public string Id { get; init; } = "";

        public SpaceKind Kind { get; init; }

        public string? Name { get; init; }

        /// <summary>Closed outline polygon (plaza, lawn, bed).</summary>
        public List<Point2>? Outline { get; init; }

        /// <summary>Path centerline, start to end (path only).</summary>
        public List<Point2>? Centerline { get; init; }

        /// <summary>Path width in metres (path only).</summary>
        public double? Width { get; init; }

        public LevelIntent? Level { get; init; }

        /// <summary>Id of a <see cref="Material"/> in <see cref="DesignIntent.Materials"/>.</summary>
        public string? MaterialId { get; init; }
    }

    /// <summary>
    /// How a space sits in height: a nominal level, spot levels, and/or a fall.
    /// Any part may be omitted when the designer has not fixed it; the grading step fills it.
    /// </summary>
    public sealed class LevelIntent
    {
        /// <summary>Nominal (typical) level of the space, absolute, metres.</summary>
        public double? Elevation { get; init; }

        public List<SpotLevel> Spots { get; init; } = new List<SpotLevel>();

        public Fall? Fall { get; init; }
    }

    /// <summary>A fixed level at a point ("+12.40 at the door").</summary>
    public sealed class SpotLevel
    {
        public double X { get; init; }

        public double Y { get; init; }

        public double Z { get; init; }

        public string? Note { get; init; }
    }

    /// <summary>A cross fall / drainage fall: slope in percent, falling towards a compass direction.</summary>
    public sealed class Fall
    {
        /// <summary>Fall in percent (2.0 = 2%). Positive.</summary>
        public double Percent { get; init; }

        /// <summary>Direction the surface falls towards, degrees counter-clockwise from east (AutoCAD angle).</summary>
        public double DirectionDegrees { get; init; }
    }

    public enum EdgeKind
    {
        /// <summary>Flush joint between two surfaces, no level change.</summary>
        Flush,
        /// <summary>Kerb / edging with a small upstand.</summary>
        Kerb,
        /// <summary>Steps taking up a level change.</summary>
        Steps,
        /// <summary>Retaining or seat wall.</summary>
        Wall,
    }

    /// <summary>A line where spaces meet or a space ends, and how the level change is made.</summary>
    public sealed class Edge
    {
        public string Id { get; init; } = "";

        public EdgeKind Kind { get; init; }

        /// <summary>The edge line, start to end (open polyline).</summary>
        public List<Point2> Line { get; init; } = new List<Point2>();

        /// <summary>One or two spaces this edge bounds.</summary>
        public List<string> SpaceIds { get; init; } = new List<string>();

        /// <summary>Upstand / level difference in metres (kerb upstand, total step rise, wall retained height).</summary>
        public double? Height { get; init; }
    }

    public enum MaterialCategory
    {
        Paving,
        Gravel,
        Lawn,
        Planting,
        Mulch,
        Other,
    }

    /// <summary>A surface material. Office libraries replace these with catalogue ids later (5.1).</summary>
    public sealed class Material
    {
        public string Id { get; init; } = "";

        public string Name { get; init; } = "";

        public MaterialCategory Category { get; init; }
    }

    /// <summary>A plan point in metres.</summary>
    public readonly record struct Point2(double X, double Y);
}
