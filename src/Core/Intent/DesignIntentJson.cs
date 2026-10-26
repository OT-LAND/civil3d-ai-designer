using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivilAI.Core.Intent
{
    /// <summary>Reads and writes Design Intent JSON (camelCase, enums as camelCase strings).</summary>
    public static class DesignIntentJson
    {
        public static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            return options;
        }

        public static string Serialize(DesignIntent intent) => JsonSerializer.Serialize(intent, Options);

        /// <summary>
        /// Parses JSON into a Design Intent. Returns false with a readable error when the text
        /// is not valid JSON or does not match the shape (unknown field, unknown kind, wrong type).
        /// Run <see cref="DesignIntentValidator"/> afterwards for content checks.
        /// </summary>
        public static bool TryParse(string json, out DesignIntent? intent, out string? error)
        {
            intent = null;
            error = null;
            try
            {
                intent = JsonSerializer.Deserialize<DesignIntent>(json, Options);
                if (intent is null)
                {
                    error = "The Design Intent is empty (null).";
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

        /// <summary>The JSON Schema (draft 2020-12) for Design Intent v1, embedded in Core.</summary>
        public static string ReadSchema()
        {
            const string name = "CivilAI.Core.Intent.Schema.design-intent.v1.schema.json";
            using var stream = typeof(DesignIntentJson).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
