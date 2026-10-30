using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Style
{
    /// <summary>Reads and writes Style Profile JSON (same conventions as Design Intent JSON).</summary>
    public static class StyleProfileJson
    {
        public static JsonSerializerOptions Options => DesignIntentJson.Options;

        public static string Serialize(StyleProfile profile) => JsonSerializer.Serialize(profile, Options);

        /// <summary>Parses JSON into a Style Profile; false with a readable error when the shape is wrong.</summary>
        public static bool TryParse(string json, out StyleProfile? profile, out string? error)
        {
            profile = null;
            error = null;
            try
            {
                profile = JsonSerializer.Deserialize<StyleProfile>(json, Options);
                if (profile is null)
                {
                    error = "The style profile is empty (null).";
                    return false;
                }
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Path is null ? ex.Message : $"{ex.Path}: {ex.Message}";
                return false;
            }
        }

        /// <summary>The JSON Schema for Style Profile v1, embedded in Core (also the Claude output schema).</summary>
        public static string ReadSchema()
        {
            const string name = "CivilAI.Core.Style.Schema.style-profile.v1.schema.json";
            using var stream = typeof(StyleProfileJson).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>Content checks the schema cannot express. Issues reuse <see cref="IntentIssue"/>.</summary>
    public static class StyleProfileValidator
    {
        public const int MaxMaterials = 12;

        /// <param name="imageCount">Reference images the profile was read from; image notes may not exceed it.</param>
        public static IReadOnlyList<IntentIssue> Validate(StyleProfile profile, int? imageCount = null)
        {
            var issues = new List<IntentIssue>();
            void Add(string path, string message) => issues.Add(new IntentIssue(path, message));

            if (profile.SchemaVersion != "1")
                Add("schemaVersion", $"Unknown style profile version \"{profile.SchemaVersion}\"; expected \"1\".");
            if (string.IsNullOrWhiteSpace(profile.Summary))
                Add("summary", "Write one or two sentences that sum up the style.");

            if (profile.Materials.Count == 0)
                Add("materials", "Give at least one material.");
            if (profile.Materials.Count > MaxMaterials)
                Add("materials", $"Up to {MaxMaterials} materials; this profile has {profile.Materials.Count}. Keep the main ones.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profile.Materials.Count; i++)
            {
                var m = profile.Materials[i];
                if (string.IsNullOrWhiteSpace(m.Id))
                    Add($"materials[{i}].id", "Material id is empty.");
                else if (!ids.Add(m.Id))
                    Add($"materials[{i}].id", $"Material id \"{m.Id}\" is used twice.");
                if (string.IsNullOrWhiteSpace(m.Name))
                    Add($"materials[{i}].name", "Material name is empty.");
            }

            var planting = profile.Planting ?? new PlantingCharacter();
            if (planting.Layers.Count == 0)
                Add("planting.layers", "Give at least one planting layer.");
            if (planting.Layers.Distinct().Count() != planting.Layers.Count)
                Add("planting.layers", "A planting layer is listed twice.");

            var form = profile.Form ?? new FormLanguage();
            if (form.CurvedShare < 0 || form.CurvedShare > 100)
                Add("form.curvedShare", $"Must be 0–100; got {form.CurvedShare}.");

            if (imageCount is int n && profile.ImageNotes.Count > n)
                Add("imageNotes", $"{profile.ImageNotes.Count} image notes for {n} image(s); give one note per image.");

            return issues;
        }
    }
}
