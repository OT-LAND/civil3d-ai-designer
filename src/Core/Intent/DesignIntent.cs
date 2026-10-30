using System.Collections.Generic;

namespace CivilAI.Core.Intent
{
    // Design Intent v1 (roadmap 1.1, extended by B.3).
    //
    // The structured description of a landscape design that Claude produces and that the
    // deterministic Civil 3D builders consume. It speaks the designer's language (spaces,
    // levels, edges, materials), not Civil 3D object types.
    //
    // Units and coordinates: metres, in the drawing's coordinate system (X east, Y north,
    // Z = absolute level). Which coordinate system that is (ITM or local) is open in D4b;
    // the intent does not assume one.
    //
    // The JSON form is described by Intent/Schema/design-intent.v1.schema.json. Keep the
    // two in step: DesignIntentTests checks the enum lists and property names match.
    //
    // v1 adds (all optional, so v0 documents still read): zone and interest-point references
    // back to the Design Brief, trees (new and kept), drainage (swales, basins, low points) and
    // site elements. A document marked "0" must not use the v1 parts.

    /// <summary>Root of a Design Intent document.</summary>
    public sealed class DesignIntent
    {
        public const string CurrentSchemaVersion = "1";

        /// <summary>Schema versions this code reads. v1 is a superset of v0.</summary>
        public static readonly IReadOnlyList<string> SupportedSchemaVersions = new[] { "0", "1" };

        public string SchemaVersion { get; init; } = CurrentSchemaVersion;

        public Site Site { get; init; } = new Site();

        public List<Space> Spaces { get; init; } = new List<Space>();

        public List<Edge> Edges { get; init; } = new List<Edge>();

        public List<Material> Materials { get; init; } = new List<Material>();

        /// <summary>Trees: new ones the design plants and existing ones it keeps (v1).</summary>
        public List<TreeIntent> Trees { get; init; } = new List<TreeIntent>();

        /// <summary>Surface drainage: swales, retention/infiltration basins, low points (v1).</summary>
        public DrainageIntent Drainage { get; init; } = new DrainageIntent();

        /// <summary>Site elements: seating, lighting, play, shade, water, art (v1).</summary>
        public List<SiteElement> Elements { get; init; } = new List<SiteElement>();
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

        /// <summary>
        /// Path only. Every path is an accessible route unless the designer marks it
        /// <c>false</c> (D11); omitted means accessible. A path marked false is skipped by
        /// the accessible-route rules; other rules still apply.
        /// </summary>
        public bool? Accessible { get; init; }

        public LevelIntent? Level { get; init; }

        /// <summary>Id of a <see cref="Material"/> in <see cref="DesignIntent.Materials"/>.</summary>
        public string? MaterialId { get; init; }

        /// <summary>v1: id of the Design Brief zone this space was planned in (not checked against the intent).</summary>
        public string? ZoneId { get; init; }

        /// <summary>
        /// v1: id of the Design Brief interest point this space is the place for ("seat under the
        /// tree"). The space is the special place designed around that point.
        /// </summary>
        public string? InterestPointId { get; init; }
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

    /// <summary>
    /// Cost class of a material, a planted tree or a site element (D20): what it costs relative
    /// to the alternatives for the same job (gravel low, concrete pavers mid, natural stone high).
    /// Measured against the budget slider as the cost level (B.1b).
    /// </summary>
    public enum CostClass
    {
        Low,
        Mid,
        High,
    }

    /// <summary>A surface material. Office libraries replace these with catalogue ids later (5.1).</summary>
    public sealed class Material
    {
        public string Id { get; init; } = "";

        public string Name { get; init; } = "";

        public MaterialCategory Category { get; init; }

        /// <summary>Cost class (D20). Optional; spaces with an unclassed material are left out of the cost level.</summary>
        public CostClass? CostClass { get; init; }
    }

    public enum TreeStatus
    {
        /// <summary>A tree the design plants.</summary>
        New,
        /// <summary>An existing (surveyed) tree the design keeps, with its protection zone.</summary>
        Existing,
    }

    /// <summary>A tree in the design (v1). Sizes in metres.</summary>
    public sealed class TreeIntent
    {
        public string Id { get; init; } = "";

        public TreeStatus Status { get; init; }

        /// <summary>Trunk position.</summary>
        public Point2 Location { get; init; }

        /// <summary>Species (botanical name preferred). The AI picks it from the plant list unless the designer gave one.</summary>
        public string? Species { get; init; }

        /// <summary>Canopy diameter: at maturity for a new tree, as surveyed for an existing one. Used for canopy cover.</summary>
        public double CanopyDiameter { get; init; }

        /// <summary>Trunk diameter at 1.3 m (D12), metres.</summary>
        public double? TrunkDiameter { get; init; }

        /// <summary>Existing trees only: radius of the root protection zone around the trunk.</summary>
        public double? ProtectionRadius { get; init; }

        /// <summary>
        /// Where the tree came from: the survey tree id for an existing tree (required), or the
        /// brief's tree point id for a new tree the designer asked for.
        /// </summary>
        public string? SourceId { get; init; }

        /// <summary>Design Brief zone id.</summary>
        public string? ZoneId { get; init; }

        /// <summary>New trees only: cost class of the size at planting (D20; low = small stock, high = semi-mature).</summary>
        public CostClass? CostClass { get; init; }
    }

    /// <summary>Surface drainage intent (v1). Pipes come later (4.x).</summary>
    public sealed class DrainageIntent
    {
        public List<Swale> Swales { get; init; } = new List<Swale>();

        public List<DrainageBasin> Basins { get; init; } = new List<DrainageBasin>();

        public List<LowPoint> LowPoints { get; init; } = new List<LowPoint>();
    }

    /// <summary>A swale or flow line, drawn from its high end to its low end (D17).</summary>
    public sealed class Swale
    {
        public string Id { get; init; } = "";

        /// <summary>Flow line, high end first.</summary>
        public List<Point2> Line { get; init; } = new List<Point2>();

        /// <summary>Top width, metres.</summary>
        public double? Width { get; init; }

        /// <summary>Depth below the surrounding ground, metres.</summary>
        public double? Depth { get; init; }

        /// <summary>Design Brief flow-basin curve this swale follows.</summary>
        public string? FlowBasinId { get; init; }

        /// <summary>Id of the <see cref="DrainageBasin"/> or <see cref="LowPoint"/> it drains into.</summary>
        public string? OutletId { get; init; }
    }

    public enum BasinKind
    {
        /// <summary>Holds water and releases it slowly.</summary>
        Retention,
        /// <summary>Lets water soak into the ground.</summary>
        Infiltration,
    }

    /// <summary>A retention or infiltration area, usually at the low end of a flow basin (D17).</summary>
    public sealed class DrainageBasin
    {
        public string Id { get; init; } = "";

        public BasinKind Kind { get; init; }

        /// <summary>Top outline, closed polygon.</summary>
        public List<Point2> Outline { get; init; } = new List<Point2>();

        /// <summary>Bottom level, absolute, metres.</summary>
        public double? BottomLevel { get; init; }

        /// <summary>Depth below the rim, metres.</summary>
        public double? Depth { get; init; }

        /// <summary>Design Brief flow-basin curve this basin ends.</summary>
        public string? FlowBasinId { get; init; }
    }

    /// <summary>A low point surface water runs to; with <see cref="Inlet"/> it is a drain into pipes.</summary>
    public sealed class LowPoint
    {
        public string Id { get; init; } = "";

        public Point2 Location { get; init; }

        /// <summary>Level, absolute, metres. Omit to let grading set it.</summary>
        public double? Level { get; init; }

        /// <summary>True for an inlet/drain (a pipe network node later); false for a surface low point.</summary>
        public bool Inlet { get; init; }
    }

    public enum ElementKind
    {
        Seating,
        Lighting,
        Play,
        Shade,
        Pergola,
        Water,
        Art,
        Other,
    }

    /// <summary>A site element (v1), later an attributed block from the office library (5.1).</summary>
    public sealed class SiteElement
    {
        public string Id { get; init; } = "";

        public ElementKind Kind { get; init; }

        public string? Name { get; init; }

        /// <summary>Insertion point.</summary>
        public Point2 Location { get; init; }

        /// <summary>
        /// Footprint, closed polygon. Required for shade and pergola (their cover counts toward
        /// the shading target); optional for the rest.
        /// </summary>
        public List<Point2>? Outline { get; init; }

        /// <summary>Rotation, degrees counter-clockwise from east.</summary>
        public double? RotationDegrees { get; init; }

        /// <summary>Design Brief zone id.</summary>
        public string? ZoneId { get; init; }

        /// <summary>Design Brief interest point this element marks or serves.</summary>
        public string? InterestPointId { get; init; }

        /// <summary>Cost class of the element's quality (D20). Optional.</summary>
        public CostClass? CostClass { get; init; }
    }

    /// <summary>A plan point in metres.</summary>
    public readonly record struct Point2(double X, double Y);
}
