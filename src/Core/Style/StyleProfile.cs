using System.Collections.Generic;
using System.Linq;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Style
{
    /// <summary>
    /// Roadmap B.4 / CONCEPT §3.3 stage 2: what the Style panel (reference images + prompt) means
    /// for the design — a material palette, the planting character and the form language.
    /// Read once by Claude (vision), cached, and handed to the Plan stage (B.5) on every run.
    /// </summary>
    public sealed class StyleProfile
    {
        public string SchemaVersion { get; init; } = "1";

        /// <summary>One or two sentences in the designer's language ("a shaded, stony Mediterranean courtyard").</summary>
        public string Summary { get; init; } = "";

        /// <summary>Surface materials the style asks for, most used first.</summary>
        public List<StyleMaterial> Materials { get; init; } = new List<StyleMaterial>();

        public PlantingCharacter Planting { get; init; } = new PlantingCharacter();

        public FormLanguage Form { get; init; } = new FormLanguage();

        /// <summary>One short note per reference image, in the order given (what was taken from it).</summary>
        public List<string> ImageNotes { get; init; } = new List<string>();

        /// <summary>The palette as Design Intent materials (id, name, category), for the Plan stage.</summary>
        public List<Material> ToIntentMaterials() =>
            Materials.Select(m => new Material { Id = m.Id, Name = m.Name, Category = m.Category }).ToList();
    }

    /// <summary>A material of the palette. Office libraries replace these with catalogue ids later (5.1).</summary>
    public sealed class StyleMaterial
    {
        /// <summary>Short readable id ("stone-paving"); becomes the Design Intent material id.</summary>
        public string Id { get; init; } = "";

        /// <summary>Designer's name ("local limestone, sawn, 60×40").</summary>
        public string Name { get; init; } = "";

        public MaterialCategory Category { get; init; }

        /// <summary>Colour in words ("warm buff", "charcoal").</summary>
        public string? Colour { get; init; }

        /// <summary>Surface finish ("sawn", "tumbled", "exposed aggregate").</summary>
        public string? Finish { get; init; }

        /// <summary>Where it goes ("main plaza", "path edges", "seat wall caps").</summary>
        public string? Use { get; init; }
    }

    public enum WaterUse { Low, Medium, High }

    public enum PlantingDensity { Sparse, Medium, Dense }

    public enum PlantLayer { Trees, Shrubs, Perennials, Grasses, Groundcover, Climbers }

    /// <summary>The planting character: how much water, how full, which layers, what it looks like.</summary>
    public sealed class PlantingCharacter
    {
        public WaterUse WaterUse { get; init; } = WaterUse.Medium;

        public PlantingDensity Density { get; init; } = PlantingDensity.Medium;

        /// <summary>Layers the style uses, main layer first.</summary>
        public List<PlantLayer> Layers { get; init; } = new List<PlantLayer>();

        /// <summary>Colours and textures in words ("silver foliage", "fine grasses").</summary>
        public List<string> Palette { get; init; } = new List<string>();

        /// <summary>
        /// Example genera or species that fit the character. Suggestions only: species are chosen
        /// later from the user's plant list (D5).
        /// </summary>
        public List<string> Examples { get; init; } = new List<string>();
    }

    public enum FormGeometry { Orthogonal, Angular, Curvilinear, Mixed }

    /// <summary>The form language: the geometry of spaces and edges.</summary>
    public sealed class FormLanguage
    {
        public FormGeometry Geometry { get; init; } = FormGeometry.Mixed;

        /// <summary>
        /// How curved the layout reads, 0 = all straight … 100 = all curved, on the same scale as
        /// the curved↔straight slider. The brief's slider stays the target; this is only the style's lean.
        /// </summary>
        public int CurvedShare { get; init; } = 50;

        /// <summary>Edges in words ("flush stone edges, no kerbs", "steel-edged beds").</summary>
        public string? Edges { get; init; }

        /// <summary>Level changes in words ("low terraces with seat walls", "gentle landform").</summary>
        public string? LevelChanges { get; init; }
    }
}
