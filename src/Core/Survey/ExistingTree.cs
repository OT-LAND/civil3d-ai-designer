using CivilAI.Core.Intent;

namespace CivilAI.Core.Survey
{
    // Existing trees from the survey (roadmap 2.1, D4b). Core works in metres; every size here
    // has already been converted from the units the designer confirmed for the source table.

    /// <summary>An existing tree as surveyed: where it stands, what it is and how big it is.</summary>
    public sealed class ExistingTree
    {
        /// <summary>Tree number from the survey, or "row N" when the table has none.</summary>
        public string Id { get; init; } = "";

        /// <summary>Species as written in the survey (botanical or common name).</summary>
        public string? Species { get; init; }

        /// <summary>Trunk position, in the drawing's coordinate system, metres.</summary>
        public Point2 Location { get; init; }

        /// <summary>Trunk diameter (usually measured at 1.3 m), metres.</summary>
        public double? TrunkDiameter { get; init; }

        /// <summary>Canopy (crown) diameter, metres.</summary>
        public double? CanopyDiameter { get; init; }

        /// <summary>Tree height, metres.</summary>
        public double? Height { get; init; }

        /// <summary>Ground level at the trunk, absolute, metres.</summary>
        public double? GroundLevel { get; init; }
    }
}
