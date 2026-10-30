using System.Collections.Generic;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Brief
{
    // Design Brief v1 (roadmap B.1, concept v1 in docs/CONCEPT.md, D14).
    //
    // What the designer wants, captured from the palette: tagged zones, curves and points,
    // character sliders, a visual style and references to background data. It is the input
    // to generation; the Design Intent (Intent/) is the output the builders consume.
    //
    // Host-neutral: metres, plan points in the drawing's coordinate system, levels absolute.
    // Each item may carry a hostId that points back to the drawing object it was picked from
    // (Civil 3D: the entity handle), so the brief can be re-read from the drawing (concept 3.4).
    //
    // The JSON form is described by Brief/Schema/design-brief.v1.schema.json. Keep the two in
    // step: DesignBriefTests checks enum lists, slider names and property names.

    /// <summary>Root of a Design Brief document.</summary>
    public sealed class DesignBrief
    {
        public const string CurrentSchemaVersion = "1";

        /// <summary>Only "m" is accepted: the capture step converts drawing units (D4b).</summary>
        public const string Metres = "m";

        public string SchemaVersion { get; init; } = CurrentSchemaVersion;

        public string Units { get; init; } = Metres;

        public BackgroundRefs Background { get; init; } = new BackgroundRefs();

        public StyleInput Style { get; init; } = new StyleInput();

        /// <summary>Global slider values; every zone uses these unless it overrides a slider (D15).</summary>
        public Character Character { get; init; } = new Character();

        public List<Zone> Zones { get; init; } = new List<Zone>();

        public List<TaggedCurve> Curves { get; init; } = new List<TaggedCurve>();

        public List<TaggedPoint> Points { get; init; } = new List<TaggedPoint>();

        /// <summary>
        /// The sliders that apply to <paramref name="zone"/>: the zone's own value where it
        /// overrides one, otherwise the global value (D15). A global slider that is missing
        /// is reported by the validator; here it falls back to <see cref="Character.DefaultValue"/>.
        /// </summary>
        public ResolvedCharacter CharacterFor(Zone zone)
        {
            var g = Character ?? new Character();
            var z = zone?.Character ?? new Character();
            int Pick(int? own, int? global) => own ?? global ?? Character.DefaultValue;
            return new ResolvedCharacter(
                Pick(z.Accessibility, g.Accessibility),
                Pick(z.HardSoft, g.HardSoft),
                Pick(z.MaxGrade, g.MaxGrade),
                Pick(z.CutFill, g.CutFill),
                Pick(z.MaxSlope, g.MaxSlope),
                Pick(z.Shading, g.Shading),
                Pick(z.TreesElements, g.TreesElements),
                Pick(z.CurvedStraight, g.CurvedStraight),
                Pick(z.Budget, g.Budget));
        }
    }

    /// <summary>
    /// Background panel: references to drawing content the generator reads as context, never edits.
    /// Each entry is a host reference such as "layer:V-TOPO-*", "xref:Survey2025" or "image:ortho.tif".
    /// Slots in palette order (D21): survey · parcel · structures · roads · utilities · orthophoto.
    /// </summary>
    public sealed class BackgroundRefs
    {
        public List<string> Survey { get; init; } = new List<string>();

        /// <summary>Lot borders and setbacks: Civil 3D parcels or closed polylines (D21).</summary>
        public List<string> Parcel { get; init; } = new List<string>();

        /// <summary>Architecture: buildings, FFLs, entrances (D21).</summary>
        public List<string> Structures { get; init; } = new List<string>();

        public List<string> Roads { get; init; } = new List<string>();

        /// <summary>Pipe networks, utility lines, manholes and easements; the generator keeps them clear (D21).</summary>
        public List<string> Utilities { get; init; } = new List<string>();

        public List<string> Orthophoto { get; init; } = new List<string>();
    }

    /// <summary>Style panel: up to <see cref="MaxImages"/> reference images and/or a text prompt.</summary>
    public sealed class StyleInput
    {
        public const int MaxImages = 6;

        public string? Prompt { get; init; }

        /// <summary>Image file paths or host references, in the order the designer placed them.</summary>
        public List<string> Images { get; init; } = new List<string>();
    }

    /// <summary>
    /// Polygon character sliders, each 0–100 between two ends (CONCEPT.md §2).
    /// Globally all nine are set; on a zone, only the overridden ones (others null).
    /// </summary>
    public sealed class Character
    {
        public const int Min = 0;
        public const int Max = 100;
        public const int DefaultValue = 50;

        /// <summary>0 legal minimum only … 100 everything step-free. Legal minimum (D10/D11) always applies.</summary>
        public int? Accessibility { get; init; }

        /// <summary>0 all paved … 100 all planted.</summary>
        public int? HardSoft { get; init; }

        /// <summary>0 gentle … 100 rule-set maximum: steepest graded ground before a retaining wall (D16).</summary>
        public int? MaxGrade { get; init; }

        /// <summary>0 export soil … 100 import soil; 50 balanced.</summary>
        public int? CutFill { get; init; }

        /// <summary>0 gentle … 100 rule-set maximum: steepest walking surface on not-accessible paths (D19).</summary>
        public int? MaxSlope { get; init; }

        /// <summary>0 open/sunny … 100 full canopy at maturity.</summary>
        public int? Shading { get; init; }

        /// <summary>0 trees only … 100 built elements only.</summary>
        public int? TreesElements { get; init; }

        /// <summary>0 organic/curvilinear … 100 orthogonal/rectilinear.</summary>
        public int? CurvedStraight { get; init; }

        /// <summary>
        /// 0 economical … 100 premium: a cost level, not a sum of money (D20). Steers materials,
        /// plant sizes at planting, walls and site elements.
        /// </summary>
        public int? Budget { get; init; }

        public static Character AllAt(int value) => new Character
        {
            Accessibility = value,
            HardSoft = value,
            MaxGrade = value,
            CutFill = value,
            MaxSlope = value,
            Shading = value,
            TreesElements = value,
            CurvedStraight = value,
            Budget = value,
        };

        /// <summary>Slider values by JSON name, in palette order (null = not set).</summary>
        public IEnumerable<(string Name, int? Value)> Sliders()
        {
            yield return ("accessibility", Accessibility);
            yield return ("hardSoft", HardSoft);
            yield return ("maxGrade", MaxGrade);
            yield return ("cutFill", CutFill);
            yield return ("maxSlope", MaxSlope);
            yield return ("shading", Shading);
            yield return ("treesElements", TreesElements);
            yield return ("curvedStraight", CurvedStraight);
            yield return ("budget", Budget);
        }
    }

    /// <summary>
    /// The nine slider values that apply to one zone after D15 overrides. Budget is last and
    /// defaults to the middle so older callers that pass eight values keep working.
    /// </summary>
    public readonly record struct ResolvedCharacter(
        int Accessibility,
        int HardSoft,
        int MaxGrade,
        int CutFill,
        int MaxSlope,
        int Shading,
        int TreesElements,
        int CurvedStraight,
        int Budget = Character.DefaultValue);

    /// <summary>Polygon panel: a design zone (closed outline) with optional slider overrides.</summary>
    public sealed class Zone
    {
        public string Id { get; init; } = "";

        public string? Name { get; init; }

        /// <summary>Closed polygon; the first point is not repeated at the end.</summary>
        public List<Point2> Outline { get; init; } = new List<Point2>();

        /// <summary>Only the sliders this zone overrides; the rest come from the global set (D15).</summary>
        public Character? Character { get; init; }

        public string? HostId { get; init; }
    }

    public enum CurveTag
    {
        /// <summary>Circulation spine; becomes paths. Accessible unless marked otherwise (D11).</summary>
        WalkingNetwork,
        /// <summary>Landform line the ground should follow; soft unless it has a level (D17).</summary>
        Contour,
        /// <summary>Swale or flow line with a retention/infiltration area at its low end (D17).</summary>
        FlowBasin,
        /// <summary>An edge to respect or build: lot border, wall, kerb, hedge line.</summary>
        Border,
    }

    /// <summary>Curves panel: an open line tagged with its intent.</summary>
    public sealed class TaggedCurve
    {
        public string Id { get; init; } = "";

        public CurveTag Tag { get; init; }

        public string? Name { get; init; }

        /// <summary>Open polyline, start to end. Arcs/splines are tessellated by the capture step.</summary>
        public List<Point2> Points { get; init; } = new List<Point2>();

        /// <summary>Contour only: a fixed level makes it a hard contour (D17).</summary>
        public double? Level { get; init; }

        /// <summary>Walking network only: false marks it not accessible (D11); omitted = accessible.</summary>
        public bool? Accessible { get; init; }

        public string? HostId { get; init; }
    }

    public enum PointTag
    {
        /// <summary>Fixed tie-in level and a node the walking network must reach.</summary>
        Entrance,
        /// <summary>Destination or focal point with a special place designed around it.</summary>
        InterestPoint,
        /// <summary>A hard spot level; needs a level.</summary>
        Elevation,
        /// <summary>"A tree here"; species from the plant list unless given (D5).</summary>
        Tree,
    }

    /// <summary>Points panel: a plan point tagged with its intent.</summary>
    public sealed class TaggedPoint
    {
        public string Id { get; init; } = "";

        public PointTag Tag { get; init; }

        public string? Name { get; init; }

        public Point2 At { get; init; }

        /// <summary>Absolute level, metres. Required for elevation points; optional threshold level for entrances.</summary>
        public double? Level { get; init; }

        /// <summary>Tree only: a species the designer fixed (otherwise the AI picks from the plant list, D5).</summary>
        public string? Species { get; init; }

        public string? HostId { get; init; }
    }
}
