using System;
using System.IO;
using System.Text.Json;
using CivilAI.Core.Intent;

namespace CivilAI.Core.Brief
{
    /// <summary>Reads and writes Design Brief JSON. Same conventions as Design Intent JSON (camelCase, enums as strings).</summary>
    public static class DesignBriefJson
    {
        public static JsonSerializerOptions Options => DesignIntentJson.Options;

        public static string Serialize(DesignBrief brief) => JsonSerializer.Serialize(brief, Options);

        /// <summary>
        /// Parses JSON into a Design Brief. Returns false with a readable error when the text is not
        /// valid JSON or does not match the shape (unknown field, unknown tag, wrong type).
        /// Run <see cref="DesignBriefValidator"/> afterwards for content checks.
        /// </summary>
        public static bool TryParse(string json, out DesignBrief? brief, out string? error)
        {
            brief = null;
            error = null;
            try
            {
                brief = JsonSerializer.Deserialize<DesignBrief>(json, Options);
                if (brief is null)
                {
                    error = "The Design Brief is empty (null).";
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

        /// <summary>The JSON Schema (draft 2020-12) for Design Brief v1, embedded in Core.</summary>
        public static string ReadSchema()
        {
            const string name = "CivilAI.Core.Brief.Schema.design-brief.v1.schema.json";
            using var stream = typeof(DesignBriefJson).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
